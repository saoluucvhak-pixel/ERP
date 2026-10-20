import { useCallback, useEffect, useState } from 'react'
import { get, post } from './api'
import { ErrorBox, PartnerSelect, useAction, useCatalog } from './common'
import { formatMoney, parseMoney, today } from './money'
import { SmartFilter, useFilterState } from './SmartFilter'
import { matchesQuery } from './filter'

export interface Order { id: string; number: string; kind: string; partnerId: string; contractNo: string | null; orderDate: string; description: string; overDeliveryTolerancePercent: string; status: string; statusReason: string | null; lines: { lineNo: number; itemId: string; quantity: string; basis: string; unitPrice: string; vatRate: string | null }[] }
interface Item { id: string; code: string; name: string; unit: string; tracksDryness: boolean }
interface FulfilLine { lineNo: number; itemCode: string; ordered: string; basis: string; unitPrice: string; stockPosted: string; stockDraft: string; bdmt: string; invoiced: string; invoicedAmount: string; returnedQty: string; returnedAmount: string; outstanding: string }
interface Fulfil { number: string; status: string; lines: FulfilLine[]; invoices: { invoiceId: string; number: string; invoiceSeries: string; invoiceNo: string; purpose: string; total: string; settled: string; open: string }[] }
interface SalesCheck { orderId: string; number: string; partnerName: string; creditLimit: string | null; creditCheckApplied: boolean; creditCheckNote: string | null; currentBalance: string; overdueAmount: string
  orderValue: string; balanceAfter: string; exceedsCredit: boolean; anyShortage: boolean; lines: { lineNo: number; itemCode: string; itemName: string; unit: string; ordered: string; onHand: string; shortfall: string }[] }
interface ContractRef { id: string; code: string; name: string; kind: string; partnerId: string; expirationDate: string; status: string; expired: boolean }
interface ContractLineRef { itemId: string; netPrice: string; remainingQty: string }
interface LineDraft { itemId: string; qty: string; basis: 'Quantity' | 'Bdmt'; price: string; vat: string }

const STATUS_VI: Record<string, string> = { Draft: 'Nháp', Confirmed: 'Đã xác nhận', Closed: 'Đã đóng', Cancelled: 'Đã hủy' }

/** Đơn mua / đơn bán: kiểm soát số lượng nhận/giao (có dung sai) và theo dõi đơn → phiếu kho → hóa đơn → thanh toán. */
export function OrdersView() {
  const { partners } = useCatalog()
  const [items, setItems] = useState<Item[]>([])
  const [kind, setKind] = useState<'Sales' | 'Purchase'>('Sales')
  const [list, setList] = useState<Order[]>([])
  const [head, setHead] = useState({ partnerId: '', contractNo: '', orderDate: today(), description: '', tolerance: '0' })
  const [lines, setLines] = useState<LineDraft[]>([{ itemId: '', qty: '', basis: 'Bdmt', price: '', vat: '' }])
  const [detail, setDetail] = useState<Fulfil | null>(null)
  const [info, setInfo] = useState('')
  const [contracts, setContracts] = useState<ContractRef[]>([])
  const [f, pf, cf] = useFilterState('orders')
  const [contractId, setContractId] = useState('')
  const [contractLines, setContractLines] = useState<ContractLineRef[]>([])
  const { busy, error, setError, run } = useAction()
  useEffect(() => { get<ContractRef[]>(`/contracts?kind=${kind}`).then(c => setContracts(c.filter(x => x.status === 'Active' && !x.expired))).catch(() => setContracts([])); setContractId(''); setContractLines([]) }, [kind])
  useEffect(() => { get<Item[]>('/items').then(setItems).catch(() => setItems([])) }, [])
  const load = useCallback(() => run(async () => setList(await get<Order[]>(`/orders?kind=${kind}`))), [run, kind])
  useEffect(() => { load() }, [load])
  const upd = (i: number, p: Partial<LineDraft>) => setLines(ls => ls.map((l, j) => (j === i ? { ...l, ...p } : l)))
  const act = (fn: () => Promise<unknown>, msg: string) => run(async () => { await fn(); setInfo(msg); await load() })
  const [check, setCheck] = useState<SalesCheck | null>(null)
  /** Đơn bán: kiểm tra tín dụng và tồn kho trước khi xác nhận (tệp phân tích ERP, mục V.1.2.1); đơn mua xác nhận ngay. */
  const confirm = (o: Order) => o.kind === 'Sales'
    ? run(async () => setCheck(await get<SalesCheck>(`/cycles/revenue/orders/${o.id}/check`)))
    : act(() => post(`/orders/${o.id}/confirm`), `Đã xác nhận ${o.number}.`)

  const save = () => run(async () => {
    const parsed = lines.map(l => ({ ...l, q: parseMoney(l.qty), p: parseMoney(l.price), v: l.vat === '' ? null : parseMoney(l.vat) }))
    if (parsed.some(l => !l.itemId || !l.q || l.p == null)) { setError(new Error('Dòng đơn hàng thiếu vật tư, số lượng hoặc đơn giá.')); return }
    const item = (id: string) => items.find(i => i.id === id)
    await post('/orders', {
      kind, partnerId: head.partnerId, orderDate: head.orderDate, description: head.description, contractNo: head.contractNo || null, contractId: contractId || null,
      overDeliveryTolerancePercent: parseMoney(head.tolerance) ?? '0', currency: null, expectedDate: null,
      lines: parsed.map(l => ({ itemId: l.itemId, quantity: l.q, unitPrice: l.p, basis: item(l.itemId)?.tracksDryness ? l.basis : 'Quantity', vatRate: l.v, description: null })),
    })
    setInfo('Đã lập đơn hàng (nháp). Người khác có quyền duyệt sẽ xác nhận đơn.')
    setLines([{ itemId: '', qty: '', basis: 'Bdmt', price: '', vat: '' }])
    await load()
  })

  const orderChips: Record<string, (o: Order) => boolean> = { draft: o => o.status === 'Draft', open: o => o.status === 'Confirmed', byContract: o => !!o.contractNo }
  const shownList = list.filter(o => (!f.sel.status || o.status === f.sel.status) && (!f.chip || orderChips[f.chip]?.(o))
    && matchesQuery(`${o.number} ${o.orderDate} ${o.contractNo ?? ''} ${o.description} ${partners.find(p => p.id === o.partnerId)?.code ?? ''} ${partners.find(p => p.id === o.partnerId)?.name ?? ''}`, null, f.q))
  const n = (v: string) => (v === '0' ? '' : formatMoney(v))
  return (
    <>
      <div className="toolbar">
        <h2>Đơn hàng</h2>
        <button className={kind === 'Sales' ? 'primary' : ''} onClick={() => setKind('Sales')}>Đơn bán</button>
        <button className={kind === 'Purchase' ? 'primary' : ''} onClick={() => setKind('Purchase')}>Đơn mua</button>
      </div>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      <div className="panel">
        <div className="form-row">
          <label>{kind === 'Sales' ? 'Khách hàng' : 'Nhà cung cấp'}<PartnerSelect partners={partners} value={head.partnerId} onChange={v => setHead(h => ({ ...h, partnerId: v }))} /></label>
          <label>Số hợp đồng<input value={head.contractNo} onChange={e => setHead(h => ({ ...h, contractNo: e.target.value }))} /></label>
          <label title="Đơn kế thừa hợp đồng: lấy giá cam kết, trừ lùi số lượng còn lại; bị chặn khi hết hạn hoặc vượt cam kết">Theo hợp đồng<select aria-label="Theo hợp đồng" value={contractId} onChange={async e => {
            const id = e.target.value; setContractId(id)
            const c = contracts.find(x => x.id === id)
            if (!c) { setContractLines([]); return }
            setHead(h => ({ ...h, partnerId: c.partnerId, contractNo: c.code }))
            run(async () => setContractLines((await get<{ lines: ContractLineRef[] }>(`/contracts/${id}`)).lines))
          }}><option value="">— không —</option>{contracts.map(c => <option key={c.id} value={c.id}>{c.code} – {c.name}</option>)}</select></label>
          <label>Ngày đơn<input type="date" value={head.orderDate} onChange={e => setHead(h => ({ ...h, orderDate: e.target.value }))} /></label>
          <label>Dung sai giao vượt %<input className="num" size={4} value={head.tolerance} onChange={e => setHead(h => ({ ...h, tolerance: e.target.value }))} /></label>
          <label style={{ flex: 1, minWidth: 200 }}>Diễn giải<input value={head.description} onChange={e => setHead(h => ({ ...h, description: e.target.value }))} /></label>
          <button className="primary" style={{ alignSelf: 'flex-end' }} disabled={busy} onClick={save}>Lưu đơn nháp</button>
        </div>
        <table className="grid">
          <thead><tr><th>#</th><th>Vật tư</th><th className="num">Số lượng (ĐVT chính)</th><th>Giá theo</th><th className="num">Đơn giá</th><th className="num">Thuế %</th><th /></tr></thead>
          <tbody>{lines.map((l, i) => (
            <tr key={i}><td>{i + 1}</td>
              <td><select aria-label="Vật tư" value={l.itemId} onChange={e => { const cl = contractLines.find(c => c.itemId === e.target.value); upd(i, { itemId: e.target.value, ...(cl && !l.price ? { price: formatMoney(cl.netPrice) } : {}) }) }}><option value="">— chọn —</option>{(contractId ? items.filter(it => contractLines.length === 0 || contractLines.some(c => c.itemId === it.id)) : items).map(it => <option key={it.id} value={it.id}>{it.code} – {it.name} ({it.unit})</option>)}</select></td>
              <td><input className="num" aria-label="Số lượng" value={l.qty} onChange={e => upd(i, { qty: e.target.value })} /></td>
              <td><select aria-label="Giá theo" value={items.find(it => it.id === l.itemId)?.tracksDryness ? l.basis : 'Quantity'} disabled={!items.find(it => it.id === l.itemId)?.tracksDryness} onChange={e => upd(i, { basis: e.target.value as LineDraft['basis'] })}><option value="Bdmt">BDMT</option><option value="Quantity">Số lượng</option></select></td>
              <td><input className="num" aria-label="Đơn giá" value={l.price} onChange={e => upd(i, { price: e.target.value })} /></td>
              <td><input className="num" aria-label="Thuế suất" size={4} value={l.vat} placeholder="KCT" onChange={e => upd(i, { vat: e.target.value })} /></td>
              <td><button aria-label="Xóa dòng" onClick={() => setLines(ls => ls.filter((_, j) => j !== i))}>✕</button></td></tr>))}
          </tbody>
        </table>
        <button style={{ marginTop: 8 }} onClick={() => setLines(ls => [...ls, { itemId: '', qty: '', basis: 'Bdmt', price: '', vat: '' }])}>+ Thêm dòng</button>
      </div>
      {detail && (
        <div className="panel">
          <div className="toolbar"><strong>Tiến độ đơn {detail.number}</strong><button onClick={() => setDetail(null)}>Đóng</button></div>
          <table className="grid">
            <thead><tr><th>Dòng</th><th>Vật tư</th><th className="num">Đặt</th><th className="num">Đã {kind === 'Sales' ? 'giao' : 'nhận'}</th><th className="num">Phiếu nháp</th><th className="num">BDMT</th><th className="num">Đã lập HĐ</th><th className="num">Tiền HĐ</th><th className="num">Trả lại</th><th className="num">Còn lại</th></tr></thead>
            <tbody>{detail.lines.map(l => <tr key={l.lineNo}><td>{l.lineNo}</td><td>{l.itemCode}</td>
              {[l.ordered, l.stockPosted, l.stockDraft, l.bdmt, l.invoiced, l.invoicedAmount, l.returnedQty, l.outstanding].map((v, i) => <td key={i} className="num">{n(v)}</td>)}</tr>)}</tbody>
          </table>
          <table className="grid" style={{ marginTop: 8 }}>
            <thead><tr><th>Hóa đơn</th><th>Loại</th><th className="num">Tổng thanh toán</th><th className="num">Đã thu/trả</th><th className="num">Còn lại</th></tr></thead>
            <tbody>{detail.invoices.map(i => <tr key={i.invoiceId}><td>{i.number} · {i.invoiceSeries}/{i.invoiceNo}</td><td>{i.purpose}</td><td className="num">{formatMoney(i.total)}</td><td className="num">{formatMoney(i.settled)}</td><td className="num">{formatMoney(i.open)}</td></tr>)}
              {detail.invoices.length === 0 && <tr><td colSpan={5} className="muted">Chưa có hóa đơn.</td></tr>}</tbody>
          </table>
        </div>
      )}
      {check && (
        <div className="panel">
          <div className="toolbar"><strong>Kiểm tra tín dụng và tồn kho — {check.number} · {check.partnerName}</strong><button onClick={() => setCheck(null)}>Đóng</button></div>
          <p style={{ color: check.exceedsCredit ? 'var(--danger)' : undefined }}>
            {check.creditCheckApplied
              ? <>Hạn mức <b>{formatMoney(check.creditLimit)}</b> · số dư công nợ hiện tại <b>{formatMoney(check.currentBalance)}</b> (quá hạn {formatMoney(check.overdueAmount)}) + giá trị đơn ước tính <b>{formatMoney(check.orderValue)}</b> = <b>{formatMoney(check.balanceAfter)}</b>{check.exceedsCredit ? ' — VƯỢT HẠN MỨC.' : ' — trong hạn mức.'}</>
              : <span className="muted">{check.creditCheckNote}</span>}
          </p>
          <table className="grid">
            <thead><tr><th>Vật tư</th><th>ĐVT</th><th className="num">Đặt</th><th className="num">Tồn hiện tại</th><th className="num">Thiếu</th></tr></thead>
            <tbody>{check.lines.map(l => <tr key={l.lineNo}><td>{l.itemCode} – {l.itemName}</td><td>{l.unit}</td><td className="num">{formatMoney(l.ordered)}</td><td className="num">{formatMoney(l.onHand)}</td>
              <td className="num" style={{ color: l.shortfall !== '0' ? 'var(--danger)' : undefined }}>{l.shortfall === '0' ? '' : formatMoney(l.shortfall)}</td></tr>)}</tbody>
          </table>
          {check.anyShortage && <p className="muted">Thiếu hàng không chặn xác nhận: thỏa thuận ngày giao với khách và giao trễ phần thiếu.</p>}
          <div className="toolbar">
            {!check.exceedsCredit && <button className="primary" disabled={busy} onClick={() => act(() => post(`/orders/${check.orderId}/confirm`), `Đã xác nhận ${check.number}.`).then(() => setCheck(null))}>Xác nhận đơn</button>}
            {check.exceedsCredit && <button className="danger" disabled={busy} title="Chỉ người có quyền chấp thuận vượt hạn mức; lý do được ghi nhật ký" onClick={() => {
              const reason = prompt('Lý do chấp thuận bán vượt hạn mức tín dụng (người có thẩm quyền):')
              if (reason && reason.trim()) act(() => post(`/orders/${check.orderId}/confirm`, { creditOverrideReason: reason.trim() }), `Đã xác nhận ${check.number} vượt hạn mức (ghi nhật ký).`).then(() => setCheck(null))
            }}>Xác nhận vượt hạn mức…</button>}
          </div>
        </div>
      )}
      <div className="scroll">
        <SmartFilter label="Đơn hàng" state={f} patch={pf} clear={cf} shown={shownList.length} total={list.length} placeholder="Tìm số đơn, đối tượng, hợp đồng, diễn giải"
          selects={[{ key: 'status', label: 'Trạng thái', options: Object.entries(STATUS_VI).map(([value, label]) => ({ value, label })) }]}
          chips={[{ key: 'draft', label: 'Chờ xác nhận', count: list.filter(orderChips.draft).length }, { key: 'open', label: 'Đang thực hiện', count: list.filter(orderChips.open).length }, { key: 'byContract', label: 'Theo hợp đồng', count: list.filter(orderChips.byContract).length }]} />
                <table className="grid">
          <thead><tr><th>Số đơn</th><th>Ngày</th><th>Đối tượng</th><th>Hợp đồng</th><th>Diễn giải</th><th>Trạng thái</th><th /></tr></thead>
          <tbody>{shownList.map(o => (
            <tr key={o.id}><td>{o.number}</td><td>{o.orderDate}</td><td>{partners.find(p => p.id === o.partnerId)?.code}</td><td>{o.contractNo}</td><td>{o.description}</td>
              <td><span className="badge">{STATUS_VI[o.status] ?? o.status}</span></td>
              <td style={{ display: 'flex', gap: 4 }}>
                <button onClick={() => run(async () => setDetail(await get<Fulfil>(`/orders/${o.id}/fulfilment`)))}>Tiến độ</button>
                {o.status === 'Draft' && <button className="primary" onClick={() => confirm(o)}>{o.kind === 'Sales' ? 'Kiểm tra & xác nhận' : 'Xác nhận'}</button>}
                {o.status === 'Confirmed' && <button onClick={() => { const r = prompt('Lý do đóng đơn:'); if (r) act(() => post(`/orders/${o.id}/close`, { reason: r }), `Đã đóng ${o.number}.`) }}>Đóng đơn</button>}
                {(o.status === 'Draft' || o.status === 'Confirmed') && <button className="danger" onClick={() => { const r = prompt('Lý do hủy đơn:'); if (r) act(() => post(`/orders/${o.id}/cancel`, { reason: r }), `Đã hủy ${o.number}.`) }}>Hủy</button>}
              </td></tr>))}
            {shownList.length === 0 && <tr><td colSpan={7} className="muted">{list.length === 0 ? 'Chưa có đơn hàng.' : 'Không có đơn khớp bộ lọc.'}</td></tr>}
          </tbody>
        </table>
      </div>
    </>
  )
}
