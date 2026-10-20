import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { get, post } from './api'
import { ErrorBox, ExportButton, navStore, PrintSheet, printSheet, useAction, useCompanyName } from './common'
import { addMoney, amountInWords, formatMoney, parseMoney, today } from './money'
import type { Order } from './Orders'

interface DLine { lineNo: number; orderLineNo: number; itemId: string; itemCode: string; itemName: string; unit: string; quantity: string; delivered: string; outstanding: string }
interface DeliveryLine { lineNo: number; dispatchLineNo: number; itemCode: string; itemName: string; unit: string; quantity: string }
interface Delivery { id: string; number: string; deliveryDate: string; shipTo: string | null; carrier: string | null; deliveryStaff: string | null; waybillNo: string | null; note: string | null
  stockDocumentId: string; stockNumber: string; stage: string; invoiceNumber: string | null; invoiceId: string | null; lines: DeliveryLine[] }
interface Dispatch { id: string; number: string; orderId: string; orderNumber: string; partnerId: string; partnerName: string; partnerAddress: string | null; warehouseId: string; warehouseCode: string
  dispatchDate: string; shipTo: string | null; note: string | null; status: string; cancelReason: string | null; lines: DLine[]; deliveries: Delivery[] }
interface QueueRow { invoiceId: string; number: string; invoiceSeries: string; invoiceNo: string; invoiceDate: string; dueDate: string | null; daysOverdue: number; partnerId: string; partnerCode: string; partnerName: string
  total: string; open: string; settleJournalLineId: string }
interface OverdueInv { invoiceId: string; invoiceSeries: string; invoiceNo: string; invoiceDate: string; dueDate: string; daysOverdue: number; total: string; open: string }
interface Overdue { partnerId: string; partnerCode: string; partnerName: string; address: string | null; phone: string | null; email: string | null; totalOpen: string; maxDaysOverdue: number; invoices: OverdueInv[] }
interface Row { key: string; name: string; quantity: string; revenue: string; vat: string; invoices: number; cost: string; uncostedLines: number }
interface Analysis { byItem: Row[]; byPartner: Row[]; byMonth: Row[]; byType?: Row[]; totalRevenue: string; totalVat: string; totalCost: string; uncostedLines: number }
interface Collections { byPartner: { partnerCode: string; partnerName: string; collected: string; receipts: number }[]; total: string }
interface Warehouse { id: string; code: string; name: string }

const STATUS_VI: Record<string, string> = { Draft: 'Đang lập', Issued: 'Đã phát hành', Cancelled: 'Đã hủy' }
const STAGE_VI: Record<string, string> = { AWAITING_STOCK_POSTING: 'Chờ ghi sổ phiếu xuất kho', DELIVERED: 'Đã xuất kho, chưa có hóa đơn', INVOICE_DRAFT: 'Hóa đơn nháp', INVOICED: 'Đã lập hóa đơn', REVERSED: 'Phiếu xuất đã đảo', CANCELLED: 'Đã hủy' }
const d8 = (s: string) => s.split('-').reverse().join('/')

/** Khâu của chu trình theo tệp phân tích ERP (mục V.1): "ghi Nhật ký" = khâu đó phát sinh bút toán. */
const STEPS: { label: string; journal: boolean; screen?: string }[] = [
  { label: 'Đơn đặt hàng + kiểm tra tín dụng, tồn kho', journal: false, screen: 'orders' },
  { label: 'Lệnh bán hàng', journal: false },
  { label: 'Giao hàng (phiếu giao hàng, vận đơn, phiếu xuất kho)', journal: true, screen: 'stock' },
  { label: 'Hóa đơn bán hàng', journal: true, screen: 'trade' },
  { label: 'Theo dõi phải thu', journal: false, screen: 'arap' },
  { label: 'Thu tiền', journal: true, screen: 'cash' },
]

function PrintOnce({ children, onDone }: { children: ReactNode; onDone: () => void }) {
  useEffect(() => { const t = setTimeout(() => { printSheet(); onDone() }, 150); return () => clearTimeout(t) }, [onDone])
  return <PrintSheet>{children}</PrintSheet>
}

/** Chu trình doanh thu: lệnh bán hàng → phiếu giao hàng → hóa đơn bán → hàng chờ thu tiền → báo cáo bán hàng và thu tiền. */
export function RevenueView() {
  const company = useCompanyName()
  const [orders, setOrders] = useState<Order[]>([])
  const [dispatches, setDispatches] = useState<Dispatch[]>([])
  const [whs, setWhs] = useState<Warehouse[]>([])
  const [queue, setQueue] = useState<QueueRow[]>([])
  const [overdue, setOverdue] = useState<Overdue[]>([])
  const [info, setInfo] = useState('')
  const [print, setPrint] = useState<ReactNode>(null)
  const { busy, error, setError, run } = useAction()
  const asOf = today()

  const load = useCallback(() => run(async () => {
    setOrders(await get<Order[]>('/orders'))
    setDispatches(await get<Dispatch[]>('/cycles/revenue/dispatch-orders'))
    setQueue(await get<QueueRow[]>(`/cycles/revenue/receivable-queue?asOf=${asOf}`))
    setOverdue(await get<Overdue[]>(`/cycles/revenue/overdue?asOf=${asOf}`))
  }), [run, asOf])
  useEffect(() => { load() }, [load])
  useEffect(() => { get<Warehouse[]>('/warehouses').then(setWhs).catch(() => setWhs([])) }, [])
  const act = (label: string, fn: () => Promise<unknown>) => run(async () => { await fn(); setInfo(label); await load() })
  const ask = (q: string) => { const v = prompt(q); return v && v.trim() ? v.trim() : null }

  // ---- create dispatch
  const [orderId, setOrderId] = useState('')
  const [warehouseId, setWarehouseId] = useState('')
  const [shipTo, setShipTo] = useState('')
  const [note, setNote] = useState('')
  const [qty, setQty] = useState<Record<number, string>>({})
  const salesOrders = orders.filter(o => o.kind === 'Sales' && o.status === 'Confirmed')
  const order = salesOrders.find(o => o.id === orderId)
  const dispatched = (lineNo: number) => addMoney(dispatches.filter(d => d.orderId === orderId && d.status !== 'Cancelled').flatMap(d => d.lines).filter(l => l.orderLineNo === lineNo).map(l => l.quantity))
  const remaining = (lineNo: number, ordered: string) => { const r = addMoney([ordered, '-' + dispatched(lineNo)]); return r.startsWith('-') ? '0' : r }
  const createDispatch = () => run(async () => {
    if (!order) return
    const lines = order.lines.map(l => ({ orderLineNo: l.lineNo, q: parseMoney(qty[l.lineNo] ?? remaining(l.lineNo, l.quantity)) })).filter(l => l.q && l.q !== '0')
    if (lines.length === 0) { setError(new Error('Chưa có dòng nào có số lượng.')); return }
    const res = await post<{ number: string }>('/cycles/revenue/dispatch-orders', { orderId, warehouseId, dispatchDate: today(), shipTo: shipTo || null, note: note || null, lines: lines.map(l => ({ orderLineNo: l.orderLineNo, quantity: l.q })) })
    setInfo(`Đã lập lệnh bán hàng ${res.number} (Đang lập). Phát hành để kho giao hàng.`); setQty({}); setNote(''); await load()
  })

  // ---- create delivery
  const [delivering, setDelivering] = useState<{ d: Dispatch; qty: Record<number, string>; carrier: string; staff: string; waybill: string; date: string } | null>(null)
  const createDelivery = () => run(async () => {
    if (!delivering) return
    const lines = delivering.d.lines.map(l => ({ dispatchLineNo: l.lineNo, q: parseMoney(delivering.qty[l.lineNo] ?? l.outstanding) })).filter(l => l.q && l.q !== '0')
    if (lines.length === 0) { setError(new Error('Chưa có dòng nào có số lượng giao.')); return }
    const res = await post<{ number: string }>('/cycles/revenue/deliveries', { dispatchOrderId: delivering.d.id, deliveryDate: delivering.date, shipTo: null,
      carrier: delivering.carrier || null, deliveryStaff: delivering.staff || null, waybillNo: delivering.waybill || null, note: null, lines: lines.map(l => ({ dispatchLineNo: l.dispatchLineNo, quantity: l.q })) })
    setInfo(`Đã lập phiếu giao hàng ${res.number} và phiếu xuất kho Nháp. Ghi sổ phiếu xuất kho ở màn Kho (người khác ghi sổ).`); setDelivering(null); await load()
  })

  const allDeliveries = dispatches.flatMap(d => d.deliveries.map(x => ({ d, x })))
  const count = (stage: string) => allDeliveries.filter(a => a.x.stage === stage).length
  const stepCount: Record<number, number | null> = { 1: dispatches.filter(d => d.status !== 'Cancelled' && d.lines.some(l => l.outstanding !== '0')).length, 2: count('AWAITING_STOCK_POSTING'), 3: count('DELIVERED') + count('INVOICE_DRAFT'), 4: queue.length, 5: queue.filter(q => q.daysOverdue > 0).length }

  const copies = (title: string, labels: string[], body: ReactNode) => (
    <>{labels.map((l, i) => <div className="copy" key={i}>
      <div><b>{company}</b></div><h1>{title}</h1><div className="center"><i>Liên {i + 1}: {l}</i></div>{body}
      <div className="foot">Mẫu in nội bộ HAK Accounting — chưa đối chiếu mẫu theo chế độ kế toán [CẦN XÁC MINH]. In lúc {new Date().toLocaleString('vi-VN')}.</div></div>)}</>)
  const printDispatch = (d: Dispatch) => setPrint(copies('LỆNH BÁN HÀNG', ['Kho hàng (làm căn cứ xuất kho)', 'Bộ phận chuẩn bị hàng', 'Bộ phận lập hóa đơn', 'Khách hàng (thông báo chấp nhận đơn đặt hàng)'], <>
    <div className="center">Số: <b>{d.number}</b> · Ngày {d8(d.dispatchDate)} · Theo đơn đặt hàng {d.orderNumber}</div>
    <p>Khách hàng: <b>{d.partnerName}</b>{d.partnerAddress ? ` · ${d.partnerAddress}` : ''}</p><p>Nơi giao: {d.shipTo ?? '………………………………'} · Xuất từ kho: {d.warehouseCode}</p>
    <table><thead><tr><th>STT</th><th>Tên, mã hàng</th><th>ĐVT</th><th>Số lượng</th></tr></thead>
      <tbody>{d.lines.map(l => <tr key={l.lineNo}><td className="center">{l.lineNo}</td><td>{l.itemName} ({l.itemCode})</td><td className="center">{l.unit}</td><td className="num">{formatMoney(l.quantity)}</td></tr>)}</tbody></table>
    {d.note && <p>Ghi chú: {d.note}</p>}
    <div className="signs"><div><b>Người lập lệnh</b><br /><i>(Ký, họ tên)</i></div><div><b>Trưởng phòng kinh doanh</b><br /><i>(Ký, họ tên)</i></div><div><b>Kế toán trưởng</b><br /><i>(Ký, họ tên)</i></div></div></>))
  const printDelivery = (d: Dispatch, x: Delivery) => setPrint(copies('PHIẾU GIAO HÀNG', ['Gửi kèm hàng cho khách', 'Bộ phận lập hóa đơn', 'Bộ phận vận chuyển (kèm vận đơn)', 'Lưu tại kho hàng'], <>
    <div className="center">Số: <b>{x.number}</b> · Ngày {d8(x.deliveryDate)} · Theo lệnh bán hàng {d.number} · Phiếu xuất kho {x.stockNumber}</div>
    <p>Khách hàng: <b>{d.partnerName}</b> · Nơi nhận: {x.shipTo ?? '………………………………'}</p>
    <p>Đơn vị vận tải: {x.carrier ?? '……………………'} · Nhân viên giao hàng: {x.deliveryStaff ?? '……………………'} · Vận đơn số: {x.waybillNo ?? '……………'}</p>
    <table><thead><tr><th>STT</th><th>Tên, mã hàng</th><th>ĐVT</th><th>Số lượng</th></tr></thead>
      <tbody>{x.lines.map(l => <tr key={l.lineNo}><td className="center">{l.lineNo}</td><td>{l.itemName} ({l.itemCode})</td><td className="center">{l.unit}</td><td className="num">{formatMoney(l.quantity)}</td></tr>)}</tbody></table>
    <div className="signs"><div><b>Người lập phiếu</b><br /><i>(Ký, họ tên)</i></div><div><b>Thủ kho</b><br /><i>(Ký, họ tên)</i></div><div><b>Người giao hàng</b><br /><i>(Ký, họ tên)</i></div><div><b>Người nhận hàng</b><br /><i>(Ký, họ tên)</i></div></div></>))

  const printNotice = (n: Overdue) => setPrint(
    <div className="copy">
      <div><b>{company}</b></div><h1>THÔNG BÁO NỢ QUÁ HẠN</h1>
      <div className="center">Ngày {d8(asOf)}</div>
      <p>Kính gửi: <b>{n.partnerName}</b>{n.address ? ` · ${n.address}` : ''}{n.phone ? ` · ĐT ${n.phone}` : ''}</p>
      <p>Theo sổ sách của chúng tôi, đến ngày {d8(asOf)} quý khách còn các khoản nợ đã quá hạn thanh toán như sau:</p>
      <table><thead><tr><th>Hóa đơn</th><th>Ngày hóa đơn</th><th>Hạn thanh toán</th><th>Quá hạn (ngày)</th><th>Còn nợ</th></tr></thead>
        <tbody>{n.invoices.map(i => <tr key={i.invoiceId}><td>{i.invoiceSeries}/{i.invoiceNo}</td><td className="center">{d8(i.invoiceDate)}</td><td className="center">{d8(i.dueDate)}</td><td className="num">{i.daysOverdue}</td><td className="num">{formatMoney(i.open)}</td></tr>)}</tbody>
        <tfoot><tr><td colSpan={4}><b>Cộng</b></td><td className="num"><b>{formatMoney(n.totalOpen)}</b></td></tr></tfoot></table>
      <p>Bằng chữ: <i>{amountInWords(n.totalOpen)}</i></p>
      <p>Đề nghị quý khách thanh toán trong thời gian sớm nhất hoặc liên hệ để thống nhất kế hoạch thanh toán. Nếu số liệu có chênh lệch, xin phản hồi để đối chiếu. Nếu quý khách đã thanh toán, xin bỏ qua thông báo này.</p>
      <div className="signs"><div><b>Kế toán công nợ</b><br /><i>(Ký, họ tên)</i></div><div><b>Kế toán trưởng</b><br /><i>(Ký, họ tên)</i></div><div><b>Giám đốc</b><br /><i>(Ký, họ tên, đóng dấu)</i></div></div>
      <div className="foot">Mẫu in nội bộ HAK Accounting — chưa đối chiếu mẫu theo chế độ kế toán [CẦN XÁC MINH]. In lúc {new Date().toLocaleString('vi-VN')}.</div>
    </div>)

  // ---- reports
  const [from, setFrom] = useState(today().slice(0, 8) + '01')
  const [to, setTo] = useState(today())
  const [analysis, setAnalysis] = useState<Analysis | null>(null)
  const [coll, setColl] = useState<Collections | null>(null)
  const loadReports = () => run(async () => { setAnalysis(await get<Analysis>(`/cycles/revenue/sales-analysis?from=${from}&to=${to}`)); setColl(await get<Collections>(`/cycles/revenue/collections?from=${from}&to=${to}`)) })
  const sub = (a: string, b: string) => addMoney([a, b.startsWith('-') ? b.slice(1) : '-' + b])
  const margin = (r: { revenue: string; cost: string }) => sub(r.revenue, r.cost)
  const rowCols = (first: string) => [{ header: first, width: 34 }, { header: 'Số lượng', numeric: true }, { header: 'Doanh thu (chưa thuế)', numeric: true }, { header: 'Thuế GTGT', numeric: true }, { header: 'Số hóa đơn', numeric: true }, { header: 'Giá vốn', numeric: true }, { header: 'Lãi gộp', numeric: true }]
  const rowData = (rows: Row[]) => rows.map(r => [r.key ? `${r.key} – ${r.name}` : r.name, r.quantity, r.revenue, r.vat, String(r.invoices), r.cost, margin(r)])

  return (
    <>
      {print && <PrintOnce onDone={() => setPrint(null)}>{print}</PrintOnce>}
      <div className="toolbar"><h2>Chu trình doanh thu: bán hàng → thu tiền</h2><button onClick={() => load()} disabled={busy}>Tải lại</button></div>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}

      <div className="panel" style={{ display: 'flex', gap: 6, flexWrap: 'wrap', alignItems: 'stretch' }}>
        {STEPS.map((s, i) => (
          <div key={s.label} style={{ flex: '1 1 150px', border: '1px solid var(--line)', borderRadius: 6, padding: '6px 8px', cursor: s.screen ? 'pointer' : undefined }} onClick={() => s.screen && navStore.go?.(s.screen)}>
            <div style={{ fontWeight: 600 }}>{i + 1}. {s.label}</div><div className="muted">{s.journal ? 'ghi Nhật ký' : 'không ghi Nhật ký'}</div>
            {stepCount[i] != null && <div>{stepCount[i]} {i === 1 ? 'lệnh còn hàng chờ giao' : i === 2 ? 'phiếu chờ ghi sổ xuất kho' : i === 3 ? 'phiếu chờ lập hóa đơn' : i === 4 ? 'hóa đơn còn nợ' : 'quá hạn'}</div>}
          </div>))}
      </div>

      <div className="panel">
        <strong>Lập lệnh bán hàng</strong><span className="muted"> · từ đơn bán đã xác nhận (đơn được kiểm tra tín dụng và tồn kho ở màn Đơn hàng)</span>
        <div className="form-row">
          <label>Đơn bán<select value={orderId} onChange={e => { setOrderId(e.target.value); setQty({}) }}><option value="">— chọn —</option>
            {salesOrders.map(o => <option key={o.id} value={o.id}>{o.number} · {o.description}</option>)}</select></label>
          <label>Kho xuất<select value={warehouseId} onChange={e => setWarehouseId(e.target.value)}><option value="">— chọn —</option>{whs.map(w => <option key={w.id} value={w.id}>{w.code} – {w.name}</option>)}</select></label>
          <label style={{ flex: 1, minWidth: 200 }}>Nơi giao<input value={shipTo} onChange={e => setShipTo(e.target.value)} /></label>
          <label style={{ flex: 1, minWidth: 160 }}>Ghi chú<input value={note} onChange={e => setNote(e.target.value)} /></label>
          <button className="primary" style={{ alignSelf: 'flex-end' }} disabled={busy || !order || !warehouseId} onClick={createDispatch}>Lập lệnh</button>
        </div>
        {order && <table className="grid">
          <thead><tr><th>Dòng</th><th className="num">Đặt</th><th className="num">Đã ra lệnh</th><th className="num">Còn lại</th><th className="num">Số lượng lệnh này</th></tr></thead>
          <tbody>{order.lines.map(l => <tr key={l.lineNo}><td>{l.lineNo} · {l.itemId.slice(0, 8)}</td><td className="num">{formatMoney(l.quantity)}</td><td className="num">{formatMoney(dispatched(l.lineNo))}</td>
            <td className="num">{formatMoney(remaining(l.lineNo, l.quantity))}</td>
            <td><input className="num" aria-label={`Số lượng lệnh dòng ${l.lineNo}`} value={qty[l.lineNo] ?? formatMoney(remaining(l.lineNo, l.quantity))} onChange={e => setQty(q => ({ ...q, [l.lineNo]: e.target.value }))} /></td></tr>)}</tbody>
        </table>}
      </div>

      <div className="panel">
        <strong>Lệnh bán hàng và phiếu giao hàng</strong>
        {dispatches.map(d => (
          <div key={d.id} style={{ borderTop: '1px solid var(--line)', marginTop: 8, paddingTop: 6 }}>
            <div className="toolbar">
              <strong>{d.number}</strong><span className="muted">{d8(d.dispatchDate)} · đơn {d.orderNumber} · {d.partnerName} · kho {d.warehouseCode}</span><span className="badge">{STATUS_VI[d.status] ?? d.status}</span>
              <span style={{ marginLeft: 'auto' }} />
              <button onClick={() => printDispatch(d)}>In lệnh (4 liên)</button>
              {d.status === 'Draft' && <button className="primary" disabled={busy} onClick={() => act(`Đã phát hành ${d.number}.`, () => post(`/cycles/revenue/dispatch-orders/${d.id}/issue`))}>Phát hành</button>}
              {d.status === 'Issued' && d.lines.some(l => l.outstanding !== '0') && <button className="primary" disabled={busy} onClick={() => setDelivering({ d, qty: {}, carrier: '', staff: '', waybill: '', date: today() })}>Lập phiếu giao hàng</button>}
              {d.status !== 'Cancelled' && d.deliveries.every(x => x.stage === 'CANCELLED') && <button className="danger" disabled={busy} onClick={() => { const r = ask('Lý do hủy lệnh:'); if (r) act('Đã hủy lệnh.', () => post(`/cycles/revenue/dispatch-orders/${d.id}/cancel`, { reason: r })) }}>Hủy lệnh</button>}
            </div>
            <table className="grid"><thead><tr><th>Hàng</th><th>ĐVT</th><th className="num">Lệnh</th><th className="num">Đã giao</th><th className="num">Còn lại</th></tr></thead>
              <tbody>{d.lines.map(l => <tr key={l.lineNo}><td>{l.itemCode} – {l.itemName}</td><td>{l.unit}</td><td className="num">{formatMoney(l.quantity)}</td><td className="num">{formatMoney(l.delivered)}</td><td className="num">{formatMoney(l.outstanding)}</td></tr>)}</tbody></table>
            {d.deliveries.length > 0 && <table className="grid" style={{ marginTop: 4 }}>
              <thead><tr><th>Phiếu giao hàng</th><th>Ngày</th><th>Vận tải / vận đơn</th><th>Phiếu xuất kho</th><th>Giai đoạn</th><th>Hóa đơn</th><th /></tr></thead>
              <tbody>{d.deliveries.map(x => (
                <tr key={x.id} style={x.stage === 'CANCELLED' ? { opacity: 0.55 } : undefined}>
                  <td>{x.number}</td><td>{d8(x.deliveryDate)}</td><td>{[x.carrier, x.deliveryStaff, x.waybillNo].filter(Boolean).join(' · ')}</td><td>{x.stockNumber}</td><td>{STAGE_VI[x.stage] ?? x.stage}</td><td>{x.invoiceNumber ?? ''}</td>
                  <td style={{ display: 'flex', gap: 4 }}>
                    <button onClick={() => printDelivery(d, x)}>In phiếu (4 liên)</button>
                    {x.stage === 'DELIVERED' && <button className="primary" disabled={busy} onClick={() => {
                      const series = ask('Ký hiệu hóa đơn (ví dụ 1C26TAA):'); if (!series) return
                      const no = ask('Số hóa đơn:'); if (!no) return
                      act(`Đã lập hóa đơn nháp từ ${x.number} (giá, thuế suất, hạn thanh toán tự điền). Ghi sổ hóa đơn ở màn Mua / Bán.`, () => post(`/cycles/revenue/deliveries/${x.id}/invoice`, { invoiceSeries: series, invoiceNo: no, invoiceDate: today() }))
                    }}>Lập hóa đơn</button>}
                    {x.stage === 'AWAITING_STOCK_POSTING' && <button className="danger" disabled={busy} onClick={() => { const r = ask('Lý do hủy phiếu giao hàng:'); if (r) act('Đã hủy phiếu giao hàng và phiếu xuất kho nháp.', () => post(`/cycles/revenue/deliveries/${x.id}/cancel`, { reason: r })) }}>Hủy</button>}
                  </td></tr>))}</tbody></table>}
          </div>))}
        {dispatches.length === 0 && <p className="muted">Chưa có lệnh bán hàng.</p>}
        {delivering && (
          <div className="panel" style={{ marginTop: 8 }}>
            <strong>Phiếu giao hàng theo lệnh {delivering.d.number}</strong>
            <div className="form-row">
              <label>Ngày giao<input type="date" value={delivering.date} onChange={e => setDelivering(v => v && { ...v, date: e.target.value })} /></label>
              <label>Đơn vị vận tải<input value={delivering.carrier} onChange={e => setDelivering(v => v && { ...v, carrier: e.target.value })} /></label>
              <label>Nhân viên giao hàng<input value={delivering.staff} onChange={e => setDelivering(v => v && { ...v, staff: e.target.value })} /></label>
              <label>Vận đơn số<input value={delivering.waybill} onChange={e => setDelivering(v => v && { ...v, waybill: e.target.value })} /></label>
            </div>
            <table className="grid"><thead><tr><th>Hàng</th><th className="num">Còn phải giao</th><th className="num">Giao lần này</th></tr></thead>
              <tbody>{delivering.d.lines.filter(l => l.outstanding !== '0').map(l => <tr key={l.lineNo}><td>{l.itemCode} – {l.itemName}</td><td className="num">{formatMoney(l.outstanding)}</td>
                <td><input className="num" aria-label={`Giao dòng ${l.lineNo}`} value={delivering.qty[l.lineNo] ?? formatMoney(l.outstanding)} onChange={e => setDelivering(v => v && { ...v, qty: { ...v.qty, [l.lineNo]: e.target.value } })} /></td></tr>)}</tbody></table>
            <div className="toolbar"><button className="primary" disabled={busy} onClick={createDelivery}>Lập phiếu giao hàng + phiếu xuất kho nháp</button><button onClick={() => setDelivering(null)}>Đóng</button></div>
          </div>)}
      </div>

      <div className="panel">
        <div className="toolbar"><strong>Hàng chờ thu tiền theo ngày đáo hạn</strong><span className="muted">hóa đơn bán VND đã ghi sổ còn nợ</span>
          <ExportButton fileName={`Cho_thu_tien_${asOf}`} sheet="Chờ thu tiền" title="Hóa đơn bán chờ thu tiền theo ngày đáo hạn" subtitle={`Đến ngày ${d8(asOf)}`} disabled={queue.length === 0}
            columns={[{ header: 'Đáo hạn' }, { header: 'Quá hạn (ngày)', numeric: true }, { header: 'Hóa đơn', width: 20 }, { header: 'Khách hàng', width: 34 }, { header: 'Còn nợ', numeric: true }]}
            rows={queue.map(r => [r.dueDate ?? '', String(r.daysOverdue), `${r.invoiceSeries}/${r.invoiceNo}`, `${r.partnerCode} – ${r.partnerName}`, r.open])} /></div>
        <table className="grid"><thead><tr><th>Đáo hạn</th><th>Hóa đơn</th><th>Khách hàng</th><th className="num">Còn nợ</th><th /></tr></thead>
          <tbody>{queue.map(r => (
            <tr key={r.invoiceId}><td style={{ color: r.daysOverdue > 0 ? 'var(--danger)' : undefined }}>{r.dueDate ?? '—'}{r.daysOverdue > 0 ? ` (quá hạn ${r.daysOverdue} ngày)` : ''}</td>
              <td>{r.invoiceSeries}/{r.invoiceNo}<div className="muted">{r.number}</div></td><td>{r.partnerCode} – {r.partnerName}</td>
              <td className="num">{formatMoney(r.open)}{r.open !== r.total && <div className="muted">/ {formatMoney(r.total)}</div>}</td>
              <td><button className="primary" onClick={() => { navStore.receipt = { partnerId: r.partnerId, settleJournalLineId: r.settleJournalLineId, amount: r.open }; navStore.go?.('cash') }}>Lập phiếu thu</button></td></tr>))}
            {queue.length === 0 && <tr><td colSpan={5} className="muted">Không có hóa đơn bán nào còn nợ.</td></tr>}</tbody></table>
      </div>

      <div className="panel">
        <div className="toolbar"><strong>Nợ quá hạn theo khách hàng</strong><span className="muted">in thông báo gửi khách (tệp mục V.1.1.2.9)</span></div>
        <table className="grid"><thead><tr><th>Khách hàng</th><th>Liên hệ</th><th className="num">Số hóa đơn</th><th className="num">Quá hạn lâu nhất (ngày)</th><th className="num">Tổng nợ quá hạn</th><th /></tr></thead>
          <tbody>{overdue.map(n => <tr key={n.partnerId}><td>{n.partnerCode} – {n.partnerName}</td><td>{[n.phone, n.email].filter(Boolean).join(' · ')}</td><td className="num">{n.invoices.length}</td>
            <td className="num" style={{ color: 'var(--danger)' }}>{n.maxDaysOverdue}</td><td className="num">{formatMoney(n.totalOpen)}</td><td><button onClick={() => printNotice(n)}>In thông báo</button></td></tr>)}
            {overdue.length === 0 && <tr><td colSpan={6} className="muted">Không có khách hàng nào nợ quá hạn.</td></tr>}</tbody></table>
      </div>

      <div className="panel">
        <div className="toolbar"><strong>Báo cáo bán hàng và thu tiền</strong>
          <label>Từ ngày<input type="date" value={from} onChange={e => setFrom(e.target.value)} /></label><label>Đến ngày<input type="date" value={to} onChange={e => setTo(e.target.value)} /></label>
          <button className="primary" disabled={busy} onClick={loadReports}>Xem</button></div>
        {analysis && <>
          <p>Doanh thu (chưa thuế, đã trừ hàng trả lại và giảm giá): <b>{formatMoney(analysis.totalRevenue)}</b> · Thuế GTGT: <b>{formatMoney(analysis.totalVat)}</b></p>
          {([['Theo loại (hàng hóa, dịch vụ)', analysis.byType ?? [], 'Loại'], ['Theo mặt hàng', analysis.byItem, 'Mặt hàng'], ['Theo khách hàng', analysis.byPartner, 'Khách hàng'], ['Theo tháng', analysis.byMonth, 'Tháng']] as const).map(([title, rows, first]) => (
            <div key={title} style={{ marginTop: 8 }}>
              <div className="toolbar"><strong>{title}</strong><ExportButton fileName={`Ban_hang_${title.replace(/ /g, '_')}_${from}_${to}`} sheet={title} title={`Phân tích tiêu thụ ${title.toLowerCase()}`} subtitle={`Từ ${d8(from)} đến ${d8(to)}`} disabled={rows.length === 0} columns={rowCols(first)} rows={rowData(rows)} /></div>
              <table className="grid"><thead><tr><th>{first}</th><th className="num">Số lượng</th><th className="num">Doanh thu</th><th className="num">Thuế GTGT</th><th className="num">Số HĐ</th><th className="num">Giá vốn</th><th className="num">Lãi gộp</th></tr></thead>
                <tbody>{rows.map(r => <tr key={r.key + r.name}><td>{r.key ? `${r.key} – ${r.name}` : r.name}</td><td className="num">{formatMoney(r.quantity)}</td><td className="num">{formatMoney(r.revenue)}</td><td className="num">{formatMoney(r.vat)}</td><td className="num">{r.invoices}</td><td className="num" title={r.uncostedLines ? `${r.uncostedLines} dòng chưa tính giá xuất` : undefined}>{formatMoney(r.cost)}{r.uncostedLines > 0 && ' *'}</td><td className="num">{formatMoney(margin(r))}</td></tr>)}
                  {rows.length === 0 && <tr><td colSpan={7} className="muted">Không có hóa đơn bán trong kỳ.</td></tr>}</tbody></table></div>))}
          <p>Giá vốn: <b>{formatMoney(analysis.totalCost)}</b> · Lãi gộp: <b>{formatMoney(sub(analysis.totalRevenue, analysis.totalCost))}</b></p>
          <p className="muted">Giá vốn lấy từ giá trị đã tính của phiếu xuất kho gắn với hóa đơn bán (hàng trả lại trừ ngược). {analysis.uncostedLines > 0 ? `* Có ${analysis.uncostedLines} dòng chưa tính giá xuất (chạy tính giá cuối tháng ở màn Kho) nên giá vốn đang thiếu; dòng dịch vụ không có giá vốn.` : 'Dòng dịch vụ không có giá vốn.'}</p>
        </>}
        {coll && <div style={{ marginTop: 8 }}>
          <div className="toolbar"><strong>Thu tiền bán hàng trong kỳ: {formatMoney(coll.total)}</strong><ExportButton fileName={`Thu_tien_ban_hang_${from}_${to}`} sheet="Thu tiền" title="Báo cáo thu tiền bán hàng" subtitle={`Từ ${d8(from)} đến ${d8(to)}`} disabled={coll.byPartner.length === 0}
            columns={[{ header: 'Khách hàng', width: 34 }, { header: 'Số phiếu', numeric: true }, { header: 'Đã thu', numeric: true }]} rows={coll.byPartner.map(r => [`${r.partnerCode} – ${r.partnerName}`, String(r.receipts), r.collected])} /></div>
          <table className="grid"><thead><tr><th>Khách hàng</th><th className="num">Số phiếu thu / báo có</th><th className="num">Đã thu</th></tr></thead>
            <tbody>{coll.byPartner.map(r => <tr key={r.partnerCode}><td>{r.partnerCode} – {r.partnerName}</td><td className="num">{r.receipts}</td><td className="num">{formatMoney(r.collected)}</td></tr>)}
              {coll.byPartner.length === 0 && <tr><td colSpan={3} className="muted">Không có phiếu thu trong kỳ.</td></tr>}</tbody></table></div>}
      </div>
    </>
  )
}
