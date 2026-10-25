import { useCallback, useEffect, useState } from 'react'
import { get, post, put } from './api'
import { ErrorBox, ExportButton, FlowStrip, Modal, navStore, PartnerSelect, useAction, useCatalog } from './common'
import { formatDate, formatMoney, parseMoney, parseRate, today } from './money'

interface Tier { threshold: string; percent: string }
interface Agreement { id: string; kind: string; partnerId: string; partnerCode: string; partnerName: string; code: string; name: string; fromDate: string; toDate: string; isActive: boolean; rowVersion: number
  tiers: Tier[]; runId: string | null; runAmount: string | null }
interface Share { invoiceId: string; number: string; invoiceSeries: string; invoiceNo: string; postingDate: string; sales: string; discount: string }
interface Preview { agreementId: string; eligibleSales: string; percent: string; amount: string; nextThreshold: string | null; nextPercent: string | null; settled: boolean; invoices: Share[] }
interface ReportRow { partnerId: string; partnerCode: string; partnerName: string; gross: string; onInvoiceTradeDiscount: string; afterSaleTradeDiscount: string; allowance: string; returns: string; increase: string; net: string; paymentDiscount: string }
interface Report { rows: ReportRow[]; total: ReportRow }

const yearStart = () => today().slice(0, 4) + '-01-01'
const blankForm = () => ({ id: '', rowVersion: 0, partnerId: '', code: '', name: '', fromDate: yearStart(), toDate: today().slice(0, 4) + '-12-31', tiers: [{ threshold: '', percent: '' }] as Tier[] })

/**
 * Chiết khấu thương mại: (1) thỏa thuận theo doanh số có bậc, tính và quyết toán cuối kỳ thành hóa đơn điều chỉnh giảm nháp; (2) báo cáo phân biệt chiết khấu thương mại,
 * giảm giá, hàng trả lại và chiết khấu thanh toán. Chiết khấu ghi ngay trên hóa đơn nhập ở màn Hóa đơn (cột CK %); chiết khấu thanh toán ở màn Chiết khấu thanh toán.
 */
export function DiscountsView() {
  const { partners } = useCatalog()
  const [kind] = useState<'Purchase' | 'Sales'>(() => navStore.kind ?? 'Sales')   // chiều do phân hệ đang mở quyết định
  const sales = kind === 'Sales'
  const [tab, setTab] = useState<'agreements' | 'report'>('agreements')
  const [rows, setRows] = useState<Agreement[]>([])
  const [form, setForm] = useState<ReturnType<typeof blankForm> | null>(null)
  const [preview, setPreview] = useState<{ a: Agreement; p: Preview } | null>(null)
  const [settle, setSettle] = useState<{ a: Agreement; series: string; no: string; invoiceDate: string; postingDate: string } | null>(null)
  const [from, setFrom] = useState(yearStart())
  const [to, setTo] = useState(today())
  const [report, setReport] = useState<Report | null>(null)
  const [info, setInfo] = useState('')
  const { busy, error, run } = useAction()
  const load = useCallback(() => run(async () => setRows(await get<Agreement[]>(`/discounts/agreements?kind=${kind}`))), [run, kind])
  // oxlint-disable-next-line react-hooks/exhaustive-deps -- tải danh sách khi mở màn / đổi chiều
  useEffect(() => { load() }, [kind])
  const loadReport = useCallback(() => run(async () => setReport(await get<Report>(`/discounts/report?kind=${kind}&from=${from}&to=${to}`))), [run, kind, from, to])
  // oxlint-disable-next-line react-hooks/exhaustive-deps -- tải báo cáo khi mở tab / đổi kỳ
  useEffect(() => { if (tab === 'report') loadReport() }, [tab, from, to, kind])

  const tiersBody = (f: NonNullable<typeof form>) => f.tiers.filter(t => t.threshold.trim() || t.percent.trim()).map((t, i) => {
    const threshold = parseMoney(t.threshold || '0'), percent = parseRate(t.percent)
    if (threshold == null || percent == null) throw new Error(`Bậc ${i + 1}: doanh số hoặc tỷ lệ không hợp lệ.`)
    return { threshold, percent }
  })
  const save = () => run(async () => {
    if (!form) return
    const body = { kind, partnerId: form.partnerId, code: form.code.trim(), name: form.name.trim(), fromDate: form.fromDate, toDate: form.toDate, tiers: tiersBody(form) }
    if (form.id) await put(`/discounts/agreements/${form.id}`, { ...body, rowVersion: form.rowVersion })
    else await post('/discounts/agreements', body)
    setForm(null); setInfo(`Đã lưu thỏa thuận ${body.code}.`); await load()
  })
  const open = (a: Agreement) => run(async () => setPreview({ a, p: await get<Preview>(`/discounts/agreements/${a.id}/preview`) }))
  const doSettle = () => run(async () => {
    if (!settle) return
    const r = await post<{ amount: string; percent: string }>(`/discounts/agreements/${settle.a.id}/settle`, { rowVersion: settle.a.rowVersion, invoiceSeries: settle.series, firstInvoiceNo: settle.no, invoiceDate: settle.invoiceDate, postingDate: settle.postingDate })
    setSettle(null); setPreview(null)
    setInfo(`Đã quyết toán ${settle.a.code}: chiết khấu ${r.percent}% = ${formatMoney(r.amount)}. Các hóa đơn điều chỉnh nháp ở màn Hóa đơn — người có quyền ghi sổ ghi sổ từng hóa đơn.`)
    await load()
  })
  const voidRun = (a: Agreement) => run(async () => {
    const reason = window.prompt(`Lý do hủy quyết toán ${a.code} (các hóa đơn điều chỉnh nháp sẽ bị hủy):`)
    if (!reason) return
    await post(`/discounts/agreements/${a.id}/void-run`, { reason }); setInfo(`Đã hủy quyết toán ${a.code}.`); await load()
  })
  const toggle = (a: Agreement) => run(async () => { await post(`/discounts/agreements/${a.id}/active`, { rowVersion: a.rowVersion, active: !a.isActive }); await load() })

  const edit = (a: Agreement) => setForm({ id: a.id, rowVersion: a.rowVersion, partnerId: a.partnerId, code: a.code, name: a.name, fromDate: a.fromDate, toDate: a.toDate, tiers: a.tiers.map(t => ({ threshold: formatMoney(t.threshold), percent: t.percent })) })
  const updTier = (i: number, p: Partial<Tier>) => setForm(f => f && ({ ...f, tiers: f.tiers.map((t, j) => (j === i ? { ...t, ...p } : t)) }))

  return (
    <>
      <div className="toolbar"><h2>{sales ? 'Chiết khấu thương mại cho khách' : 'Chiết khấu thương mại được hưởng'}</h2>
        <button className={tab === 'agreements' ? 'primary' : ''} onClick={() => setTab('agreements')}>Thỏa thuận theo doanh số</button>
        <button className={tab === 'report' ? 'primary' : ''} onClick={() => setTab('report')}>Báo cáo chiết khấu</button></div>
      <FlowStrip steps={[
        { label: 'Chiết khấu ghi trên hóa đơn (cột CK % ở màn Hóa đơn)', journal: false, screen: 'trade', note: 'doanh thu / giá nhập đã là số sau chiết khấu' },
        { label: 'Thỏa thuận theo doanh số, quyết toán cuối kỳ', journal: false, count: `${rows.length} thỏa thuận · ${rows.filter(r => r.runId).length} đã quyết toán` },
        { label: sales ? 'Hóa đơn điều chỉnh giảm (Nợ chiết khấu thương mại, giảm thuế đầu ra)' : 'Hóa đơn điều chỉnh giảm (giảm hàng tồn kho / giá vốn, giảm thuế đầu vào)', journal: true, screen: 'trade' },
        { label: 'Chiết khấu thanh toán (trả / thu sớm — chi phí / thu nhập tài chính)', journal: true, screen: 'paydiscount' },
      ]} />
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}

      {tab === 'agreements' && <>
        <div className="toolbar"><button className="primary" onClick={() => setForm(blankForm())}>Lập thỏa thuận</button></div>
        <div className="scroll"><table className="grid">
          <thead><tr><th>Mã</th><th>{sales ? 'Khách hàng' : 'Nhà cung cấp'}</th><th>Kỳ áp dụng</th><th>Bậc chiết khấu (doanh số chưa thuế → %)</th><th>Quyết toán</th><th /></tr></thead>
          <tbody>{rows.map(a => <tr key={a.id} style={{ opacity: a.isActive ? undefined : 0.55 }}>
            <td>{a.code}<div className="muted">{a.name}{a.isActive ? '' : ' (ngừng)'}</div></td><td>{a.partnerCode} – {a.partnerName}</td><td>{formatDate(a.fromDate)} – {formatDate(a.toDate)}</td>
            <td>{a.tiers.map(t => `từ ${formatMoney(t.threshold)} → ${formatMoney(t.percent)}%`).join('; ')}</td>
            <td>{a.runId ? <>Đã quyết toán <b>{formatMoney(a.runAmount)}</b></> : <span className="muted">chưa</span>}</td>
            <td style={{ display: 'flex', gap: 4, flexWrap: 'wrap' }}>
              <button disabled={busy} onClick={() => open(a)}>Xem tính</button>
              {!a.runId && <button disabled={busy} onClick={() => edit(a)}>Sửa</button>}
              {a.runId && <button disabled={busy} onClick={() => voidRun(a)}>Hủy quyết toán</button>}
              {!a.runId && <button disabled={busy} onClick={() => toggle(a)}>{a.isActive ? 'Ngừng' : 'Áp dụng lại'}</button>}</td></tr>)}
            {rows.length === 0 && <tr><td colSpan={6} className="muted">Chưa có thỏa thuận. Lập thỏa thuận với bậc doanh số và tỷ lệ; cuối kỳ bấm “Xem tính” rồi “Quyết toán”.</td></tr>}</tbody>
        </table></div>
        <p className="muted">Doanh số = tiền hàng chưa thuế (quy ra tiền hạch toán) của hóa đơn thường đã ghi sổ trong kỳ, kể cả hóa đơn ngoại tệ (chiết khấu tính theo tiền và tỷ giá của hóa đơn), trừ hàng trả lại và giảm giá đã ghi sổ. Bậc đạt được áp cho TOÀN BỘ doanh số. Quyết toán sinh hóa đơn điều chỉnh giảm cho từng hóa đơn gốc để thuế GTGT giảm đúng thuế suất từng dòng; chiết khấu theo thỏa thuận không cần phê duyệt từng dòng (người lập thỏa thuận là người duyệt).</p>
      </>}

      {tab === 'report' && <>
        <div className="toolbar"><label>Từ ngày<input type="date" value={from} onChange={e => setFrom(e.target.value)} /></label><label>Đến ngày<input type="date" value={to} onChange={e => setTo(e.target.value)} /></label>
          <ExportButton fileName={`Bao_cao_chiet_khau_${kind}_${from}_${to}`} sheet="Chiết khấu" disabled={!report}
            columns={[{ header: 'Đối tượng', width: 32 }, { header: sales ? 'Doanh số gộp' : 'Giá trị mua gộp', numeric: true, width: 16 }, { header: 'CK thương mại trên HĐ', numeric: true, width: 16 }, { header: 'CK thương mại sau bán', numeric: true, width: 16 },
              { header: 'Giảm giá', numeric: true, width: 14 }, { header: 'Trả lại', numeric: true, width: 14 }, { header: 'Điều chỉnh tăng', numeric: true, width: 14 }, { header: sales ? 'Doanh số thuần' : 'Giá trị mua thuần', numeric: true, width: 16 }, { header: 'CK thanh toán', numeric: true, width: 14 }]}
            rows={(report ? [...report.rows, report.total] : []).map(r => [r.partnerCode ? `${r.partnerCode} – ${r.partnerName}` : 'Cộng', r.gross, r.onInvoiceTradeDiscount, r.afterSaleTradeDiscount, r.allowance, r.returns, r.increase, r.net, r.paymentDiscount])} /></div>
        <div className="scroll"><table className="grid">
          <thead><tr><th>{sales ? 'Khách hàng' : 'Nhà cung cấp'}</th><th className="num">{sales ? 'Doanh số gộp' : 'Giá trị mua gộp'}</th><th className="num">CK thương mại trên HĐ</th><th className="num">CK thương mại sau bán</th><th className="num">Giảm giá</th><th className="num">Trả lại</th><th className="num">Điều chỉnh tăng</th><th className="num">{sales ? 'Doanh số thuần' : 'Giá trị mua thuần'}</th><th className="num">CK thanh toán {sales ? '(chi phí TC)' : '(thu nhập TC)'}</th></tr></thead>
          <tbody>{(report?.rows ?? []).map(r => <tr key={r.partnerId}><td>{r.partnerCode} – {r.partnerName}</td>
            {[r.gross, r.onInvoiceTradeDiscount, r.afterSaleTradeDiscount, r.allowance, r.returns, r.increase].map((v, i) => <td key={i} className="num">{formatMoney(v)}</td>)}<td className="num"><b>{formatMoney(r.net)}</b></td><td className="num">{formatMoney(r.paymentDiscount)}</td></tr>)}
            {report && <tr><td><b>Cộng</b></td>{[report.total.gross, report.total.onInvoiceTradeDiscount, report.total.afterSaleTradeDiscount, report.total.allowance, report.total.returns, report.total.increase].map((v, i) => <td key={i} className="num"><b>{formatMoney(v)}</b></td>)}<td className="num"><b>{formatMoney(report.total.net)}</b></td><td className="num"><b>{formatMoney(report.total.paymentDiscount)}</b></td></tr>}
            {report && report.rows.length === 0 && <tr><td colSpan={9} className="muted">Không có hóa đơn đã ghi sổ trong kỳ.</td></tr>}</tbody>
        </table></div>
        <p className="muted">Mọi số là tiền hạch toán, chưa thuế, theo ngày hạch toán hóa đơn. Doanh số thuần = gộp − chiết khấu thương mại (trên hóa đơn và sau bán) − giảm giá − trả lại + điều chỉnh tăng. Chiết khấu thanh toán là khoản tài chính, KHÔNG trừ vào doanh số thuần.</p>
      </>}

      {form && <Modal title={form.id ? `Sửa thỏa thuận ${form.code}` : 'Lập thỏa thuận chiết khấu theo doanh số'} onClose={() => setForm(null)} busy={busy}
        footer={<><button onClick={() => setForm(null)}>Đóng</button><button className="primary" disabled={busy || !form.partnerId || !form.code.trim() || !form.name.trim()} onClick={save}>Lưu</button></>}>
        <div className="form-row">
          <label>{sales ? 'Khách hàng' : 'Nhà cung cấp'}<PartnerSelect partners={partners} value={form.partnerId} onChange={v => setForm({ ...form, partnerId: v })} /></label>
          <label>Mã<input value={form.code} onChange={e => setForm({ ...form, code: e.target.value })} /></label>
          <label style={{ flex: 1, minWidth: 180 }}>Tên<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} /></label>
          <label>Từ ngày<input type="date" value={form.fromDate} onChange={e => setForm({ ...form, fromDate: e.target.value })} /></label>
          <label>Đến ngày<input type="date" value={form.toDate} onChange={e => setForm({ ...form, toDate: e.target.value })} /></label>
        </div>
        <table className="grid"><thead><tr><th className="num">Doanh số từ (chưa thuế)</th><th className="num">Chiết khấu %</th><th /></tr></thead>
          <tbody>{form.tiers.map((t, i) => <tr key={i}>
            <td><input className="num" aria-label="Doanh số từ" value={t.threshold} onChange={e => updTier(i, { threshold: e.target.value })} /></td>
            <td><input className="num" aria-label="Chiết khấu %" size={6} value={t.percent} onChange={e => updTier(i, { percent: e.target.value })} /></td>
            <td><button aria-label="Xóa bậc" disabled={form.tiers.length === 1} onClick={() => setForm({ ...form, tiers: form.tiers.filter((_, j) => j !== i) })}>✕</button></td></tr>)}</tbody></table>
        <button onClick={() => setForm({ ...form, tiers: [...form.tiers, { threshold: '', percent: '' }] })}>+ Thêm bậc</button>
        <p className="muted">Bậc cao hơn không được có tỷ lệ thấp hơn bậc dưới. Đạt bậc nào thì tỷ lệ đó áp cho toàn bộ doanh số trong kỳ.</p>
      </Modal>}

      {preview && !settle && <Modal title={`Tính chiết khấu — ${preview.a.code}`} onClose={() => setPreview(null)} busy={busy}
        footer={<><button onClick={() => setPreview(null)}>Đóng</button>{!preview.p.settled && Number(preview.p.amount) > 0 &&
          <button className="primary" disabled={busy} onClick={() => setSettle({ a: preview.a, series: '', no: '', invoiceDate: preview.a.toDate > today() ? preview.a.toDate : today(), postingDate: preview.a.toDate > today() ? preview.a.toDate : today() })}>Quyết toán…</button>}</>}>
        <p>Doanh số đủ điều kiện <b>{formatMoney(preview.p.eligibleSales)}</b> → bậc <b>{formatMoney(preview.p.percent)}%</b> → chiết khấu <b>{formatMoney(preview.p.amount)}</b>.
          {preview.p.nextThreshold && <> Còn {formatMoney(String(Number(preview.p.nextThreshold) - Number(preview.p.eligibleSales)))} nữa để lên bậc {formatMoney(preview.p.nextPercent)}%.</>}
          {preview.p.settled && <> <b>Đã quyết toán.</b></>}</p>
        <table className="grid"><thead><tr><th>Hóa đơn</th><th>Ngày</th><th className="num">Doanh số</th><th className="num">Chiết khấu chia</th></tr></thead>
          <tbody>{preview.p.invoices.map(s => <tr key={s.invoiceId}><td>{s.invoiceSeries}/{s.invoiceNo}<div className="muted">{s.number}</div></td><td>{formatDate(s.postingDate)}</td><td className="num">{formatMoney(s.sales)}</td><td className="num">{formatMoney(s.discount)}</td></tr>)}
            {preview.p.invoices.length === 0 && <tr><td colSpan={4} className="muted">Không có hóa đơn đã ghi sổ của đối tượng này trong kỳ.</td></tr>}</tbody></table>
      </Modal>}

      {settle && <Modal title={`Quyết toán ${settle.a.code}`} onClose={() => setSettle(null)} busy={busy}
        footer={<><button onClick={() => setSettle(null)}>Đóng</button><button className="primary" disabled={busy || !settle.series.trim() || !settle.no.trim()} onClick={doSettle}>Sinh hóa đơn điều chỉnh nháp</button></>}>
        <p>{sales ? 'Hóa đơn điều chỉnh do công ty phát hành: nhập ký hiệu và số đầu tiên; các hóa đơn sau đánh số liên tiếp.' : 'Hóa đơn điều chỉnh do nhà cung cấp phát hành: nhập ký hiệu và số của chứng từ đầu tiên nhận được; các hóa đơn sau đánh số liên tiếp (sửa lại bằng cách hủy quyết toán nếu số thực tế khác).'}</p>
        <div className="form-row">
          <label>Ký hiệu HĐ<input value={settle.series} onChange={e => setSettle({ ...settle, series: e.target.value })} /></label>
          <label>Số HĐ đầu tiên<input value={settle.no} onChange={e => setSettle({ ...settle, no: e.target.value })} /></label>
          <label>Ngày hóa đơn<input type="date" value={settle.invoiceDate} onChange={e => setSettle({ ...settle, invoiceDate: e.target.value })} /></label>
          <label>Ngày hạch toán<input type="date" value={settle.postingDate} onChange={e => setSettle({ ...settle, postingDate: e.target.value })} /></label>
        </div>
      </Modal>}
    </>
  )
}
