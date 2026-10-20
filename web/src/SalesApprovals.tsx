import { useCallback, useEffect, useMemo, useState } from 'react'
import { get, post } from './api'
import { ErrorBox, Modal, PartnerSelect, useAction, useCatalog } from './common'
import { addMoney, formatDate, formatMoney, parseMoney, today } from './money'
import { SmartFilter, useFilterState } from './SmartFilter'
import { matchesQuery } from './filter'

type AStatus = 'Requested' | 'Approved' | 'Rejected' | 'Cancelled'
const STATUS_VI: Record<AStatus, string> = { Requested: 'Chờ duyệt', Approved: 'Đã duyệt', Rejected: 'Từ chối', Cancelled: 'Đã hủy' }
interface ReqLine { originalLineNo: number; itemCode: string; itemName: string; invoicedQty: string; invoicedAmount: string; quantity: string | null; amount: string | null; used: string; note: string | null }
interface Req { id: string; number: string; invoiceId: string; invoiceNumber: string; partnerCode: string; partnerName: string; kind: 'Return' | 'Discount'; reason: string; status: AStatus; decisionNote: string | null; createdAt: string; totalAmount: string; totalQuantity: string; lines: ReqLine[] }
interface Inv { id: string; number: string; invoiceSeries: string; invoiceNo: string; kind: string; purpose: string; status: string; partnerId: string; lines: { lineNo: number; itemId: string; stockDocumentId: string | null; stockQuantity: string | null; functionalAmount: string }[] }
interface Item { id: string; code: string; name: string }
interface Cand { journalLineId: string; voucherNumber: string; postingDate: string; dueDate: string | null; open: string; overdueDays: number; description: string | null }
interface WoRow { id: string; number: string; partnerId: string; partnerCode: string; partnerName: string; reason: string; basis: string; status: AStatus; decisionNote: string | null; voucherNumber: string | null; voucherPending: boolean; done: boolean; total: string; provision: string; createdAt: string; items: { journalLineId: string; voucherNumber: string; postingDate: string; amount: string; provisionUsed: string }[] }

/** Phê duyệt trong chu trình doanh thu: yêu cầu trả hàng / giảm giá (duyệt trước khi nhận hàng, lập hóa đơn điều chỉnh) và xóa nợ phải thu khó đòi. */
export function SalesApprovalsView() {
  const [tab, setTab] = useState<'adj' | 'wo'>('adj')
  const [policy, setPolicy] = useState(false)
  const { busy, error, run } = useAction()
  const loadPolicy = useCallback(() => run(async () => setPolicy((await get<{ require: boolean }>('/sales-approvals/policy')).require)), [run])
  useEffect(() => { loadPolicy() }, [loadPolicy])
  return (
    <>
      <div className="toolbar"><h2>Phê duyệt bán hàng</h2>
        <button className={tab === 'adj' ? 'primary' : ''} onClick={() => setTab('adj')}>Trả hàng / giảm giá</button>
        <button className={tab === 'wo' ? 'primary' : ''} onClick={() => setTab('wo')}>Xóa nợ khó đòi</button></div>
      <ErrorBox error={error} />
      {tab === 'adj' && <>
        <label className="check panel" style={{ display: 'block' }}><input type="checkbox" checked={policy} disabled={busy} onChange={e => run(async () => { await post('/sales-approvals/policy', { require: e.target.checked }); await loadPolicy() })} />
          {' '}Bắt buộc duyệt trước khi nhận hàng bán trả lại và lập hóa đơn giảm giá bán (chỉ làm trong phạm vi số lượng / số tiền đã duyệt cho từng dòng hóa đơn)</label>
        <Adjustments policy={policy} />
      </>}
      {tab === 'wo' && <WriteOffs />}
    </>
  )
}

function Adjustments({ policy }: { policy: boolean }) {
  const [rows, setRows] = useState<Req[]>([])
  const [invs, setInvs] = useState<Inv[]>([])
  const [items, setItems] = useState<Item[]>([])
  const [f, pf, cf] = useFilterState('sales-adjust')
  const [form, setForm] = useState<{ invoiceId: string; kind: 'Return' | 'Discount'; reason: string; v: Record<number, string> } | null>(null)
  const [info, setInfo] = useState('')
  const { busy, error, run, setError } = useAction()
  const load = useCallback(() => run(async () => {
    setRows(await get<Req[]>('/sales-approvals/adjustments'))
    setInvs((await get<Inv[]>('/invoices?kind=Sales')).filter(i => i.purpose === 'Normal' && i.status === 'Posted'))
    setItems(await get<Item[]>('/items'))
  }), [run])
  useEffect(() => { load() }, [load])
  const shown = useMemo(() => rows.filter(r => (!f.chip || r.status === f.chip) && matchesQuery(`${r.number} ${r.invoiceNumber} ${r.partnerCode} ${r.partnerName} ${r.reason}`, Number(r.kind === 'Return' ? r.totalQuantity : r.totalAmount), f.q)), [rows, f])
  const act = (msg: string, fn: () => Promise<unknown>) => run(async () => { await fn(); setInfo(msg); await load() })
  const inv = form ? invs.find(i => i.id === form.invoiceId) : undefined
  const code = (id: string) => items.find(i => i.id === id)?.code ?? ''
  const save = () => run(async () => {
    if (!form || !inv) { setError(new Error('Chọn hóa đơn bán.')); return }
    const lines = Object.entries(form.v).filter(([, v]) => v.trim()).map(([n, v]) => ({ originalLineNo: Number(n), quantity: form.kind === 'Return' ? parseMoney(v) : null, amount: form.kind === 'Discount' ? parseMoney(v) : null }))
    if (lines.length === 0 || lines.some(l => (form.kind === 'Return' ? l.quantity : l.amount) == null)) { setError(new Error('Nhập số lượng / số tiền hợp lệ cho ít nhất một dòng.')); return }
    await post('/sales-approvals/adjustments', { invoiceId: inv.id, kind: form.kind, reason: form.reason, lines })
    setForm(null); setInfo('Đã lập yêu cầu, chờ người có thẩm quyền duyệt.'); await load()
  })
  return (
    <>
      <div className="toolbar">{!policy && <span className="muted">Chính sách duyệt đang tắt: yêu cầu chỉ để lưu vết, hệ thống chưa chặn trả hàng / giảm giá chưa duyệt.</span>}
        <button className="primary" onClick={() => setForm({ invoiceId: '', kind: 'Return', reason: '', v: {} })}>Lập yêu cầu</button></div>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      <SmartFilter label="Yêu cầu trả hàng / giảm giá" state={f} patch={pf} clear={cf} shown={shown.length} total={rows.length} placeholder="Tìm số, hóa đơn, khách hàng, lý do"
        chips={(['Requested', 'Approved', 'Rejected', 'Cancelled'] as AStatus[]).map(k => ({ key: k, label: STATUS_VI[k], count: rows.filter(r => r.status === k).length }))} />
      <div className="scroll"><table className="grid">
        <thead><tr><th>Số</th><th>Hóa đơn</th><th>Khách hàng</th><th>Loại</th><th>Nội dung</th><th>Lý do</th><th>Trạng thái</th><th /></tr></thead>
        <tbody>{shown.map(r => <tr key={r.id}><td>{r.number}<div className="muted">{formatDate(r.createdAt)}</div></td><td>{r.invoiceNumber}</td><td>{r.partnerCode} – {r.partnerName}</td><td>{r.kind === 'Return' ? 'Trả hàng' : 'Giảm giá'}</td>
          <td>{r.lines.map(l => <div key={l.originalLineNo}>Dòng {l.originalLineNo} {l.itemCode}: {r.kind === 'Return' ? `${formatMoney(l.quantity)}/${formatMoney(l.invoicedQty)} (đã dùng ${formatMoney(l.used)})` : `${formatMoney(l.amount)} (đã giảm ${formatMoney(l.used)})`}</div>)}</td>
          <td>{r.reason}{r.decisionNote ? <div className="muted">Duyệt: {r.decisionNote}</div> : null}</td><td>{STATUS_VI[r.status]}</td>
          <td style={{ whiteSpace: 'nowrap' }}>
            {r.status === 'Requested' && <><button className="primary" disabled={busy} onClick={() => act('Đã duyệt.', () => post(`/sales-approvals/adjustments/${r.id}/decide`, { approve: true }))}>Duyệt</button>
              <button disabled={busy} onClick={() => { const n = prompt('Lý do từ chối:'); if (n?.trim()) act('Đã từ chối.', () => post(`/sales-approvals/adjustments/${r.id}/decide`, { approve: false, note: n.trim() })) }}>Từ chối</button></>}
            {['Requested', 'Approved'].includes(r.status) && <button disabled={busy} onClick={() => { const n = prompt('Lý do hủy:'); if (n?.trim()) act('Đã hủy.', () => post(`/sales-approvals/adjustments/${r.id}/cancel`, { reason: n.trim() })) }}>Hủy</button>}
          </td></tr>)}
          {shown.length === 0 && <tr><td colSpan={8} className="muted">Chưa có yêu cầu.</td></tr>}</tbody>
      </table></div>
      {form && <Modal title="Yêu cầu trả hàng / giảm giá" onClose={() => setForm(null)} busy={busy}
        footer={<><button onClick={() => setForm(null)}>Đóng</button><button className="primary" disabled={busy} onClick={save}>Lập yêu cầu</button></>}>
        <div className="form-row">
          <label>Hóa đơn bán<select value={form.invoiceId} onChange={e => setForm({ ...form, invoiceId: e.target.value, v: {} })}><option value="">— chọn —</option>{invs.map(i => <option key={i.id} value={i.id}>{i.number} · {i.invoiceSeries}/{i.invoiceNo}</option>)}</select></label>
          <label>Loại<select value={form.kind} onChange={e => setForm({ ...form, kind: e.target.value as 'Return' | 'Discount', v: {} })}><option value="Return">Trả hàng</option><option value="Discount">Giảm giá</option></select></label>
        </div>
        {inv && <table className="grid"><thead><tr><th>Dòng</th><th>Vật tư</th><th className="num">Số lượng bán</th><th className="num">Giá trị (chưa thuế)</th><th className="num">{form.kind === 'Return' ? 'Số lượng trả' : 'Số tiền giảm'}</th></tr></thead>
          <tbody>{inv.lines.filter(l => form.kind === 'Discount' || l.stockDocumentId).map(l => <tr key={l.lineNo}><td>{l.lineNo}</td><td>{code(l.itemId)}</td><td className="num">{formatMoney(l.stockQuantity)}</td><td className="num">{formatMoney(l.functionalAmount)}</td>
            <td className="num"><input className="num" size={12} aria-label={`Dòng ${l.lineNo}`} value={form.v[l.lineNo] ?? ''} onChange={e => setForm({ ...form, v: { ...form.v, [l.lineNo]: e.target.value } })} /></td></tr>)}</tbody></table>}
        <label style={{ display: 'block' }}>Lý do<input style={{ width: '100%' }} value={form.reason} placeholder="Ví dụ: độ khô không đạt hợp đồng, biên bản giám định số …" onChange={e => setForm({ ...form, reason: e.target.value })} /></label>
      </Modal>}
    </>
  )
}

function WriteOffs() {
  const { partners } = useCatalog()
  const [rows, setRows] = useState<WoRow[]>([])
  const [f, pf, cf] = useFilterState('writeoffs')
  const [form, setForm] = useState<{ partnerId: string; reason: string; basis: string; cands: Cand[]; amt: Record<string, string>; prov: Record<string, string> } | null>(null)
  const [vdlg, setVdlg] = useState<{ row: WoRow; date: string } | null>(null)
  const [info, setInfo] = useState('')
  const { busy, error, run, setError } = useAction()
  const load = useCallback(() => run(async () => setRows(await get<WoRow[]>('/sales-approvals/write-offs'))), [run])
  useEffect(() => { load() }, [load])
  const shown = rows.filter(r => (!f.chip || (f.chip === 'done' ? r.done : r.status === f.chip)) && matchesQuery(`${r.number} ${r.partnerCode} ${r.partnerName} ${r.reason} ${r.basis}`, Number(r.total), f.q))
  const act = (msg: string, fn: () => Promise<unknown>) => run(async () => { await fn(); setInfo(msg); await load() })
  const pick = (partnerId: string) => run(async () => {
    const cands = partnerId ? await get<Cand[]>(`/sales-approvals/write-offs/candidates?partnerId=${partnerId}&asOf=${today()}`) : []
    setForm(s => s && { ...s, partnerId, cands, amt: {}, prov: {} })
  })
  const save = () => run(async () => {
    if (!form) return
    const items = form.cands.filter(c => form.amt[c.journalLineId]?.trim()).map(c => ({ journalLineId: c.journalLineId, amount: parseMoney(form.amt[c.journalLineId]), provisionUsed: form.prov[c.journalLineId]?.trim() ? parseMoney(form.prov[c.journalLineId]) : '0' }))
    if (items.length === 0 || items.some(i => !i.amount || !i.provisionUsed)) { setError(new Error('Chọn khoản nợ và nhập số tiền hợp lệ.')); return }
    await post('/sales-approvals/write-offs', { partnerId: form.partnerId, reason: form.reason, basis: form.basis, items })
    setForm(null); setInfo('Đã lập phiếu xóa nợ, chờ người có thẩm quyền duyệt.'); await load()
  })
  return (
    <>
      <div className="toolbar"><button className="primary" onClick={() => setForm({ partnerId: '', reason: '', basis: '', cands: [], amt: {}, prov: {} })}>Lập phiếu xóa nợ</button></div>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      <SmartFilter label="Xóa nợ" state={f} patch={pf} clear={cf} shown={shown.length} total={rows.length} placeholder="Tìm số, khách hàng, lý do, căn cứ, số tiền"
        chips={[{ key: 'Requested', label: 'Chờ duyệt', count: rows.filter(r => r.status === 'Requested').length }, { key: 'Approved', label: 'Đã duyệt', count: rows.filter(r => r.status === 'Approved').length }, { key: 'done', label: 'Đã ghi sổ', count: rows.filter(r => r.done).length }]} />
      <div className="scroll"><table className="grid">
        <thead><tr><th>Số</th><th>Khách hàng</th><th>Lý do / căn cứ</th><th className="num">Số xóa</th><th className="num">Dùng dự phòng</th><th>Trạng thái</th><th /></tr></thead>
        <tbody>{shown.map(r => <tr key={r.id}><td>{r.number}<div className="muted">{formatDate(r.createdAt)}</div></td><td>{r.partnerCode} – {r.partnerName}</td><td>{r.reason}<div className="muted">{r.basis}</div>{r.decisionNote ? <div className="muted">Duyệt: {r.decisionNote}</div> : null}</td>
          <td className="num">{formatMoney(r.total)}</td><td className="num">{r.provision === '0' ? '' : formatMoney(r.provision)}</td>
          <td>{r.done ? 'Đã ghi sổ' : r.voucherNumber ? `Đã lập chứng từ ${r.voucherNumber}${r.voucherPending ? ' (chờ ghi sổ)' : ''}` : STATUS_VI[r.status]}</td>
          <td style={{ whiteSpace: 'nowrap' }}>
            {r.status === 'Requested' && <><button className="primary" disabled={busy} title="Cần quyền người có thẩm quyền (credit.override)" onClick={() => act('Đã duyệt xóa nợ.', () => post(`/sales-approvals/write-offs/${r.id}/decide`, { approve: true }))}>Duyệt</button>
              <button disabled={busy} onClick={() => { const n = prompt('Lý do từ chối:'); if (n?.trim()) act('Đã từ chối.', () => post(`/sales-approvals/write-offs/${r.id}/decide`, { approve: false, note: n.trim() })) }}>Từ chối</button></>}
            {r.status === 'Approved' && !r.voucherNumber && <button className="primary" disabled={busy} onClick={() => setVdlg({ row: r, date: today() })}>Lập chứng từ</button>}
            {['Requested', 'Approved'].includes(r.status) && !r.voucherNumber && <button disabled={busy} onClick={() => { const n = prompt('Lý do hủy:'); if (n?.trim()) act('Đã hủy.', () => post(`/sales-approvals/write-offs/${r.id}/cancel`, { reason: n.trim() })) }}>Hủy</button>}
          </td></tr>)}
          {shown.length === 0 && <tr><td colSpan={7} className="muted">Chưa có phiếu xóa nợ.</td></tr>}</tbody>
      </table></div>

      {form && <Modal title="Phiếu xóa nợ phải thu khó đòi" onClose={() => setForm(null)} busy={busy}
        footer={<><button onClick={() => setForm(null)}>Đóng</button><button className="primary" disabled={busy} onClick={save}>Lập phiếu</button></>}>
        <label>Khách hàng<PartnerSelect partners={partners.filter(p => p.isCustomer)} value={form.partnerId} onChange={pick} /></label>
        {form.partnerId && <div className="scroll"><table className="grid">
          <thead><tr><th>Chứng từ</th><th>Ngày</th><th>Hạn</th><th className="num">Còn nợ</th><th className="num">Xóa</th><th className="num">Trong đó dự phòng</th></tr></thead>
          <tbody>{form.cands.map(c => <tr key={c.journalLineId}><td>{c.voucherNumber}</td><td>{formatDate(c.postingDate)}</td><td>{formatDate(c.dueDate)}{c.overdueDays > 0 ? ` · quá ${c.overdueDays} ngày` : ''}</td><td className="num">{formatMoney(c.open)}</td>
            <td className="num"><input className="num" size={12} aria-label={`Xóa ${c.voucherNumber}`} value={form.amt[c.journalLineId] ?? ''} onChange={e => setForm({ ...form, amt: { ...form.amt, [c.journalLineId]: e.target.value } })} /></td>
            <td className="num"><input className="num" size={12} aria-label={`Dự phòng ${c.voucherNumber}`} value={form.prov[c.journalLineId] ?? ''} onChange={e => setForm({ ...form, prov: { ...form.prov, [c.journalLineId]: e.target.value } })} /></td></tr>)}
            {form.cands.length === 0 && <tr><td colSpan={6} className="muted">Khách hàng không có khoản phải thu đang mở.</td></tr>}</tbody>
          <tfoot><tr><td colSpan={4}><b>Tổng xóa</b></td><td className="num"><b>{formatMoney(addMoney(form.cands.map(c => parseMoney(form.amt[c.journalLineId] ?? '') ?? '0')))}</b></td><td className="num"><b>{formatMoney(addMoney(form.cands.map(c => parseMoney(form.prov[c.journalLineId] ?? '') ?? '0')))}</b></td></tr></tfoot></table></div>}
        <label style={{ display: 'block' }}>Lý do xóa nợ<input style={{ width: '100%' }} value={form.reason} placeholder="Khách giải thể / phá sản / mất tích, hết thời hiệu…" onChange={e => setForm({ ...form, reason: e.target.value })} /></label>
        <label style={{ display: 'block' }}>Căn cứ (số quyết định, biên bản, hồ sơ đòi nợ)<input style={{ width: '100%' }} value={form.basis} onChange={e => setForm({ ...form, basis: e.target.value })} /></label>
        <p className="muted">Phần "dự phòng" là số đã trích dự phòng phải thu khó đòi, dùng bù khi xóa (Nợ vai trò BAD_DEBT_PROVISION); phần còn lại ghi chi phí (BAD_DEBT_EXPENSE). Chế độ kế toán có thể yêu cầu theo dõi nợ đã xóa ngoài bảng cân đối [CẦN XÁC MINH].</p>
      </Modal>}
      {vdlg && <Modal title={`Chứng từ xóa nợ ${vdlg.row.number}`} onClose={() => setVdlg(null)} busy={busy}
        footer={<><button onClick={() => setVdlg(null)}>Đóng</button><button className="primary" disabled={busy} onClick={() => run(async () => {
          const v = await post<{ number: string }>(`/sales-approvals/write-offs/${vdlg.row.id}/voucher`, { date: vdlg.date }); setVdlg(null); setInfo(`Đã lập chứng từ ${v.number} (nháp) — duyệt và ghi sổ ở màn Chứng từ.`); await load()
        })}>Lập chứng từ</button></>}>
        <label>Ngày hạch toán<input type="date" value={vdlg.date} onChange={e => setVdlg({ ...vdlg, date: e.target.value })} /></label>
      </Modal>}
    </>
  )
}
