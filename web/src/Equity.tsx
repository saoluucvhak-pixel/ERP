import { useCallback, useEffect, useState } from 'react'
import { get, post, put } from './api'
import { AccountSelect, ErrorBox, ExportButton, Modal, PartnerSelect, PrintSheet, printSheet, useAction, useCatalog, useCompanyName, FlowStrip } from './common'
import { addMoney, formatDate, formatMoney, parseMoney, parseRate, today } from './money'
import { SmartFilter, useFilterState } from './SmartFilter'
import { matchesQuery } from './filter'

interface Holder { id: string; partnerId: string; partnerCode: string; partnerName: string; isIndividual: boolean; isActive: boolean; committedCapital: string; contributed: string; remaining: string; ownershipPercent: string | null }
interface Move { id: string; shareholderId: string; partnerCode: string; partnerName: string; kind: 'Contribution' | 'Withdrawal'; date: string; amount: string; voucherNumber: string | null; voucherPending: boolean; note: string | null }
type DStatus = 'Draft' | 'Approved' | 'Declared' | 'Cancelled'
interface Div { id: string; number: string; resolutionDate: string; description: string; totalAmount: string; pitRatePercent: string; status: DStatus; declarationVoucherNumber: string | null; declarationPending: boolean; pitTotal: string; netTotal: string }
interface DLine { shareholderId: string; partnerCode: string; partnerName: string; isIndividual: boolean; capitalBasis: string; percent: string | null; gross: string; pitWithheld: string; net: string; paymentVoucherNumber: string | null; paymentPending: boolean; paid: boolean }
interface DView { row: Div; decisionNote: string | null; lines: DLine[] }
const DSTATUS_VI: Record<DStatus, string> = { Draft: 'Dự thảo', Approved: 'Đã duyệt', Declared: 'Đã ghi nhận phải trả', Cancelled: 'Đã hủy/từ chối' }

/** Vốn góp và cổ tức: danh sách chủ sở hữu (cam kết, thực góp, tỷ lệ), góp / rút vốn tạo chứng từ nháp, phương án chia cổ tức → duyệt → ghi nhận phải trả (khấu trừ TNCN) → chi từng người. */
export function EquityView() {
  const [tab, setTab] = useState<'holders' | 'moves' | 'dividends'>('holders')
  return (
    <>
      <div className="toolbar"><h2>Vốn góp và cổ tức</h2>
        <button className={tab === 'holders' ? 'primary' : ''} onClick={() => setTab('holders')}>Chủ sở hữu</button>
        <button className={tab === 'moves' ? 'primary' : ''} onClick={() => setTab('moves')}>Góp / rút vốn</button>
        <button className={tab === 'dividends' ? 'primary' : ''} onClick={() => setTab('dividends')}>Chia cổ tức</button></div>
      {tab === 'holders' && <Holders />}
      {tab === 'moves' && <Moves />}
      {tab === 'dividends' && <Dividends />}
    </>
  )
}

function Holders() {
  const { partners } = useCatalog()
  const [rows, setRows] = useState<Holder[]>([])
  const [asOf, setAsOf] = useState(today())
  const [f, pf, cf] = useFilterState('equity-holders')
  const [nw, setNw] = useState({ partnerId: '', committed: '', individual: true })
  const [edit, setEdit] = useState<Record<string, { committed: string; individual: boolean; active: boolean }>>({})
  const { busy, error, run, setError } = useAction()
  const load = useCallback(() => run(async () => setRows(await get<Holder[]>(`/equity/shareholders?asOf=${asOf}`))), [run, asOf])
  useEffect(() => { load() }, [load])
  const shown = rows.filter(r => matchesQuery(`${r.partnerCode} ${r.partnerName}`, Number(r.contributed), f.q))
  const val = (r: Holder) => edit[r.id] ?? { committed: formatMoney(r.committedCapital), individual: r.isIndividual, active: r.isActive }
  return (
    <>
      <ErrorBox error={error} />
      <div className="toolbar"><label>Tính đến ngày<input type="date" value={asOf} onChange={e => setAsOf(e.target.value)} /></label>
        <ExportButton fileName={`Chu_so_huu_${asOf}`} sheet="Chủ sở hữu" disabled={shown.length === 0}
          columns={[{ header: 'Mã', width: 12 }, { header: 'Tên', width: 30 }, { header: 'Vốn cam kết', numeric: true, width: 18 }, { header: 'Thực góp', numeric: true, width: 18 }, { header: 'Còn phải góp', numeric: true, width: 18 }, { header: 'Tỷ lệ %', numeric: true, width: 10 }]}
          rows={shown.map(r => [r.partnerCode, r.partnerName, r.committedCapital, r.contributed, r.remaining, r.ownershipPercent])} /></div>
      <div className="panel form-row">
        <label>Đối tượng<PartnerSelect partners={partners.filter(p => !rows.some(r => r.partnerId === p.id))} value={nw.partnerId} onChange={id => setNw({ ...nw, partnerId: id })} /></label>
        <label>Vốn cam kết<input className="num" value={nw.committed} onChange={e => setNw({ ...nw, committed: e.target.value })} /></label>
        <label className="check"><input type="checkbox" checked={nw.individual} onChange={e => setNw({ ...nw, individual: e.target.checked })} /> Cá nhân (khấu trừ TNCN cổ tức)</label>
        <button className="primary" style={{ alignSelf: 'flex-end' }} disabled={busy || !nw.partnerId} onClick={() => run(async () => {
          const c = nw.committed.trim() ? parseMoney(nw.committed) : '0'
          if (!c) { setError(new Error('Vốn cam kết không hợp lệ.')); return }
          await post('/equity/shareholders', { partnerId: nw.partnerId, committedCapital: c, isIndividual: nw.individual }); setNw({ partnerId: '', committed: '', individual: true }); await load()
        })}>Thêm chủ sở hữu</button>
      </div>
      <SmartFilter label="Chủ sở hữu" state={f} patch={pf} clear={cf} shown={shown.length} total={rows.length} placeholder="Tìm mã, tên" />
      <div className="scroll"><table className="grid">
        <thead><tr><th>Mã</th><th>Tên</th><th className="num">Vốn cam kết</th><th className="num">Thực góp</th><th className="num">Còn phải góp</th><th className="num">Tỷ lệ %</th><th>Cá nhân</th><th>Đang theo dõi</th><th /></tr></thead>
        <tbody>{shown.map(r => { const v = val(r); const dirty = !!edit[r.id]; return <tr key={r.id}>
          <td>{r.partnerCode}</td><td>{r.partnerName}</td>
          <td className="num"><input className="num" size={14} aria-label="Vốn cam kết" value={v.committed} onChange={e => setEdit(s => ({ ...s, [r.id]: { ...v, committed: e.target.value } }))} /></td>
          <td className="num">{formatMoney(r.contributed)}</td><td className="num">{formatMoney(r.remaining)}</td><td className="num">{r.ownershipPercent == null ? '' : formatMoney(r.ownershipPercent)}</td>
          <td><input type="checkbox" aria-label="Cá nhân" checked={v.individual} onChange={e => setEdit(s => ({ ...s, [r.id]: { ...v, individual: e.target.checked } }))} /></td>
          <td><input type="checkbox" aria-label="Đang theo dõi" checked={v.active} onChange={e => setEdit(s => ({ ...s, [r.id]: { ...v, active: e.target.checked } }))} /></td>
          <td>{dirty && <button className="primary" disabled={busy} onClick={() => run(async () => {
            const c = parseMoney(v.committed); if (!c) { setError(new Error('Vốn cam kết không hợp lệ.')); return }
            await put(`/equity/shareholders/${r.id}`, { committedCapital: c, isIndividual: v.individual, isActive: v.active, note: null }); setEdit(s => { const n = { ...s }; delete n[r.id]; return n }); await load()
          })}>Lưu</button>}</td></tr> })}
          {shown.length === 0 && <tr><td colSpan={9} className="muted">Chưa có chủ sở hữu.</td></tr>}</tbody>
        <tfoot>{shown.length > 0 && <tr><td colSpan={2}><b>Cộng</b></td><td className="num"><b>{formatMoney(addMoney(shown.map(r => r.committedCapital)))}</b></td><td className="num"><b>{formatMoney(addMoney(shown.map(r => r.contributed)))}</b></td><td className="num"><b>{formatMoney(addMoney(shown.map(r => r.remaining)))}</b></td><td colSpan={4} /></tr>}</tfoot>
      </table></div>
    </>
  )
}

function Moves() {
  const { accounts } = useCatalog()
  const [holders, setHolders] = useState<Holder[]>([])
  const [rows, setRows] = useState<Move[]>([])
  const [f, pf, cf] = useFilterState('equity-moves')
  const [form, setForm] = useState({ holder: '', kind: 'Contribution', date: today(), amount: '', cash: '', note: '' })
  const [info, setInfo] = useState('')
  const { busy, error, run, setError } = useAction()
  const load = useCallback(() => run(async () => {
    setHolders(await get<Holder[]>(`/equity/shareholders?asOf=${today()}`))
    setRows(await get<Move[]>(`/equity/movements?from=${f.from}&to=${f.to}`))
  }), [run, f.from, f.to])
  useEffect(() => { load() }, [load])
  const shown = rows.filter(r => (!f.chip || (f.chip === 'in' ? r.kind === 'Contribution' : r.kind === 'Withdrawal')) && matchesQuery(`${r.partnerCode} ${r.partnerName} ${r.voucherNumber ?? ''} ${r.note ?? ''}`, Number(r.amount), f.q))
  return (
    <>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      <div className="panel form-row">
        <label>Chủ sở hữu<select value={form.holder} onChange={e => setForm({ ...form, holder: e.target.value })}><option value="">— chọn —</option>{holders.filter(h => h.isActive).map(h => <option key={h.id} value={h.id}>{h.partnerCode} – {h.partnerName}</option>)}</select></label>
        <label>Loại<select value={form.kind} onChange={e => setForm({ ...form, kind: e.target.value })}><option value="Contribution">Góp vốn</option><option value="Withdrawal">Rút vốn</option></select></label>
        <label>Ngày<input type="date" value={form.date} onChange={e => setForm({ ...form, date: e.target.value })} /></label>
        <label>Số tiền<input className="num" value={form.amount} onChange={e => setForm({ ...form, amount: e.target.value })} /></label>
        <label>Tài khoản tiền<AccountSelect accounts={accounts} value={form.cash} onChange={id => setForm({ ...form, cash: id })} filter={a => a.cashKind === 'Cash' || a.cashKind === 'Bank'} /></label>
        <label style={{ flex: 1, minWidth: 180 }}>Ghi chú<input value={form.note} onChange={e => setForm({ ...form, note: e.target.value })} /></label>
        <button className="primary" style={{ alignSelf: 'flex-end' }} disabled={busy} onClick={() => run(async () => {
          const amount = parseMoney(form.amount)
          if (!form.holder || !form.cash || !amount) { setError(new Error('Chọn chủ sở hữu, tài khoản tiền và nhập số tiền.')); return }
          const v = await post<{ number: string }>('/equity/movements', { shareholderId: form.holder, kind: form.kind, date: form.date, amount, cashAccountId: form.cash, note: form.note || null })
          setInfo(`Đã lập chứng từ ${v.number} (nháp) — duyệt và ghi sổ ở màn Chứng từ; số thực góp chỉ tăng khi chứng từ còn hiệu lực.`); setForm({ ...form, amount: '', note: '' }); await load()
        })}>Lập chứng từ</button>
      </div>
      <SmartFilter label="Góp / rút vốn" state={f} patch={pf} clear={cf} dateLabel="ngày góp / rút" shown={shown.length} total={rows.length} placeholder="Tìm chủ sở hữu, số chứng từ, số tiền"
        chips={[{ key: 'in', label: 'Góp vốn', count: rows.filter(r => r.kind === 'Contribution').length }, { key: 'out', label: 'Rút vốn', count: rows.filter(r => r.kind === 'Withdrawal').length }]} />
      <div className="scroll"><table className="grid">
        <thead><tr><th>Ngày</th><th>Chủ sở hữu</th><th>Loại</th><th className="num">Số tiền</th><th>Chứng từ</th><th>Ghi chú</th></tr></thead>
        <tbody>{shown.map(r => <tr key={r.id}><td>{formatDate(r.date)}</td><td>{r.partnerCode} – {r.partnerName}</td><td>{r.kind === 'Contribution' ? 'Góp vốn' : 'Rút vốn'}</td><td className="num">{formatMoney(r.amount)}</td>
          <td>{r.voucherNumber}{r.voucherPending ? ' (chờ ghi sổ)' : ''}</td><td>{r.note}</td></tr>)}
          {shown.length === 0 && <tr><td colSpan={6} className="muted">Chưa có lần góp / rút vốn trong khoảng ngày.</td></tr>}</tbody>
      </table></div>
    </>
  )
}

function Dividends() {
  const { accounts } = useCatalog()
  const [rows, setRows] = useState<Div[]>([])
  const [view, setView] = useState<DView | null>(null)
  const [form, setForm] = useState<{ date: string; desc: string; total: string; pit: string } | null>(null)
  const [dec, setDec] = useState<{ date: string } | null>(null)
  const [pay, setPay] = useState<{ line: DLine; cash: string; date: string } | null>(null)
  const [info, setInfo] = useState('')
  const { busy, error, run, setError } = useAction()
  const load = useCallback(() => run(async () => setRows(await get<Div[]>('/equity/dividends'))), [run])
  useEffect(() => { load() }, [load])
  const open = (id: string) => run(async () => setView(await get<DView>(`/equity/dividends/${id}`)))
  const act = (msg: string, fn: () => Promise<unknown>) => run(async () => { await fn(); setInfo(msg); await load(); if (view) setView(await get<DView>(`/equity/dividends/${view.row.id}`)) })
  const v = view?.row
  return (
    <>
      <div className="toolbar"><button className="primary" onClick={() => setForm({ date: today(), desc: '', total: '', pit: '5' })}>Lập phương án chia cổ tức</button></div>
      <FlowStrip steps={[
        { label: 'Phương án chia theo vốn thực góp (nghị quyết)', journal: false, count: `${rows.filter(r => r.status === 'Draft').length} dự thảo` },
        { label: 'Duyệt (người khác người lập)', journal: false, count: `${rows.filter(r => r.status === 'Approved' && !r.declarationVoucherNumber).length} chờ ghi nhận` },
        { label: 'Ghi nhận phải trả, khấu trừ TNCN (Nhật ký)', journal: true, screen: 'vouchers', count: `${rows.filter(r => r.status === 'Declared').length} đã ghi nhận` },
        { label: 'Chi từng người (bù trừ khoản phải trả)', journal: true, screen: 'cash', count: 'chứng từ chi theo người' },
      ]} />
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      <div className="scroll"><table className="grid">
        <thead><tr><th>Số</th><th>Ngày nghị quyết</th><th>Căn cứ</th><th className="num">Tổng chia</th><th className="num">Thuế TNCN</th><th className="num">Thực nhận</th><th>Trạng thái</th><th /></tr></thead>
        <tbody>{rows.map(r => <tr key={r.id}><td>{r.number}</td><td>{formatDate(r.resolutionDate)}</td><td>{r.description}</td><td className="num">{formatMoney(r.totalAmount)}</td><td className="num">{formatMoney(r.pitTotal)}</td><td className="num">{formatMoney(r.netTotal)}</td>
          <td>{DSTATUS_VI[r.status]}{r.declarationVoucherNumber ? ` · ${r.declarationVoucherNumber}${r.declarationPending ? ' (chờ ghi sổ)' : ''}` : ''}</td><td><button onClick={() => open(r.id)}>Chi tiết</button></td></tr>)}
          {rows.length === 0 && <tr><td colSpan={8} className="muted">Chưa có phương án chia cổ tức.</td></tr>}</tbody>
      </table></div>

      {form && <Modal title="Phương án chia cổ tức" onClose={() => setForm(null)} busy={busy}
        footer={<><button onClick={() => setForm(null)}>Đóng</button><button className="primary" disabled={busy} onClick={() => run(async () => {
          const total = parseMoney(form.total), pit = parseRate(form.pit)
          if (!total || pit == null) { setError(new Error('Tổng cổ tức và thuế suất không hợp lệ.')); return }
          await post('/equity/dividends', { resolutionDate: form.date, description: form.desc, totalAmount: total, pitRatePercent: pit }); setForm(null); setInfo('Đã lập phương án (dự thảo): chia theo vốn thực góp đến ngày nghị quyết.'); await load()
        })}>Lập phương án</button></>}>
        <div className="form-row"><label>Ngày nghị quyết<input type="date" value={form.date} onChange={e => setForm({ ...form, date: e.target.value })} /></label>
          <label>Tổng số chia<input className="num" value={form.total} onChange={e => setForm({ ...form, total: e.target.value })} /></label>
          <label>Thuế TNCN khấu trừ cổ đông cá nhân (%)<input className="num" size={6} value={form.pit} onChange={e => setForm({ ...form, pit: e.target.value })} /></label></div>
        <label style={{ display: 'block' }}>Căn cứ (số nghị quyết, kỳ lợi nhuận)<input style={{ width: '100%' }} value={form.desc} onChange={e => setForm({ ...form, desc: e.target.value })} /></label>
        <p className="muted">Thuế suất do kế toán khai báo theo quy định hiện hành về thuế thu nhập cá nhân [CẦN XÁC MINH]; phần dư làm tròn dồn vào người góp nhiều nhất.</p>
      </Modal>}

      {view && v && <Modal title={`Phương án ${v.number}`} onClose={() => setView(null)} busy={busy}
        footer={<>{v.status === 'Draft' && <><button className="primary" disabled={busy} onClick={() => act('Đã duyệt phương án.', () => post(`/equity/dividends/${v.id}/decide`, { approve: true }))}>Duyệt</button>
          <button disabled={busy} onClick={() => { const n = prompt('Lý do từ chối:'); if (n?.trim()) act('Đã từ chối.', () => post(`/equity/dividends/${v.id}/decide`, { approve: false, note: n.trim() })) }}>Từ chối</button></>}
          {v.status === 'Approved' && !v.declarationPending && <button className="primary" disabled={busy} onClick={() => setDec({ date: v.resolutionDate })}>Lập chứng từ ghi nhận phải trả</button>}
          {['Draft', 'Approved'].includes(v.status) && !v.declarationVoucherNumber && <button disabled={busy} onClick={() => { const n = prompt('Lý do hủy:'); if (n?.trim()) act('Đã hủy phương án.', () => post(`/equity/dividends/${v.id}/cancel`, { reason: n.trim() })) }}>Hủy</button>}
          <PrintDividend view={view} /><button onClick={() => setView(null)}>Đóng</button></>}>
        <p>{v.description} · Nghị quyết ngày {formatDate(v.resolutionDate)} · Thuế TNCN {formatMoney(v.pitRatePercent)}% · <b>{DSTATUS_VI[v.status]}</b>{view.decisionNote ? ` · ${view.decisionNote}` : ''}</p>
        <div className="scroll"><table className="grid">
          <thead><tr><th>Chủ sở hữu</th><th className="num">Vốn thực góp</th><th className="num">Tỷ lệ %</th><th className="num">Chia (gộp)</th><th className="num">TNCN</th><th className="num">Thực nhận</th><th>Chi</th></tr></thead>
          <tbody>{view.lines.map(l => <tr key={l.shareholderId}><td>{l.partnerCode} – {l.partnerName}{l.isIndividual ? '' : ' (tổ chức)'}</td><td className="num">{formatMoney(l.capitalBasis)}</td><td className="num">{l.percent == null ? '' : formatMoney(l.percent)}</td>
            <td className="num">{formatMoney(l.gross)}</td><td className="num">{l.pitWithheld === '0' ? '' : formatMoney(l.pitWithheld)}</td><td className="num">{formatMoney(l.net)}</td>
            <td>{l.paid ? 'Đã chi' : l.paymentVoucherNumber ? `${l.paymentVoucherNumber} (chờ ghi sổ)` : v.status === 'Declared' && !v.declarationPending && <button disabled={busy} onClick={() => setPay({ line: l, cash: '', date: today() })}>Lập chứng từ chi</button>}</td></tr>)}</tbody>
          <tfoot><tr><td colSpan={3}><b>Cộng</b></td><td className="num"><b>{formatMoney(v.totalAmount)}</b></td><td className="num"><b>{formatMoney(v.pitTotal)}</b></td><td className="num"><b>{formatMoney(v.netTotal)}</b></td><td /></tr></tfoot>
        </table></div>
      </Modal>}

      {dec && view && <Modal title="Ghi nhận cổ tức phải trả" onClose={() => setDec(null)} busy={busy}
        footer={<><button onClick={() => setDec(null)}>Đóng</button><button className="primary" disabled={busy} onClick={() => run(async () => {
          const r = await post<{ number: string }>(`/equity/dividends/${view.row.id}/declare`, { date: dec.date }); setDec(null); setInfo(`Đã lập chứng từ ${r.number} (nháp): Nợ LNST chưa phân phối / Có cổ tức phải trả (theo người), thuế TNCN. Duyệt và ghi sổ ở màn Chứng từ.`); await load(); setView(await get<DView>(`/equity/dividends/${view.row.id}`))
        })}>Lập chứng từ</button></>}>
        <label>Ngày hạch toán<input type="date" value={dec.date} onChange={e => setDec({ date: e.target.value })} /></label>
        <p className="muted">Cần ánh xạ vai trò DIVIDEND_SOURCE, DIVIDEND_PAYABLE (và DIVIDEND_PIT_PAYABLE khi có thuế) ở Thiết lập › Vai trò tài khoản.</p>
      </Modal>}

      {pay && view && <Modal title={`Chi cổ tức — ${pay.line.partnerName}`} onClose={() => setPay(null)} busy={busy}
        footer={<><button onClick={() => setPay(null)}>Đóng</button><button className="primary" disabled={busy || !pay.cash} onClick={() => run(async () => {
          const r = await post<{ number: string }>(`/equity/dividends/${view.row.id}/pay`, { shareholderId: pay.line.shareholderId, cashAccountId: pay.cash, date: pay.date }); setPay(null); setInfo(`Đã lập chứng từ chi ${r.number} (nháp).`); await load(); setView(await get<DView>(`/equity/dividends/${view.row.id}`))
        })}>Lập chứng từ chi</button></>}>
        <p>Chi <b>{formatMoney(pay.line.net)}</b> (đã trừ thuế TNCN {formatMoney(pay.line.pitWithheld)}).</p>
        <div className="form-row"><label>Tài khoản tiền<AccountSelect accounts={accounts} value={pay.cash} onChange={id => setPay({ ...pay, cash: id })} filter={a => a.cashKind === 'Cash' || a.cashKind === 'Bank'} /></label>
          <label>Ngày chi<input type="date" value={pay.date} onChange={e => setPay({ ...pay, date: e.target.value })} /></label></div>
      </Modal>}
    </>
  )
}

/** Bảng chia cổ tức in kèm nghị quyết: chủ sở hữu, tỷ lệ, số chia, thuế, thực nhận, chữ ký nhận. */
function PrintDividend({ view }: { view: DView }) {
  const company = useCompanyName()
  const [on, setOn] = useState(false)
  useEffect(() => { if (!on) return; const t = setTimeout(() => { printSheet(); setOn(false) }, 150); return () => clearTimeout(t) }, [on])
  const v = view.row
  return <>
    <button onClick={() => setOn(true)}>In bảng chia</button>
    {on && <PrintSheet>
      <div><b>{company}</b></div>
      <h1>BẢNG CHIA CỔ TỨC, LỢI NHUẬN</h1>
      <div className="center">Số: <b>{v.number}</b> · Theo nghị quyết ngày {formatDate(v.resolutionDate)} · {v.description}</div>
      <table><thead><tr><th>STT</th><th>Chủ sở hữu</th><th>Vốn thực góp</th><th>Tỷ lệ %</th><th>Số chia</th><th>Thuế TNCN</th><th>Thực nhận</th><th>Ký nhận</th></tr></thead>
        <tbody>{view.lines.map((l, i) => <tr key={l.shareholderId}><td className="center">{i + 1}</td><td>{l.partnerName}</td><td className="num">{formatMoney(l.capitalBasis)}</td><td className="num">{l.percent == null ? '' : formatMoney(l.percent)}</td><td className="num">{formatMoney(l.gross)}</td><td className="num">{formatMoney(l.pitWithheld)}</td><td className="num">{formatMoney(l.net)}</td><td /></tr>)}</tbody>
        <tfoot><tr><td colSpan={4}><b>Cộng</b></td><td className="num"><b>{formatMoney(v.totalAmount)}</b></td><td className="num"><b>{formatMoney(v.pitTotal)}</b></td><td className="num"><b>{formatMoney(v.netTotal)}</b></td><td /></tr></tfoot></table>
      <div className="signs"><div><b>Người lập</b><br /><i>(Ký, họ tên)</i></div><div><b>Kế toán trưởng</b><br /><i>(Ký, họ tên)</i></div><div><b>Giám đốc</b><br /><i>(Ký, họ tên, đóng dấu)</i></div></div>
      <div className="foot">Mẫu in nội bộ HAK Accounting [CẦN XÁC MINH]. In lúc {new Date().toLocaleString('vi-VN')}.</div>
    </PrintSheet>}
  </>
}
