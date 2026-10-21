import { useCallback, useEffect, useMemo, useState } from 'react'
import { get, post } from './api'
import { ErrorBox, ExportButton, FlowStrip, Modal, useAction, navStore } from './common'
import { formatDate, formatMoney, monthStart, parseMoney, today } from './money'
import type { Order } from './Orders'
import { ServiceProgress } from './ServiceProgress'

type AStatus = 'Requested' | 'Approved' | 'Rejected' | 'Cancelled'
const STATUS_VI: Record<AStatus, string> = { Requested: 'Chờ xác nhận', Approved: 'Đã xác nhận', Rejected: 'Từ chối', Cancelled: 'Đã hủy' }
interface Line { orderLineNo: number; itemCode: string; itemName: string; unit: string; quantity: string; note: string | null }
interface Row { id: string; number: string; kind: 'Sales' | 'Purchase'; orderId: string; orderNumber: string; partnerCode: string; partnerName: string; acceptanceDate: string; status: AStatus; note: string | null; decisionNote: string | null; lines: Line[] }
interface Item { id: string; code: string; name: string; unit: string; isService?: boolean }
interface RRow { key: string; name: string; quantity: string; amount: string; vat: string; invoices: number }
interface Backlog { orderNumber: string; orderLineNo: number; partnerCode: string; partnerName: string; itemCode: string; itemName: string; quantity: string; unitPrice: string; amount: string }
interface Report { byService: RRow[]; byPartner: RRow[]; byMonth: RRow[]; total: string; totalVat: string; backlog: Backlog[]; backlogAmount: string }
interface Cand { orderId: string; orderNumber: string; orderLineNo: number; itemCode: string; itemName: string; ordered: string; accepted: string; invoiced: string; remaining: string }

/** Nghiệm thu dịch vụ (khâu thay cho giao / nhận hàng của dịch vụ: vận chuyển thuê, tư vấn…): lập biên bản theo dòng đơn hàng, người khác xác nhận, hóa đơn chỉ lập trong phạm vi đã nghiệm thu. */
export function ServiceAcceptancesView() {
  const [kind, setKind] = useState<'Sales' | 'Purchase'>(() => navStore.kind ?? 'Sales')
  const [rows, setRows] = useState<Row[]>([])
  const [orders, setOrders] = useState<Order[]>([])
  const [items, setItems] = useState<Item[]>([])
  const [cands, setCands] = useState<Cand[]>([])
  const [form, setForm] = useState<{ orderId: string; date: string; note: string; v: Record<number, string> } | null>(null)
  const [sub, setSub] = useState<'acc' | 'progress'>('acc')
  const [info, setInfo] = useState('')
  const [range, setRange] = useState({ from: monthStart(), to: today() })
  const [report, setReport] = useState<Report | null>(null)
  const [late, setLate] = useState<{ contractId: string; contractCode: string; partnerName: string; name: string; itemCode: string; plannedDate: string; daysLate: number; missing: string }[]>([])
  const { busy, error, run, setError } = useAction()
  const load = useCallback(() => run(async () => {
    setRows(await get<Row[]>(`/service-acceptances?kind=${kind}`))
    setOrders((await get<Order[]>(`/orders?kind=${kind}`)).filter(o => o.status === 'Confirmed'))
    setItems(await get<Item[]>('/items'))
    setCands(await get<Cand[]>(`/service-acceptances/invoice-candidates?kind=${kind}`))
    setLate(await get(`/service-acceptances/plan-overdue?kind=${kind}`))
  }), [run, kind])
  useEffect(() => { load() }, [load])
  const isService = (id: string) => items.find(i => i.id === id)?.isService
  const serviceOrders = useMemo(() => orders.filter(o => o.lines.some(l => isService(l.itemId))), [orders, items]) // eslint-disable-line react-hooks/exhaustive-deps
  const order = form ? orders.find(o => o.id === form.orderId) : undefined
  const act = (msg: string, fn: () => Promise<unknown>) => run(async () => { await fn(); setInfo(msg); await load() })
  const save = () => run(async () => {
    if (!form || !order) { setError(new Error('Chọn đơn hàng.')); return }
    const lines = Object.entries(form.v).filter(([, v]) => v.trim()).map(([n, v]) => ({ orderLineNo: Number(n), quantity: parseMoney(v) }))
    if (lines.length === 0 || lines.some(l => !l.quantity)) { setError(new Error('Nhập số lượng nghiệm thu cho ít nhất một dòng (số hợp lệ).')); return }
    await post('/service-acceptances', { orderId: order.id, acceptanceDate: form.date, note: form.note || null, lines })
    setForm(null); setInfo('Đã lập biên bản nghiệm thu; chờ người khác xác nhận.'); await load()
  })
  return (
    <>
      <div className="toolbar"><h2>Nghiệm thu dịch vụ</h2>
        <button className={kind === 'Sales' ? 'primary' : ''} onClick={() => setKind('Sales')}>Dịch vụ bán</button>
        <button className={kind === 'Purchase' ? 'primary' : ''} onClick={() => setKind('Purchase')}>Dịch vụ mua</button>
        <span className="muted">|</span>
        <button className={sub === 'acc' ? 'primary' : ''} onClick={() => setSub('acc')}>Biên bản nghiệm thu</button>
        <button className={sub === 'progress' ? 'primary' : ''} onClick={() => setSub('progress')}>Mốc, chấm giờ</button>
        {sub === 'acc' && <button onClick={() => setForm({ orderId: '', date: today(), note: '', v: {} })}>Lập biên bản nghiệm thu</button>}</div>
      {sub === 'progress' && <ServiceProgress orders={orders} items={items} onChanged={load} />}
      {sub === 'acc' && <>
      <FlowStrip steps={[
        { label: kind === 'Sales' ? 'Đơn bán dịch vụ' : 'Đơn mua dịch vụ', screen: 'orders', journal: false, note: 'không ghi Nhật ký' },
        { label: 'Nghiệm thu dịch vụ', journal: false, note: `${rows.filter(r => r.status === 'Requested').length} biên bản chờ xác nhận` },
        { label: kind === 'Sales' ? 'Hóa đơn bán' : 'Hóa đơn mua', screen: 'trade', journal: true, note: `ghi Nhật ký · ${cands.length} dòng chờ lập hóa đơn` },
        { label: kind === 'Sales' ? 'Thu tiền' : 'Thanh toán', screen: kind === 'Sales' ? 'revenue' : 'procurement', journal: true },
      ]} />
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      {late.length > 0 && <section className="panel"><h3>Đợt nghiệm thu quá hạn theo hợp đồng</h3>
        <div className="scroll"><table className="grid"><thead><tr><th>Hợp đồng</th><th>Đối tượng</th><th>Đợt</th><th>Dịch vụ</th><th>Ngày dự kiến</th><th className="num">Trễ (ngày)</th><th className="num">Còn thiếu</th></tr></thead>
          <tbody>{late.map((l, i) => <tr key={i}><td>{l.contractCode}</td><td>{l.partnerName}</td><td>{l.name}</td><td>{l.itemCode}</td><td>{formatDate(l.plannedDate)}</td><td className="num">{l.daysLate}</td><td className="num">{formatMoney(l.missing)}</td></tr>)}</tbody></table></div></section>}
      <section className="panel"><h3>Dịch vụ đã nghiệm thu, chờ lập hóa đơn</h3>
        <div className="scroll"><table className="grid"><thead><tr><th>Đơn</th><th>Dòng</th><th>Dịch vụ</th><th className="num">Đặt</th><th className="num">Đã nghiệm thu</th><th className="num">Đã lập hóa đơn</th><th className="num">Còn lập được</th></tr></thead>
          <tbody>{cands.map(c => <tr key={`${c.orderId}-${c.orderLineNo}`}><td>{c.orderNumber}</td><td>{c.orderLineNo}</td><td>{c.itemCode} – {c.itemName}</td><td className="num">{formatMoney(c.ordered)}</td><td className="num">{formatMoney(c.accepted)}</td><td className="num">{formatMoney(c.invoiced)}</td><td className="num"><b>{formatMoney(c.remaining)}</b></td></tr>)}
            {cands.length === 0 && <tr><td colSpan={7} className="muted">Không có dịch vụ nào chờ lập hóa đơn. Lập hóa đơn ở màn Hóa đơn, chọn dòng dịch vụ đã nghiệm thu.</td></tr>}</tbody></table></div></section>
      <section className="panel"><h3>Biên bản nghiệm thu</h3>
        <div className="scroll"><table className="grid"><thead><tr><th>Số</th><th>Ngày</th><th>Đơn</th><th>Đối tượng</th><th>Dịch vụ nghiệm thu</th><th>Trạng thái</th><th /></tr></thead>
          <tbody>{rows.map(r => <tr key={r.id}><td>{r.number}</td><td>{formatDate(r.acceptanceDate)}</td><td>{r.orderNumber}</td><td>{r.partnerCode} – {r.partnerName}</td>
            <td>{r.lines.map(l => `${l.itemCode} × ${formatMoney(l.quantity)} ${l.unit} (dòng ${l.orderLineNo})`).join('; ')}{r.note ? <div className="muted">{r.note}</div> : null}</td>
            <td>{STATUS_VI[r.status]}{r.decisionNote ? <div className="muted">{r.decisionNote}</div> : null}</td>
            <td>{r.status === 'Requested' && <><button className="primary" disabled={busy} title="Cần quyền duyệt chứng từ; khác người lập" onClick={() => act('Đã xác nhận nghiệm thu.', () => post(`/service-acceptances/${r.id}/decide`, { approve: true }))}>Xác nhận</button>
              <button disabled={busy} onClick={() => { const n = prompt('Lý do từ chối:'); if (n?.trim()) act('Đã từ chối.', () => post(`/service-acceptances/${r.id}/decide`, { approve: false, note: n.trim() })) }}>Từ chối</button></>}
              {(r.status === 'Requested' || r.status === 'Approved') && <button disabled={busy} onClick={() => { const n = prompt('Lý do hủy:'); if (n?.trim()) act('Đã hủy biên bản.', () => post(`/service-acceptances/${r.id}/cancel`, { reason: n.trim() })) }}>Hủy</button>}</td></tr>)}
            {rows.length === 0 && <tr><td colSpan={7} className="muted">Chưa có biên bản nghiệm thu.</td></tr>}</tbody></table></div></section>
      <section className="panel">
        <div className="toolbar"><strong>{kind === 'Sales' ? 'Báo cáo doanh thu dịch vụ' : 'Báo cáo chi phí dịch vụ mua'}</strong>
          <label>Từ ngày<input type="date" value={range.from} onChange={e => setRange(r => ({ ...r, from: e.target.value }))} /></label>
          <label>Đến ngày<input type="date" value={range.to} onChange={e => setRange(r => ({ ...r, to: e.target.value }))} /></label>
          <button className="primary" disabled={busy} onClick={() => run(async () => setReport(await get<Report>(`/service-acceptances/report?kind=${kind}&fromDate=${range.from}&toDate=${range.to}`)))}>Xem</button></div>
        {report && <>
          <p>{kind === 'Sales' ? 'Doanh thu' : 'Chi phí'} dịch vụ trong kỳ (chưa thuế, đã trừ trả lại, giảm giá): <b>{formatMoney(report.total)}</b> · Thuế GTGT: <b>{formatMoney(report.totalVat)}</b> ·
            Đã nghiệm thu chưa lập hóa đơn: <b>{formatMoney(report.backlogAmount)}</b></p>
          {([['Theo dịch vụ', report.byService, 'Dịch vụ'], ['Theo đối tượng', report.byPartner, kind === 'Sales' ? 'Khách hàng' : 'Nhà cung cấp'], ['Theo tháng', report.byMonth, 'Tháng']] as const).map(([title, rows, first]) => (
            <div key={title} style={{ marginTop: 8 }}>
              <div className="toolbar"><strong>{title}</strong><ExportButton fileName={`Dich_vu_${kind}_${title.replace(/ /g, '_')}_${range.from}_${range.to}`} sheet={title} title={`Báo cáo dịch vụ ${title.toLowerCase()}`} disabled={rows.length === 0}
                columns={[{ header: first, width: 34 }, { header: 'Số lượng', numeric: true }, { header: 'Giá trị', numeric: true }, { header: 'Thuế GTGT', numeric: true }, { header: 'Số HĐ', numeric: true }]}
                rows={rows.map(r => [r.key === r.name ? r.name : `${r.key} – ${r.name}`, r.quantity, r.amount, r.vat, String(r.invoices)])} /></div>
              <div className="scroll"><table className="grid"><thead><tr><th>{first}</th><th className="num">Số lượng</th><th className="num">Giá trị</th><th className="num">Thuế GTGT</th><th className="num">Số HĐ</th></tr></thead>
                <tbody>{rows.map(r => <tr key={r.key}><td>{r.key === r.name ? r.name : `${r.key} – ${r.name}`}</td><td className="num">{formatMoney(r.quantity)}</td><td className="num">{formatMoney(r.amount)}</td><td className="num">{formatMoney(r.vat)}</td><td className="num">{r.invoices}</td></tr>)}
                  {rows.length === 0 && <tr><td colSpan={5} className="muted">Không có hóa đơn dịch vụ trong kỳ.</td></tr>}</tbody></table></div></div>))}
          {report.backlog.length > 0 && <p className="muted">Phần đã nghiệm thu chưa lập hóa đơn (ước tính theo đơn giá đơn hàng): {report.backlog.map(b => `${b.orderNumber}/${b.orderLineNo} ${b.itemCode} × ${formatMoney(b.quantity)} = ${formatMoney(b.amount)}`).join('; ')}.</p>}
        </>}
      </section>
      </>}
      {form && <Modal title="Lập biên bản nghiệm thu dịch vụ" onClose={() => setForm(null)} busy={busy}
        footer={<><button onClick={() => setForm(null)}>Đóng</button><button className="primary" disabled={busy || !form.orderId} onClick={save}>Lưu</button></>}>
        <div className="form-row">
          <label>Đơn hàng (đã xác nhận, có dịch vụ)<select value={form.orderId} onChange={e => setForm({ ...form, orderId: e.target.value, v: {} })}><option value="">— chọn —</option>
            {serviceOrders.map(o => <option key={o.id} value={o.id}>{o.number} – {o.description}</option>)}</select></label>
          <label>Ngày nghiệm thu<input type="date" value={form.date} onChange={e => setForm({ ...form, date: e.target.value })} /></label>
          <label style={{ flex: 1 }}>Ghi chú<input value={form.note} onChange={e => setForm({ ...form, note: e.target.value })} /></label>
        </div>
        {order && <table className="grid"><thead><tr><th>Dòng</th><th>Dịch vụ</th><th className="num">Đặt</th><th className="num">Số lượng nghiệm thu lần này</th></tr></thead>
          <tbody>{order.lines.filter(l => isService(l.itemId)).map(l => { const it = items.find(i => i.id === l.itemId); return <tr key={l.lineNo}><td>{l.lineNo}</td><td>{it?.code} – {it?.name}</td><td className="num">{formatMoney(l.quantity)} {it?.unit}</td>
            <td className="num"><input inputMode="decimal" aria-label={`Số lượng nghiệm thu dòng ${l.lineNo}`} value={form.v[l.lineNo] ?? ''} onChange={e => setForm({ ...form, v: { ...form.v, [l.lineNo]: e.target.value } })} /></td></tr> })}</tbody></table>}
        <p className="muted">Số nghiệm thu mỗi dòng cộng dồn không vượt số lượng đặt (cộng dung sai của đơn). Biên bản được người khác xác nhận rồi mới lập được hóa đơn.</p>
      </Modal>}
    </>
  )
}
