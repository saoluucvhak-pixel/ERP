import { useCallback, useEffect, useState } from 'react'
import { get, post } from './api'
import { ErrorBox, useAction } from './common'
import { formatMoney } from './money'

interface Reservation { id: string; warehouseCode: string; itemCode: string; orderNumber: string; orderLineNo: number; quantity: string; consumed: string; remaining: string; status: string; createdAt: string; closeReason: string | null }
interface Availability { itemCode: string; onHand: string; reserved: string; available: string; incoming: string }
interface Item { id: string; code: string; name: string; unit: string }
const STATUS_VI: Record<string, string> = { Active: 'Đang giữ', Released: 'Đã nhả', Consumed: 'Đã xuất' }

/** Giữ hàng theo kho cho đơn bán: giữ không ghi sổ và không trừ tồn thực; xem tồn vật lý / đang giữ / khả dụng / đang về theo vật tư. */
export function ReservationsPanel() {
  const { busy, error, run } = useAction()
  const [activeOnly, setActiveOnly] = useState(true)
  const [rows, setRows] = useState<Reservation[]>([])
  const [items, setItems] = useState<Item[]>([])
  const [itemId, setItemId] = useState('')
  const [avail, setAvail] = useState<Availability | null>(null)
  const [info, setInfo] = useState('')

  const load = useCallback(() => run(async () => {
    setRows(await get<Reservation[]>(`/reservations?activeOnly=${activeOnly}`))
    setItems(await get<Item[]>('/items'))
  }), [run, activeOnly])
  useEffect(() => { load() }, [load])

  const release = (r: Reservation) => {
    const reason = prompt(`Lý do nhả giữ hàng ${r.itemCode} của đơn ${r.orderNumber}:`)
    if (reason && reason.trim()) run(async () => { await post(`/reservations/${r.id}/release`, { reason: reason.trim() }); setInfo('Đã nhả phần giữ.'); await load() })
  }
  return (
    <>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      <div className="panel">
        <strong>Tồn khả dụng theo vật tư</strong>
        <div className="form-row">
          <select aria-label="Vật tư" value={itemId} onChange={e => { setItemId(e.target.value); setAvail(null); if (e.target.value) run(async () => setAvail(await get<Availability>(`/stock/availability?itemId=${e.target.value}`))) }}>
            <option value="">— Chọn vật tư —</option>{items.map(i => <option key={i.id} value={i.id}>{i.code} – {i.name}</option>)}
          </select>
        </div>
        {avail && <table className="grid"><thead><tr><th className="num">Tồn vật lý</th><th className="num">Đang giữ</th><th className="num">Khả dụng</th><th className="num">Đang về</th></tr></thead>
          <tbody><tr><td className="num">{formatMoney(avail.onHand)}</td><td className="num">{formatMoney(avail.reserved)}</td><td className="num"><strong>{formatMoney(avail.available)}</strong></td><td className="num">{formatMoney(avail.incoming)}</td></tr></tbody></table>}
      </div>
      <div className="panel">
        <div className="toolbar"><strong>Hàng đang giữ cho đơn bán</strong>
          <label><input type="checkbox" checked={activeOnly} onChange={e => setActiveOnly(e.target.checked)} /> Chỉ phần đang giữ</label></div>
        <table className="grid">
          <thead><tr><th>Đơn</th><th>Dòng</th><th>Kho</th><th>Vật tư</th><th className="num">Giữ</th><th className="num">Đã xuất</th><th className="num">Còn giữ</th><th>Trạng thái</th><th /></tr></thead>
          <tbody>{rows.map(r => (
            <tr key={r.id}><td>{r.orderNumber}</td><td>{r.orderLineNo}</td><td>{r.warehouseCode}</td><td>{r.itemCode}</td><td className="num">{formatMoney(r.quantity)}</td><td className="num">{formatMoney(r.consumed)}</td><td className="num">{formatMoney(r.remaining)}</td>
              <td>{STATUS_VI[r.status] ?? r.status}{r.closeReason ? <div className="muted">{r.closeReason}</div> : null}</td>
              <td>{r.status === 'Active' && <button disabled={busy} onClick={() => release(r)}>Nhả giữ</button>}</td></tr>))}
            {rows.length === 0 && <tr><td colSpan={9} className="muted">Không có phần giữ hàng nào.</td></tr>}</tbody>
        </table>
      </div>
    </>
  )
}
