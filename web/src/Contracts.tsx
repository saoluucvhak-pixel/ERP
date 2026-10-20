import { Fragment, useCallback, useEffect, useState } from 'react'
import { get, post, put } from './api'
import { ErrorBox, PartnerSelect, useAction, useCatalog } from './common'
import { formatDate, formatMoney, parseMoney, today } from './money'
import { SmartFilter, overlaps, useFilterState } from './SmartFilter'
import { matchesQuery, toNumber } from './filter'

type Kind = 'Sales' | 'Purchase'
type CStatus = 'Draft' | 'PendingApproval' | 'Active' | 'Completed' | 'Cancelled'
interface Row { id: string; code: string; name: string; kind: Kind; category: 'Quantity' | 'Value' | 'Standard'; partnerId: string; partnerCode: string; partnerName: string; currency: string; effectiveDate: string; expirationDate: string; totalAmount: string; orderedAmount: string; percentDone: string | null; status: CStatus; expired: boolean; daysToExpire: number; version: number }
interface LineV { id: string; lineNo: number; itemId: string; itemCode: string; itemName: string; unit: string; committedQty: string; unitPrice: string; discountRate: string; taxRate: string; netPrice: string; committedAmount: string; orderedQty: string; orderedAmount: string; remainingQty: string; percentDone: string | null; notes: string | null }
interface MsV { id: string; name: string; percentage: string | null; dueAmount: string; dueDate: string; paidAmount: string; paymentStatus: 'PENDING' | 'PARTIAL' | 'PAID' | 'OVERDUE'; lastPaidDate: string | null }
interface View { header: Row; paymentTerms: string | null; signedDate: string | null; exchangeRate: string; taxAmount: string; statusReason: string | null; lines: LineV[]; milestones: MsV[]
  amendments: { id: string; code: string; amendmentDate: string; summary: string; valueChange: string; newExpirationDate: string | null }[]
  documents: { id: string; fileName: string; filePath: string; fileType: string | null; uploadedAt: string }[]
  orders: { orderId: string; number: string; orderDate: string; status: string; amount: string }[] }
interface Variance { contractId: string; contractCode: string; itemCode: string; itemName: string; contractPrice: string; orderedQty: string; averagePrice: string; minPrice: string; maxPrice: string; variancePercent: string }
interface Sched { contractId: string; contractCode: string; kind: Kind; partnerName: string; milestoneId: string; name: string; dueDate: string; dueAmount: string; paidAmount: string; remaining: string; paymentStatus: string }
interface Alert { contractId: string; contractCode: string; kind: string; message: string }
interface ItemRef { id: string; code: string; name: string; unit: string }
interface LineDraft { itemId: string; qty: string; price: string; discount: string; tax: string }
interface MsDraft { name: string; amount: string; date: string }

const STATUS_VI: Record<CStatus, string> = { Draft: 'Nháp', PendingApproval: 'Chờ duyệt', Active: 'Hiệu lực', Completed: 'Đã thanh lý', Cancelled: 'Đã hủy' }
const CATEGORY_VI = { Quantity: 'Khung số lượng', Value: 'Khung giá trị', Standard: 'Tiêu chuẩn' } as const
const ORDER_VI: Record<string, string> = { Draft: 'Nháp', Confirmed: 'Đã xác nhận', Closed: 'Đã đóng', Cancelled: 'Đã hủy' }
const PAY_VI = { PENDING: 'Chưa đến hạn', PARTIAL: 'Đã trả một phần', PAID: 'Đã thanh toán', OVERDUE: 'Quá hạn' } as const

/** Quản lý hợp đồng mua bán (tệp phân tích quản lý hợp đồng): dự thảo → duyệt → hiệu lực → đơn kế thừa trừ lùi → phụ lục → thanh lý; mốc thanh toán; báo cáo, cảnh báo. */
export function ContractsView() {
  const { partners } = useCatalog()
  const [kind, setKind] = useState<Kind>('Sales')
  const [rows, setRows] = useState<Row[]>([])
  const [alerts, setAlerts] = useState<Alert[]>([])
  const [variance, setVariance] = useState<Variance[]>([])
  const [sched, setSched] = useState<Sched[]>([])
  const [items, setItems] = useState<ItemRef[]>([])
  const [view, setView] = useState<View | null>(null)
  const [form, setForm] = useState<{ id: string | null; name: string; category: Row['category']; partnerId: string; effective: string; expiration: string; terms: string; total: string; lines: LineDraft[]; ms: MsDraft[] } | null>(null)
  const [amend, setAmend] = useState<{ summary: string; value: string; expiry: string; qty: Record<string, string> } | null>(null)
  const [f, pf, cf] = useFilterState('contracts')
  const [info, setInfo] = useState('')
  const { busy, error, run, setError } = useAction()

  const load = useCallback(() => run(async () => {
    setRows(await get<Row[]>(`/contracts?kind=${kind}`))
    setAlerts(await get<Alert[]>('/contracts/alerts'))
    setVariance(await get<Variance[]>('/contracts/reports/price-variance'))
    setSched(await get<Sched[]>(`/contracts/reports/milestones?kind=${kind}`))
  }), [run, kind])
  useEffect(() => { load() }, [load])
  useEffect(() => { run(async () => setItems(await get<ItemRef[]>('/items'))) }, [run])
  const open = (id: string) => run(async () => setView(await get<View>(`/contracts/${id}`)))
  const act = (msg: string, fn: () => Promise<unknown>) => run(async () => { await fn(); setInfo(msg); await load(); if (view) setView(await get<View>(`/contracts/${view.header.id}`)) })
  const ask = (q: string) => { const v = prompt(q); return v && v.trim() ? v.trim() : null }
  const mine = partners.filter(p => (kind === 'Sales' ? p.isCustomer : p.isSupplier))
  const blank = () => setForm({ id: null, name: '', category: 'Quantity', partnerId: '', effective: today(), expiration: '', terms: '', total: '', lines: [{ itemId: '', qty: '', price: '', discount: '', tax: '' }], ms: [] })

  const save = () => {
    if (!form) return
    const money = (t: string) => (t.trim() ? parseMoney(t) : '0')
    const lines = form.lines.filter(l => l.itemId).map(l => ({ itemId: l.itemId, committedQty: parseMoney(l.qty), unitPrice: parseMoney(l.price), discountRate: money(l.discount), taxRate: money(l.tax) }))
    const ms = form.ms.filter(m => m.name.trim()).map(m => ({ name: m.name, percentage: null, dueAmount: parseMoney(m.amount), dueDate: m.date }))
    const total = form.category === 'Value' ? parseMoney(form.total) : null
    if (lines.some(l => l.committedQty == null || l.unitPrice == null || l.discountRate == null || l.taxRate == null) || ms.some(m => m.dueAmount == null || !m.dueDate) || (form.category === 'Value' && total == null)) { setError(new Error('Có ô số hoặc ngày không hợp lệ.')); return }
    const body = { kind, name: form.name, category: form.category, partnerId: form.partnerId, effectiveDate: form.effective, expirationDate: form.expiration, lines, paymentTerms: form.terms || null, totalAmount: total, milestones: ms }
    act(form.id ? 'Đã lưu hợp đồng (phiên bản mới).' : 'Đã lập hợp đồng nháp.', async () => { if (form.id) await put(`/contracts/${form.id}`, body); else await post('/contracts', body); setForm(null) })
  }
  const edit = (v: View) => setForm({ id: v.header.id, name: v.header.name, category: v.header.category, partnerId: v.header.partnerId, effective: v.header.effectiveDate, expiration: v.header.expirationDate, terms: v.paymentTerms ?? '', total: v.header.category === 'Value' ? formatMoney(v.header.totalAmount) : '',
    lines: v.lines.map(l => ({ itemId: l.itemId, qty: formatMoney(l.committedQty), price: formatMoney(l.unitPrice), discount: l.discountRate === '0' ? '' : l.discountRate, tax: l.taxRate === '0' ? '' : l.taxRate })), ms: v.milestones.map(m => ({ name: m.name, amount: formatMoney(m.dueAmount), date: m.dueDate })) })
  const setLine = (i: number, p: Partial<LineDraft>) => form && setForm({ ...form, lines: form.lines.map((l, k) => k === i ? { ...l, ...p } : l) })
  const setMs = (i: number, p: Partial<MsDraft>) => form && setForm({ ...form, ms: form.ms.map((m, k) => k === i ? { ...m, ...p } : m) })
  const h = view?.header
  const chipTests: Record<string, (r: Row) => boolean> = {
    expiring: r => r.status === 'Active' && !r.expired && r.daysToExpire <= 60, expired: r => r.expired, nearLimit: r => r.status === 'Active' && Number(r.percentDone ?? 0) >= 90, pending: r => r.status === 'PendingApproval' }
  const shownRows = rows.filter(r => overlaps(f, r.effectiveDate, r.expirationDate) && (!f.sel.status || (f.sel.status === 'Expired' ? r.expired : r.status === f.sel.status && !r.expired)) && (!f.chip || chipTests[f.chip]?.(r))
    && matchesQuery(`${r.code} ${r.name} ${r.partnerCode} ${r.partnerName}`, toNumber(r.totalAmount), f.q))

  return (
    <>
      <div className="toolbar">
        <h2>Hợp đồng</h2>
        <button className={kind === 'Sales' ? 'primary' : ''} onClick={() => { setKind('Sales'); setView(null); setForm(null) }}>Hợp đồng bán</button>
        <button className={kind === 'Purchase' ? 'primary' : ''} onClick={() => { setKind('Purchase'); setView(null); setForm(null) }}>Hợp đồng mua</button>
        <button onClick={blank}>+ Lập hợp đồng</button>
      </div>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      {alerts.length > 0 && <section className="panel" aria-label="Cảnh báo hợp đồng"><h3>Cảnh báo</h3><ul>{alerts.map((a, i) => <li key={i}><button className="link" onClick={() => open(a.contractId)}>{a.message}</button></li>)}</ul></section>}

      {form && <section className="panel" aria-label="Soạn hợp đồng">
        <h3>{form.id ? 'Sửa dự thảo hợp đồng' : `Dự thảo hợp đồng ${kind === 'Sales' ? 'bán' : 'mua'}`}</h3>
        <div className="form-row">
          <label>Tên hợp đồng<input aria-label="Tên hợp đồng" value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} size={36} /></label>
          <label>{kind === 'Sales' ? 'Khách hàng' : 'Nhà cung cấp'}<PartnerSelect partners={mine} value={form.partnerId} onChange={v => setForm({ ...form, partnerId: v })} /></label>
          <label>Loại<select aria-label="Loại hợp đồng" value={form.category} onChange={e => setForm({ ...form, category: e.target.value as Row['category'] })}>{(Object.keys(CATEGORY_VI) as (keyof typeof CATEGORY_VI)[]).map(k => <option key={k} value={k}>{CATEGORY_VI[k]}</option>)}</select></label>
          <label>Hiệu lực từ<input type="date" value={form.effective} onChange={e => setForm({ ...form, effective: e.target.value })} /></label>
          <label>Hết hạn<input type="date" aria-label="Ngày hết hạn" value={form.expiration} onChange={e => setForm({ ...form, expiration: e.target.value })} /></label>
          {form.category === 'Value' && <label>Tổng giá trị (hạn mức)<input className="num" aria-label="Tổng giá trị hợp đồng" value={form.total} onChange={e => setForm({ ...form, total: e.target.value })} /></label>}
          <label style={{ flex: 1, minWidth: 220 }}>Điều khoản thanh toán<input value={form.terms} onChange={e => setForm({ ...form, terms: e.target.value })} /></label>
        </div>
        <table className="grid"><thead><tr><th>Vật tư, hàng hóa cam kết</th><th className="num">Số lượng cam kết</th><th className="num">Đơn giá</th><th className="num">Chiết khấu %</th><th className="num">Thuế %</th><th /></tr></thead>
          <tbody>{form.lines.map((l, i) => <tr key={i}>
            <td><select aria-label={`Vật tư HĐ ${i + 1}`} value={l.itemId} onChange={e => setLine(i, { itemId: e.target.value })}><option value="">—</option>{items.map(it => <option key={it.id} value={it.id}>{it.code} – {it.name} ({it.unit})</option>)}</select></td>
            <td><input className="num" aria-label={`Số lượng HĐ ${i + 1}`} value={l.qty} onChange={e => setLine(i, { qty: e.target.value })} size={9} /></td>
            <td><input className="num" aria-label={`Đơn giá HĐ ${i + 1}`} value={l.price} onChange={e => setLine(i, { price: e.target.value })} size={11} /></td>
            <td><input className="num" value={l.discount} onChange={e => setLine(i, { discount: e.target.value })} size={4} /></td>
            <td><input className="num" value={l.tax} onChange={e => setLine(i, { tax: e.target.value })} size={4} /></td>
            <td><button onClick={() => setForm({ ...form, lines: form.lines.filter((_, k) => k !== i) })}>Bỏ</button></td></tr>)}</tbody></table>
        <button onClick={() => setForm({ ...form, lines: [...form.lines, { itemId: '', qty: '', price: '', discount: '', tax: '' }] })}>+ Thêm dòng</button>
        <table className="grid" style={{ marginTop: 8 }}><thead><tr><th>Mốc thanh toán / tạm ứng / nghiệm thu</th><th className="num">Số tiền</th><th>Hạn</th><th /></tr></thead>
          <tbody>{form.ms.map((m, i) => <tr key={i}><td><input aria-label={`Tên mốc ${i + 1}`} value={m.name} onChange={e => setMs(i, { name: e.target.value })} size={30} /></td>
            <td><input className="num" aria-label={`Số tiền mốc ${i + 1}`} value={m.amount} onChange={e => setMs(i, { amount: e.target.value })} size={14} /></td>
            <td><input type="date" value={m.date} onChange={e => setMs(i, { date: e.target.value })} /></td>
            <td><button onClick={() => setForm({ ...form, ms: form.ms.filter((_, k) => k !== i) })}>Bỏ</button></td></tr>)}</tbody></table>
        <div className="toolbar"><button onClick={() => setForm({ ...form, ms: [...form.ms, { name: '', amount: '', date: today() }] })}>+ Thêm mốc</button>
          <button className="primary" disabled={busy || !form.name.trim() || !form.partnerId || !form.expiration} onClick={save}>{form.id ? 'Lưu dự thảo' : 'Lập hợp đồng'}</button>
          <button onClick={() => setForm(null)}>Đóng</button></div>
      </section>}

      <section className="panel">
        <SmartFilter label="Hợp đồng" dateLabel="thời gian hiệu lực hợp đồng giao với khoảng ngày" state={f} patch={pf} clear={cf} shown={shownRows.length} total={rows.length} placeholder="Tìm mã, tên, đối tác; ví dụ: khach hang a >1ty"
          selects={[{ key: 'status', label: 'Trạng thái', options: [...(Object.keys(STATUS_VI) as CStatus[]).map(k => ({ value: k, label: STATUS_VI[k] })), { value: 'Expired', label: 'Hết hạn, chưa thanh lý' }] }]}
          chips={[{ key: 'expiring', label: 'Sắp hết hạn (≤ 60 ngày)', count: rows.filter(chipTests.expiring).length }, { key: 'expired', label: 'Đã hết hạn', count: rows.filter(chipTests.expired).length },
            { key: 'nearLimit', label: 'Dùng ≥ 90% hạn mức', count: rows.filter(chipTests.nearLimit).length }, { key: 'pending', label: 'Chờ duyệt', count: rows.filter(chipTests.pending).length }]} />
        <table className="grid"><thead><tr><th>Mã</th><th>Tên</th><th>Đối tác</th><th>Loại</th><th>Hiệu lực</th><th className="num">Giá trị</th><th className="num">Đã đặt</th><th className="num">% thực hiện</th><th>Trạng thái</th></tr></thead>
          <tbody>{shownRows.map(r => <tr key={r.id}><td><button className="link" onClick={() => open(r.id)}>{r.code}</button></td><td>{r.name}</td><td>{r.partnerCode} – {r.partnerName}</td><td>{CATEGORY_VI[r.category]}</td>
            <td>{formatDate(r.effectiveDate)} → {formatDate(r.expirationDate)}</td><td className="num">{formatMoney(r.totalAmount)}</td><td className="num">{formatMoney(r.orderedAmount)}</td><td className="num">{formatMoney(r.percentDone)}</td>
            <td>{r.expired ? 'Hết hạn, chưa thanh lý' : STATUS_VI[r.status]}{r.version > 1 ? ` · v${r.version}` : ''}</td></tr>)}
            {shownRows.length === 0 && <tr><td colSpan={9} className="muted">{rows.length === 0 ? 'Chưa có hợp đồng.' : 'Không có hợp đồng khớp bộ lọc.'}</td></tr>}</tbody></table>
      </section>

      {view && h && <section className="panel" aria-label="Chi tiết hợp đồng">
        <div className="toolbar"><h3>{h.code} – {h.name}</h3><button onClick={() => setView(null)}>Đóng</button></div>
        <p>{h.partnerCode} – {h.partnerName} · {CATEGORY_VI[h.category]} · {formatDate(h.effectiveDate)} → {formatDate(h.expirationDate)}{h.daysToExpire >= 0 && h.status === 'Active' ? ` (còn ${h.daysToExpire} ngày)` : ''} · {STATUS_VI[h.status]}{view.statusReason ? ` — ${view.statusReason}` : ''}</p>
        {view.paymentTerms && <p className="muted">Thanh toán: {view.paymentTerms}</p>}
        <div className="toolbar">
          {h.status === 'Draft' && <><button disabled={busy} onClick={() => edit(view)}>Sửa dự thảo</button><button className="primary" disabled={busy} onClick={() => act(`Đã gửi duyệt ${h.code}.`, () => post(`/contracts/${h.id}/submit`))}>Gửi duyệt</button></>}
          {h.status === 'PendingApproval' && <><button className="primary" disabled={busy} onClick={() => act(`Đã duyệt ${h.code}.`, () => post(`/contracts/${h.id}/decide`, { approve: true, note: null }))}>Duyệt</button>
            <button disabled={busy} onClick={() => { const note = ask('Lý do từ chối:'); if (note) act(`Đã trả ${h.code} về nháp.`, () => post(`/contracts/${h.id}/decide`, { approve: false, note })) }}>Từ chối</button></>}
          {h.status === 'Active' && <><button disabled={busy} onClick={() => setAmend({ summary: '', value: '', expiry: '', qty: {} })}>Lập phụ lục</button>
            <button disabled={busy} onClick={() => { const note = ask('Ghi chú thanh lý (đã thực hiện đủ / hết hiệu lực):'); if (note) act(`Đã thanh lý ${h.code}.`, () => post(`/contracts/${h.id}/complete`, { note })) }}>Thanh lý, đóng</button></>}
          {['Draft', 'PendingApproval', 'Active'].includes(h.status) && <button disabled={busy} onClick={() => { const reason = ask('Lý do hủy hợp đồng:'); if (reason) act(`Đã hủy ${h.code}.`, () => post(`/contracts/${h.id}/cancel`, { reason })) }}>Hủy</button>}
          <button disabled={busy} onClick={() => { const n = ask('Tên tệp (VD: HD-da-ky.pdf):'); const pth = n && ask('Đường dẫn lưu trữ tệp:'); if (n && pth) act('Đã đính kèm tài liệu.', () => post(`/contracts/${h.id}/documents`, { fileName: n, filePath: pth, fileType: n.split('.').pop() })) }}>Đính kèm tài liệu</button>
        </div>
        {amend && <div className="form-row"><strong>Phụ lục</strong>
          <label>Nội dung<input aria-label="Nội dung phụ lục" value={amend.summary} onChange={e => setAmend({ ...amend, summary: e.target.value })} size={34} /></label>
          <label>Tăng / giảm giá trị<input className="num" aria-label="Giá trị phụ lục" value={amend.value} onChange={e => setAmend({ ...amend, value: e.target.value })} /></label>
          <label>Gia hạn đến<input type="date" value={amend.expiry} onChange={e => setAmend({ ...amend, expiry: e.target.value })} /></label>
          {view.lines.map(l => <label key={l.id}>Thêm/bớt SL {l.itemCode}<input className="num" aria-label={`Thay đổi số lượng ${l.itemCode}`} value={amend.qty[l.itemId] ?? ''} onChange={e => setAmend({ ...amend, qty: { ...amend.qty, [l.itemId]: e.target.value } })} size={8} /></label>)}
          <button className="primary" disabled={busy || !amend.summary.trim()} onClick={() => {
            const value = amend.value.trim() ? parseMoney(amend.value) : '0'
            const changes = Object.entries(amend.qty).filter(([, v]) => v.trim()).map(([itemId, v]) => ({ itemId, qtyDelta: parseMoney(v) }))
            if (value == null || changes.some(c => c.qtyDelta == null)) { setError(new Error('Có ô số không hợp lệ.')); return }
            act('Đã lập phụ lục.', async () => { await post(`/contracts/${h.id}/amend`, { date: today(), summary: amend.summary, valueChange: value, newExpirationDate: amend.expiry || null, lineChanges: changes }); setAmend(null) })
          }}>Lưu phụ lục</button><button onClick={() => setAmend(null)}>Đóng</button></div>}
        <h4>Hàng hóa cam kết và thực hiện (trừ lùi)</h4>
        <table className="grid"><thead><tr><th>Vật tư</th><th className="num">Cam kết</th><th className="num">Đơn giá</th><th className="num">CK %</th><th className="num">Giá sau CK</th><th className="num">Đã đặt</th><th className="num">Còn lại</th><th className="num">%</th><th className="num">Giá trị cam kết</th></tr></thead>
          <tbody>{view.lines.map(l => <tr key={l.id}><td>{l.itemCode} – {l.itemName}</td><td className="num">{formatMoney(l.committedQty)} {l.unit}</td><td className="num">{formatMoney(l.unitPrice)}</td><td className="num">{formatMoney(l.discountRate)}</td><td className="num">{formatMoney(l.netPrice)}</td>
            <td className="num">{formatMoney(l.orderedQty)}</td><td className="num">{formatMoney(l.remainingQty)}</td><td className="num">{formatMoney(l.percentDone)}</td><td className="num">{formatMoney(l.committedAmount)}</td></tr>)}
            {view.lines.length === 0 && <tr><td colSpan={9} className="muted">Hợp đồng khung giá trị: hạn mức {formatMoney(h.totalAmount)}, đã đặt {formatMoney(h.orderedAmount)}.</td></tr>}</tbody></table>
        <h4>Mốc thanh toán</h4>
        <table className="grid"><thead><tr><th>Mốc</th><th>Hạn</th><th className="num">Phải thanh toán</th><th className="num">Đã thanh toán</th><th>Tình trạng</th><th /></tr></thead>
          <tbody>{view.milestones.map(m => <tr key={m.id}><td>{m.name}</td><td>{formatDate(m.dueDate)}</td><td className="num">{formatMoney(m.dueAmount)}</td><td className="num">{formatMoney(m.paidAmount)}</td><td>{PAY_VI[m.paymentStatus]}</td>
            <td>{['Active', 'Completed'].includes(h.status) && m.paymentStatus !== 'PAID' && <button disabled={busy} onClick={() => { const t = ask('Số tiền đã thanh toán đợt này:'); const amt = t ? parseMoney(t) : null; if (amt) act('Đã ghi nhận thanh toán.', () => post(`/contracts/${h.id}/milestones/${m.id}/pay`, { amount: amt, date: today() })) }}>Ghi nhận thanh toán</button>}</td></tr>)}
            {view.milestones.length === 0 && <tr><td colSpan={6} className="muted">Chưa khai mốc thanh toán.</td></tr>}</tbody></table>
        <h4>Đơn hàng kế thừa</h4>
        <table className="grid"><thead><tr><th>Đơn</th><th>Ngày</th><th>Trạng thái</th><th className="num">Giá trị</th></tr></thead>
          <tbody>{view.orders.map(o => <tr key={o.orderId}><td>{o.number}</td><td>{formatDate(o.orderDate)}</td><td>{ORDER_VI[o.status] ?? o.status}</td><td className="num">{formatMoney(o.amount)}</td></tr>)}
            {view.orders.length === 0 && <tr><td colSpan={4} className="muted">Chưa có đơn kế thừa. Lập ở màn Đơn hàng, chọn "Theo hợp đồng".</td></tr>}</tbody></table>
        {view.amendments.length > 0 && <><h4>Phụ lục</h4><ul>{view.amendments.map(a => <li key={a.id}>{a.code} ({formatDate(a.amendmentDate)}): {a.summary}{a.valueChange !== '0' ? ` · giá trị ${formatMoney(a.valueChange)}` : ''}{a.newExpirationDate ? ` · gia hạn đến ${a.newExpirationDate}` : ''}</li>)}</ul></>}
        {view.documents.length > 0 && <><h4>Tài liệu</h4><ul>{view.documents.map(d => <li key={d.id}>{d.fileName} — {d.filePath}</li>)}</ul></>}
      </section>}

      <section className="panel" aria-label="Báo cáo hợp đồng">
        <h3>Lịch thanh toán theo hợp đồng</h3>
        <table className="grid"><thead><tr><th>Hợp đồng</th><th>Đối tác</th><th>Mốc</th><th>Hạn</th><th className="num">Phải thanh toán</th><th className="num">Còn lại</th><th>Tình trạng</th></tr></thead>
          <tbody>{sched.map(m => <Fragment key={m.milestoneId}><tr><td>{m.contractCode}</td><td>{m.partnerName}</td><td>{m.name}</td><td>{formatDate(m.dueDate)}</td><td className="num">{formatMoney(m.dueAmount)}</td><td className="num">{formatMoney(m.remaining)}</td><td>{PAY_VI[m.paymentStatus as keyof typeof PAY_VI]}</td></tr></Fragment>)}
            {sched.length === 0 && <tr><td colSpan={7} className="muted">Chưa có mốc thanh toán.</td></tr>}</tbody></table>
        <h3>Chênh lệch giá so với hợp đồng</h3>
        <table className="grid"><thead><tr><th>Hợp đồng</th><th>Vật tư</th><th className="num">Giá cam kết (sau CK)</th><th className="num">SL đã đặt</th><th className="num">Giá bình quân thực tế</th><th className="num">Thấp nhất</th><th className="num">Cao nhất</th><th className="num">Chênh lệch %</th></tr></thead>
          <tbody>{variance.map((v, i) => <tr key={i}><td>{v.contractCode}</td><td>{v.itemCode} – {v.itemName}</td><td className="num">{formatMoney(v.contractPrice)}</td><td className="num">{formatMoney(v.orderedQty)}</td><td className="num">{formatMoney(v.averagePrice)}</td><td className="num">{formatMoney(v.minPrice)}</td><td className="num">{formatMoney(v.maxPrice)}</td><td className="num">{formatMoney(v.variancePercent)}</td></tr>)}
            {variance.length === 0 && <tr><td colSpan={8} className="muted">Chưa có đơn kế thừa để so giá.</td></tr>}</tbody></table>
      </section>
    </>
  )
}
