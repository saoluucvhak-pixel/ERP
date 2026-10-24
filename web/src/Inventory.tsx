import { useCallback, useEffect, useState } from 'react'
import { ReservationsPanel } from './Reservations'
import { toAlt, toMain } from './units'
import { api, get, post, type Period } from './api'
import { AccountSelect, ErrorBox, ExportButton, navStore, PartnerSelect, PrintSheet, printSheet, useAction, useCatalog, FlowStrip } from './common'
import { useUnsavedWhenChanged } from './unsaved'
import { useOpenTarget } from './opentarget'
import { addMoney, formatDate, formatMoney, formatMonth, parseMoney, today } from './money'
import type { Order } from './Orders'

interface Warehouse { id: string; code: string; name: string; policies: { method: string; effectiveFrom: string }[] }
interface Item { id: string; code: string; name: string; unit: string; tracksDryness?: boolean; altUnit?: string | null; altFactor?: string | null }
interface StockDoc { id: string; rowVersion: number; number: string; type: string; warehouseId: string; toWarehouseId: string | null; documentDate?: string; postingDate: string; description: string; partnerId?: string | null; status: string; voucherId: string | null; lines: { lineNo: number; itemId: string; quantity: string; amount: string | null; counterAccountId?: string | null; description?: string | null }[] }

const METHOD_VI: Record<string, string> = { Fifo: 'Nhập trước xuất trước', MovingAverage: 'Bình quân tức thời', MonthlyAverage: 'Bình quân cuối tháng' }
const TYPE_VI: Record<string, string> = { Receipt: 'Phiếu nhập', Issue: 'Phiếu xuất', Transfer: 'Điều chuyển' }
const STATUS_VI: Record<string, string> = { Draft: 'Nháp', Posted: 'Đã ghi sổ', Reversed: 'Đã đảo', Cancelled: 'Đã hủy' }
const whLabel = (w: Warehouse) => `${w.code} – ${w.name}${w.policies.length ? ' · ' + METHOD_VI[w.policies[w.policies.length - 1].method] : ' · chưa có phương pháp tính giá'}`

function useStockCatalog() {
  const [warehouses, setWarehouses] = useState<Warehouse[]>([])
  const [items, setItems] = useState<Item[]>([])
  useEffect(() => {
    get<Warehouse[]>('/warehouses').then(setWarehouses).catch(() => setWarehouses([]))
    get<Item[]>('/items').then(r => setItems(r.filter(i => !(i as Item & { isService?: boolean }).isService))).catch(() => setItems([]))
  }, [])
  return { warehouses, items }
}

export function InventoryView() {
  const [intent] = useState(() => navStore.intent)   // từ "Thêm nhanh": Nhập / Xuất / Kiểm kê
  const [tab, setTab] = useState<'docs' | 'count' | 'summary' | 'card' | 'costing' | 'bdmt' | 'reserve'>(intent?.stockTab ?? 'docs')
  const [counts, setCounts] = useState<{ draft: number; posted: number; pending: number }>({ draft: 0, posted: 0, pending: 0 })
  useEffect(() => {
    Promise.all([get<StockDoc[]>('/stock-documents'), get<unknown[]>('/measurements/pending').catch(() => [])]).then(([d, p]) => setCounts({ draft: d.filter(x => x.status === 'Draft').length, posted: d.filter(x => x.status === 'Posted').length, pending: p.length })).catch(() => undefined)
  }, [tab])
  const tabs = { docs: 'Phiếu kho', count: 'Kiểm kê', summary: 'Nhập – xuất – tồn', card: 'Thẻ kho', costing: 'Tính giá cuối tháng', bdmt: 'MT / BDMT', reserve: 'Giữ hàng' } as const
  return (
    <>
      <div className="toolbar">
        <h2>Kho</h2>
        {(Object.keys(tabs) as (keyof typeof tabs)[]).map(k => <button key={k} className={tab === k ? 'primary' : ''} onClick={() => setTab(k)}>{tabs[k]}</button>)}
      </div>
      <FlowStrip steps={[
        { label: 'Phiếu nhập / xuất / chuyển (nháp)', journal: false, count: `${counts.draft} phiếu nháp` },
        { label: 'Ghi sổ: sổ kho + Nhật ký (người khác ghi sổ)', journal: true, count: `${counts.posted} phiếu đã ghi sổ` },
        { label: 'Tính giá xuất cuối tháng (kho bình quân tháng)', journal: true, count: 'tab Tính giá cuối tháng' },
        { label: 'Kiểm kê định kỳ, đối chiếu sổ', journal: false, count: 'tab Kiểm kê' },
        { label: 'MT / BDMT (hàng theo dõi độ khô)', journal: false, count: `${counts.pending} dòng chờ kết quả đo` },
      ]} />
      {tab === 'docs' && <StockDocs initialType={intent?.stockType} />}
      {tab === 'count' && <StockCount />}
      {tab === 'summary' && <Summary />}
      {tab === 'card' && <StockCard />}
      {tab === 'costing' && <Costing />}
      {tab === 'bdmt' && <><Bdmt /><DailyRates /></>}
      {tab === 'reserve' && <ReservationsPanel />}
    </>
  )
}

interface LineDraft { itemId: string; quantity: string; amount: string; counterAccountId: string; orderLine: string }

function StockDocs({ initialType }: { initialType?: 'Receipt' | 'Issue' | 'Transfer' }) {
  const { accounts, partners } = useCatalog()
  const { warehouses, items } = useStockCatalog()
  const [list, setList] = useState<StockDoc[]>([])
  const [type, setType] = useState<'Receipt' | 'Issue' | 'Transfer'>(initialType ?? 'Receipt')
  const [warehouseId, setWarehouseId] = useState('')
  const [toWarehouseId, setToWarehouseId] = useState('')
  const [date, setDate] = useState(today())
  const [partnerId, setPartnerId] = useState('')
  const [description, setDescription] = useState('')
  const [lines, setLines] = useState<LineDraft[]>([{ itemId: '', quantity: '', amount: '', counterAccountId: '', orderLine: '' }])
  useUnsavedWhenChanged([date, partnerId, description, lines])
  const [info, setInfo] = useState('')
  const { busy, error, setError, run } = useAction()
  const [orders, setOrders] = useState<Order[]>([])
  const [from, setFrom] = useState(today().slice(0, 8) + '01')
  const [to, setTo] = useState(today())
  const [statusFilter, setStatusFilter] = useState('')
  const openId = useOpenTarget('stockDocument')
  const [selId, setSelId] = useState(openId ?? '')
  const [pinned, setPinned] = useState<StockDoc | null>(null)
  useEffect(() => { if (openId) get<StockDoc>(`/stock-documents/${openId}`).then(setPinned).catch(() => { /* không còn / không có quyền */ }) }, [openId])
  const load = useCallback(() => run(async () => { setList(await get<StockDoc[]>(`/stock-documents?from=${from}&to=${to}`)); setOrders(await get<Order[]>('/orders')) }), [run, from, to])
  useEffect(() => { load() }, [load])
  const orderKind = type === 'Receipt' ? 'Purchase' : type === 'Issue' ? 'Sales' : ''
  const orderLines = orders.filter(o => o.status === 'Confirmed' && o.kind === orderKind && o.partnerId === partnerId)
    .flatMap(o => o.lines.map(l => ({ key: `${o.id}:${l.lineNo}`, itemId: l.itemId, label: `${o.number}/${l.lineNo} · đặt ${formatMoney(l.quantity)}` })))
  const wh = (id: string | null) => warehouses.find(w => w.id === id)?.code ?? ''
  const shown = (pinned && !list.some(d => d.id === pinned.id) ? [pinned, ...list] : list).filter(d => d.id === openId || !statusFilter || d.status === statusFilter)
  const sel = (pinned && pinned.id === selId ? pinned : null) ?? list.find(d => d.id === selId) ?? null
  const upd = (i: number, p: Partial<LineDraft>) => setLines(ls => ls.map((l, j) => (j === i ? { ...l, ...p } : l)))

  const save = () => run(async () => {
    const parsed = lines.map(l => ({ q: parseMoney(l.quantity), a: type === 'Receipt' ? parseMoney(l.amount || '0') : null }))
    if (parsed.some(p => !p.q || (type === 'Receipt' && p.a == null))) { setError(new Error('Số lượng/giá trị không hợp lệ.')); return }
    const d = await post<StockDoc>('/stock-documents', {
      type, warehouseId, toWarehouseId: type === 'Transfer' ? toWarehouseId : null, documentDate: date, postingDate: date, description,
      partnerId: partnerId || null,
      lines: lines.map((l, i) => ({ itemId: l.itemId, quantity: parsed[i].q, amount: parsed[i].a, counterAccountId: type === 'Transfer' ? null : l.counterAccountId || null, description: null,
        orderId: l.orderLine ? l.orderLine.split(':')[0] : null, orderLineNo: l.orderLine ? Number.parseInt(l.orderLine.split(':')[1], 10) : null })),
    })
    setInfo(`Đã lập ${TYPE_VI[d.type]} ${d.number} (Nháp). Người khác có quyền ghi sổ sẽ ghi sổ phiếu.`)
    setLines([{ itemId: '', quantity: '', amount: '', counterAccountId: '', orderLine: '' }])
    await load()
  })
  const act = (fn: () => Promise<unknown>, msg: string) => run(async () => { await fn(); setInfo(msg); await load() })

  return (
    <>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      <div className="panel">
        <div className="form-row">
          <label>Loại phiếu<select value={type} onChange={e => setType(e.target.value as typeof type)}>{Object.entries(TYPE_VI).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</select></label>
          <label>{type === 'Transfer' ? 'Kho xuất' : 'Kho'}<select value={warehouseId} onChange={e => setWarehouseId(e.target.value)}><option value="">— chọn —</option>{warehouses.map(w => <option key={w.id} value={w.id}>{whLabel(w)}</option>)}</select></label>
          {type === 'Transfer' && <label>Kho nhận<select value={toWarehouseId} onChange={e => setToWarehouseId(e.target.value)}><option value="">— chọn —</option>{warehouses.map(w => <option key={w.id} value={w.id}>{whLabel(w)}</option>)}</select></label>}
          <label>Ngày<input type="date" value={date} onChange={e => setDate(e.target.value)} /></label>
          {type !== 'Transfer' && <label>Đối tượng<PartnerSelect partners={partners} value={partnerId} onChange={setPartnerId} /></label>}
          <label style={{ flex: 1, minWidth: 200 }}>Diễn giải<input value={description} onChange={e => setDescription(e.target.value)} /></label>
          <button className="primary" onClick={save} disabled={busy} style={{ alignSelf: 'flex-end' }}>Lưu nháp</button>
        </div>
        <table className="grid">
          <thead><tr><th>#</th><th>Vật tư</th><th className="num">Số lượng</th>{type === 'Receipt' && <th className="num">Giá trị</th>}{type !== 'Transfer' && <th>TK đối ứng</th>}{type !== 'Transfer' && <th>Theo đơn hàng</th>}<th /></tr></thead>
          <tbody>{lines.map((l, i) => (
            <tr key={i}><td>{i + 1}</td>
              <td><select aria-label="Vật tư" value={l.itemId} onChange={e => upd(i, { itemId: e.target.value })}><option value="">— chọn —</option>{items.map(it => <option key={it.id} value={it.id}>{it.code} – {it.name} ({it.unit})</option>)}</select></td>
              <td><input className="num" aria-label="Số lượng" value={l.quantity} onChange={e => upd(i, { quantity: e.target.value })} />
                {(() => { const it = items.find(x => x.id === l.itemId); if (!it?.altUnit || !it.altFactor) return null
                  const q = parseMoney(l.quantity); const alt = q ? toAlt(q, it.altFactor) : null
                  return <div className="muted" style={{ fontSize: 12 }}>= <input className="num" aria-label={`Số lượng theo ${it.altUnit}`} size={7} placeholder={it.altUnit} value={alt ? formatMoney(alt) : ''}
                    onChange={e => { const a = parseMoney(e.target.value); if (a) { const m = toMain(a, it.altFactor!); if (m) upd(i, { quantity: formatMoney(m) }) } else if (e.target.value.trim() === '') upd(i, { quantity: '' }) }} /> {it.altUnit}</div> })()}</td>
              {type === 'Receipt' && <td><input className="num" aria-label="Giá trị" value={l.amount} onChange={e => upd(i, { amount: e.target.value })} onBlur={() => { const p = parseMoney(l.amount); if (p) upd(i, { amount: formatMoney(p) }) }} /></td>}
              {type !== 'Transfer' && <td><AccountSelect accounts={accounts} value={l.counterAccountId} onChange={v => upd(i, { counterAccountId: v })} ariaLabel="TK đối ứng" /></td>}
              {type !== 'Transfer' && <td><select aria-label="Theo đơn hàng" value={l.orderLine} onChange={e => upd(i, { orderLine: e.target.value })}><option value="">—</option>{orderLines.filter(o => !l.itemId || o.itemId === l.itemId).map(o => <option key={o.key} value={o.key}>{o.label}</option>)}</select></td>}
              <td><button aria-label="Xóa dòng" onClick={() => setLines(ls => ls.filter((_, j) => j !== i))}>✕</button></td></tr>))}
          </tbody>
        </table>
        <button style={{ marginTop: 8 }} onClick={() => setLines(ls => [...ls, { itemId: '', quantity: '', amount: '', counterAccountId: '', orderLine: '' }])}>+ Thêm dòng</button>
        <p className="muted">Giá xuất kho do hệ thống tính theo phương pháp của kho; kho bình quân cuối tháng chỉ có giá trị sau khi chạy “Tính giá cuối tháng”.</p>
      </div>
      <div className="toolbar">
        <label>Từ ngày<input type="date" value={from} onChange={e => setFrom(e.target.value)} /></label>
        <label>Đến ngày<input type="date" value={to} onChange={e => setTo(e.target.value)} /></label>
        <label>Trạng thái<select value={statusFilter} onChange={e => setStatusFilter(e.target.value)}><option value="">Tất cả</option>{Object.entries(STATUS_VI).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</select></label>
        <ExportButton fileName={`Phieu_kho_${from}_${to}`} sheet="Phiếu kho" disabled={shown.length === 0}
          columns={[{ header: 'Ngày' }, { header: 'Số' }, { header: 'Loại' }, { header: 'Kho' }, { header: 'Diễn giải', width: 40 }, { header: 'Trạng thái' }]}
          rows={shown.map(d => [d.postingDate, d.number, TYPE_VI[d.type], wh(d.warehouseId) + (d.toWarehouseId ? ' → ' + wh(d.toWarehouseId) : ''), d.description, STATUS_VI[d.status] ?? d.status])} />
      </div>
      {sel && <StockDocDetail doc={sel} warehouses={warehouses} items={items} partners={partners} accounts={accounts} />}
      <div className="scroll">
        <table className="grid">
          <thead><tr><th>Ngày</th><th>Số</th><th>Loại</th><th>Kho</th><th>Diễn giải</th><th>Trạng thái</th><th /></tr></thead>
          <tbody>{shown.map(d => (
            <tr key={d.id} className={d.id === selId ? 'selected' : ''} style={{ cursor: 'pointer' }} onClick={() => setSelId(d.id)}><td>{formatDate(d.postingDate)}</td><td>{d.number}</td><td>{TYPE_VI[d.type]}</td><td>{wh(d.warehouseId)}{d.toWarehouseId ? ' → ' + wh(d.toWarehouseId) : ''}</td><td>{d.description}</td>
              <td><span className="badge">{STATUS_VI[d.status] ?? d.status}</span></td>
              <td style={{ display: 'flex', gap: 4 }} onClick={e => e.stopPropagation()}>
                {d.status === 'Draft' && <button className="primary" disabled={busy} onClick={() => act(() => post(`/stock-documents/${d.id}/post?rowVersion=${d.rowVersion}`), `Đã ghi sổ ${d.number}.`)}>Ghi sổ</button>}
                {d.status === 'Draft' && <button className="danger" onClick={() => { const r = prompt('Lý do hủy phiếu nháp:'); if (r) act(() => post(`/stock-documents/${d.id}/cancel?rowVersion=${d.rowVersion}`, { reason: r }), `Đã hủy ${d.number}.`) }}>Hủy</button>}
                {d.status === 'Posted' && !d.number.startsWith('DP') && <button className="danger" onClick={() => {
                  const reason = prompt('Lý do đảo phiếu:'); if (!reason) return
                  const dt = prompt('Ngày đảo (yyyy-mm-dd):', d.postingDate); if (dt) act(() => post(`/stock-documents/${d.id}/reverse?rowVersion=${d.rowVersion}`, { reversalDate: dt, reason }), `Đã đảo ${d.number}.`)
                }}>Đảo</button>}
              </td></tr>))}
            {shown.length === 0 && <tr><td colSpan={7} className="muted">Không có phiếu kho trong khoảng đã chọn.</td></tr>}
          </tbody>
        </table>
      </div>
    </>
  )
}

/** Chi tiết một phiếu kho + in Phiếu nhập kho / xuất kho / điều chuyển (mẫu in nội bộ). */
function StockDocDetail({ doc: d, warehouses, items, partners, accounts }: { doc: StockDoc; warehouses: Warehouse[]; items: Item[]; partners: { id: string; code: string; name: string }[]; accounts: { id: string; code: string }[] }) {
  const wh = (id: string | null) => { const w = warehouses.find(x => x.id === id); return w ? `${w.code} – ${w.name}` : '' }
  const it = (id: string) => items.find(x => x.id === id)
  const par = partners.find(p => p.id === d.partnerId)
  const total = addMoney(d.lines.map(l => l.amount ?? '0'))
  const dd = (s: string) => s.split('-').reverse().join('/')
  const title = d.type === 'Receipt' ? 'PHIẾU NHẬP KHO' : d.type === 'Issue' ? 'PHIẾU XUẤT KHO' : 'PHIẾU ĐIỀU CHUYỂN KHO'
  return (
    <div className="panel">
      <div className="toolbar">
        <strong>{d.number}</strong> <span className="muted">{TYPE_VI[d.type]} · {wh(d.warehouseId)}{d.toWarehouseId ? ' → ' + wh(d.toWarehouseId) : ''} · {formatDate(d.postingDate)} · {STATUS_VI[d.status] ?? d.status}</span>
        <span style={{ marginLeft: 'auto' }} /><button onClick={printSheet}>In phiếu</button>
      </div>
      <table className="grid">
        <thead><tr><th>#</th><th>Vật tư</th><th>ĐVT</th><th className="num">Số lượng</th><th className="num">Giá trị</th><th>TK đối ứng</th></tr></thead>
        <tbody>{d.lines.map(l => <tr key={l.lineNo}><td>{l.lineNo}</td><td>{it(l.itemId)?.code} – {it(l.itemId)?.name}</td><td>{it(l.itemId)?.unit}</td>
          <td className="num">{formatMoney(l.quantity)}</td><td className="num">{l.amount == null ? <span className="muted">chờ tính giá</span> : formatMoney(l.amount)}</td>
          <td>{accounts.find(a => a.id === l.counterAccountId)?.code ?? ''}</td></tr>)}</tbody>
      </table>
      <PrintSheet>
        <h1>{title}</h1>
        <div className="center">Số: <b>{d.number}</b> · Ngày {dd(d.postingDate)}</div>
        {d.status !== 'Posted' && <div className="center" style={{ marginTop: 6 }}><span className="stamp">{(STATUS_VI[d.status] ?? d.status).toUpperCase()} — CHƯA GHI SỔ</span></div>}
        <p>Họ tên người {d.type === 'Receipt' ? 'giao hàng' : 'nhận hàng'}: {par ? `${par.code} – ${par.name}` : '………………………………'}</p>
        <p>Lý do: {d.description}</p>
        <p>{d.type === 'Transfer' ? `Từ kho: ${wh(d.warehouseId)} → Đến kho: ${wh(d.toWarehouseId)}` : `Kho: ${wh(d.warehouseId)}`}</p>
        <table>
          <thead><tr><th>STT</th><th>Tên, mã vật tư</th><th>ĐVT</th><th>Số lượng</th><th>Thành tiền</th></tr></thead>
          <tbody>{d.lines.map(l => <tr key={l.lineNo}><td className="center">{l.lineNo}</td><td>{it(l.itemId)?.name} ({it(l.itemId)?.code})</td><td className="center">{it(l.itemId)?.unit}</td>
            <td className="num">{formatMoney(l.quantity)}</td><td className="num">{l.amount == null ? '' : formatMoney(l.amount)}</td></tr>)}</tbody>
          <tfoot><tr><td colSpan={4}><b>Cộng</b></td><td className="num"><b>{formatMoney(total)}</b></td></tr></tfoot>
        </table>
        <div className="signs"><div><b>Người lập phiếu</b><br /><i>(Ký, họ tên)</i></div><div><b>Người {d.type === 'Receipt' ? 'giao' : 'nhận'} hàng</b><br /><i>(Ký, họ tên)</i></div><div><b>Thủ kho</b><br /><i>(Ký, họ tên)</i></div><div><b>Kế toán trưởng</b><br /><i>(Ký, họ tên)</i></div></div>
        <div className="foot">Mẫu in nội bộ HAK Accounting — chưa đối chiếu với mẫu chứng từ theo chế độ kế toán [CẦN XÁC MINH]. Thành tiền phiếu xuất để trống nếu chưa tính giá.</div>
      </PrintSheet>
    </div>
  )
}

/**
 * Kiểm kê kho: tồn sổ sách theo ngày ↔ số lượng thực tế; chênh lệch sinh phiếu nhập (thừa, nhập giá trị) và phiếu xuất (thiếu, hệ thống tính giá)
 * ở trạng thái Nháp, TK đối ứng do người dùng chọn (không tự gán tài khoản xử lý chênh lệch).
 */
interface CountRow { itemId: string; code: string; name: string; unit: string; bookQty: string; actual: string; surplusValue: string }

function StockCount() {
  const { accounts } = useCatalog()
  const { warehouses } = useStockCatalog()
  const [warehouseId, setWarehouseId] = useState('')
  const [date, setDate] = useState(today())
  const [rows, setRows] = useState<CountRow[] | null>(null)
  const [surplusAcc, setSurplusAcc] = useState('')
  const [shortAcc, setShortAcc] = useState('')
  const [info, setInfo] = useState('')
  const { busy, error, setError, run } = useAction()
  const load = () => run(async () => {
    const s = await get<{ itemId: string; itemCode: string; itemName: string; unit: string; closingQty: string }[]>(`/reports/inventory-summary?from=${date}&to=${date}&warehouseId=${warehouseId}`)
    setRows(s.map(r => ({ itemId: r.itemId, code: r.itemCode, name: r.itemName, unit: r.unit, bookQty: r.closingQty, actual: '', surplusValue: '' })))
    setInfo('')
  })
  const upd = (i: number, p: Partial<CountRow>) => setRows(rs => rs && rs.map((r, j) => (j === i ? { ...r, ...p } : r)))
  const diff = (r: CountRow) => { const a = parseMoney(r.actual); return a == null ? null : addMoney([a, '-' + r.bookQty]) }
  const counted = (rows ?? []).filter(r => diff(r) != null)
  const surplus = counted.filter(r => !diff(r)!.startsWith('-') && diff(r) !== '0')
  const shortage = counted.filter(r => diff(r)!.startsWith('-'))
  const create = () => run(async () => {
    if (!warehouseId) return
    if (surplus.length && !surplusAcc) { setError(new Error('Chọn TK đối ứng cho hàng thừa.')); return }
    if (shortage.length && !shortAcc) { setError(new Error('Chọn TK đối ứng cho hàng thiếu.')); return }
    const sv = surplus.map(r => parseMoney(r.surplusValue))
    if (sv.some(v => v == null)) { setError(new Error('Hàng thừa phải nhập giá trị nhập.')); return }
    const base = { warehouseId, toWarehouseId: null, documentDate: date, postingDate: date }
    const made: string[] = []
    if (surplus.length) made.push((await post<StockDoc>('/stock-documents', { ...base, type: 'Receipt', description: `Kiểm kê kho ngày ${date}: hàng thừa`, partnerId: null,
      lines: surplus.map((r, i) => ({ itemId: r.itemId, quantity: diff(r), amount: sv[i], counterAccountId: surplusAcc, description: null, orderId: null, orderLineNo: null })) })).number)
    if (shortage.length) made.push((await post<StockDoc>('/stock-documents', { ...base, type: 'Issue', description: `Kiểm kê kho ngày ${date}: hàng thiếu`, partnerId: null,
      lines: shortage.map(r => ({ itemId: r.itemId, quantity: diff(r)!.slice(1), amount: null, counterAccountId: shortAcc, description: null, orderId: null, orderLineNo: null })) })).number)
    setInfo(made.length ? `Đã lập phiếu Nháp: ${made.join(', ')}. Ghi sổ ở tab Phiếu kho (người khác ghi sổ).` : 'Không có chênh lệch, không cần lập phiếu.')
    setRows(null)   // lấy lại tồn sổ sách trước khi kiểm kê tiếp: bấm lần nữa sẽ không lập trùng phiếu
  })
  const wname = warehouses.find(w => w.id === warehouseId)
  return (
    <>
      <div className="toolbar">
        <label>Kho<select value={warehouseId} onChange={e => { setWarehouseId(e.target.value); setRows(null) }}><option value="">— chọn —</option>{warehouses.map(w => <option key={w.id} value={w.id}>{w.code} – {w.name}</option>)}</select></label>
        <label>Ngày kiểm kê<input type="date" value={date} onChange={e => setDate(e.target.value)} /></label>
        <button className="primary" disabled={busy || !warehouseId} onClick={load}>Lấy tồn sổ sách</button>
        <button disabled={!rows} onClick={printSheet}>In biên bản</button>
      </div>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      {rows && <div className="panel">
        <table className="grid">
          <thead><tr><th>Vật tư</th><th>ĐVT</th><th className="num">Tồn sổ sách</th><th className="num">Thực tế</th><th className="num">Chênh lệch</th><th className="num">Giá trị nhập (nếu thừa)</th></tr></thead>
          <tbody>{rows.map((r, i) => { const d = diff(r); return (
            <tr key={r.code}><td>{r.code} – {r.name}</td><td>{r.unit}</td><td className="num">{formatMoney(r.bookQty)}</td>
              <td><input className="num" aria-label={`Thực tế ${r.code}`} value={r.actual} onChange={e => upd(i, { actual: e.target.value })} /></td>
              <td className="num" style={{ color: d && d !== '0' ? 'var(--danger)' : undefined }}>{d == null ? '' : formatMoney(d)}</td>
              <td>{d && d !== '0' && !d.startsWith('-') && <input className="num" aria-label={`Giá trị ${r.code}`} value={r.surplusValue} onChange={e => upd(i, { surplusValue: e.target.value })} />}</td></tr>) })}
            {rows.length === 0 && <tr><td colSpan={6} className="muted">Kho chưa có vật tư phát sinh đến ngày này.</td></tr>}</tbody>
        </table>
        <div className="form-row" style={{ marginTop: 8 }}>
          <label>TK đối ứng hàng thừa<AccountSelect accounts={accounts} value={surplusAcc} onChange={setSurplusAcc} ariaLabel="TK hàng thừa" /></label>
          <label>TK đối ứng hàng thiếu<AccountSelect accounts={accounts} value={shortAcc} onChange={setShortAcc} ariaLabel="TK hàng thiếu" /></label>
          <button className="primary" style={{ alignSelf: 'flex-end' }} disabled={busy || counted.length === 0} onClick={create}>Lập phiếu điều chỉnh ({surplus.length} thừa, {shortage.length} thiếu)</button>
        </div>
        <p className="muted">Chỉ các dòng đã nhập số thực tế mới được xử lý. Phiếu ở trạng thái Nháp; hệ thống không tự chọn tài khoản xử lý chênh lệch — chị chọn theo quyết định của hội đồng kiểm kê.</p>
      </div>}
      {rows && <PrintSheet>
        <h1>BIÊN BẢN KIỂM KÊ KHO</h1>
        <div className="center">Kho: {wname ? `${wname.code} – ${wname.name}` : ''} · Thời điểm kiểm kê: {date.split('-').reverse().join('/')}</div>
        <table>
          <thead><tr><th>STT</th><th>Vật tư</th><th>ĐVT</th><th>Sổ sách</th><th>Thực tế</th><th>Chênh lệch</th></tr></thead>
          <tbody>{rows.map((r, i) => { const d = diff(r); return <tr key={r.code}><td className="center">{i + 1}</td><td>{r.name} ({r.code})</td><td className="center">{r.unit}</td>
            <td className="num">{formatMoney(r.bookQty)}</td><td className="num">{r.actual ? formatMoney(parseMoney(r.actual) ?? r.actual) : ''}</td><td className="num">{d == null ? '' : formatMoney(d)}</td></tr> })}</tbody>
        </table>
        <p>Ý kiến xử lý chênh lệch: ……………………………………………………………………………</p>
        <div className="signs"><div><b>Thủ kho</b><br /><i>(Ký, họ tên)</i></div><div><b>Kế toán kho</b><br /><i>(Ký, họ tên)</i></div><div><b>Đại diện Ban kiểm kê</b><br /><i>(Ký, họ tên)</i></div><div><b>Kế toán trưởng</b><br /><i>(Ký, họ tên)</i></div></div>
        <div className="foot">Mẫu in nội bộ HAK Accounting — chưa đối chiếu với mẫu theo chế độ kế toán [CẦN XÁC MINH].</div>
      </PrintSheet>}
    </>
  )
}

interface SummaryRow { warehouseCode: string; itemCode: string; itemName: string; unit: string; openingQty: string; openingValue: string; inQty: string; inValue: string; outQty: string; outValue: string; closingQty: string; closingValue: string; pendingOutQty: string }

/** Số lượng theo đơn vị quy đổi cố định của vật tư (ví dụ 2 tấn); vật tư không có đơn vị quy đổi cố định thì để trống. */
const altText = (item: Item | undefined, mainQty: string) => {
  if (!item?.altUnit || !item.altFactor) return item?.tracksDryness ? <span className="muted" title="Quy đổi theo độ khô từng thời điểm: xem tab MT / BDMT">BDMT</span> : ''
  const v = toAlt(mainQty, item.altFactor)
  return v == null || v === '0' ? '' : `${formatMoney(v)} ${item.altUnit}`
}

function Summary() {
  const { items } = useStockCatalog()
  const [from, setFrom] = useState(today().slice(0, 8) + '01')
  const [to, setTo] = useState(today())
  const [rows, setRows] = useState<SummaryRow[] | null>(null)
  const [recon, setRecon] = useState<{ accountCode: string; difference: string; pendingIssueQty: string }[]>([])
  const { busy, error, run } = useAction()
  const load = () => run(async () => {
    setRows(await get(`/reports/inventory-summary?from=${from}&to=${to}`))
    setRecon(await get(`/reports/inventory-reconciliation?asOf=${to}`))
  })
  const f = (v: string) => (v === '0' ? '' : formatMoney(v))
  return (
    <>
      <div className="toolbar">
        <label>Từ ngày<input type="date" value={from} onChange={e => setFrom(e.target.value)} /></label>
        <label>Đến ngày<input type="date" value={to} onChange={e => setTo(e.target.value)} /></label>
        <button className="primary" onClick={load} disabled={busy}>Xem</button>
        <ExportButton fileName={'NXT_' + from + '_' + to} sheet="Nhập xuất tồn" disabled={!rows}
          columns={[{ header: 'Kho' }, { header: 'Mã vật tư' }, { header: 'Tên vật tư', width: 30 }, { header: 'ĐVT' }, ...['Tồn đầu SL', 'Tồn đầu GT', 'Nhập SL', 'Nhập GT', 'Xuất SL', 'Xuất GT', 'Tồn cuối SL', 'Tồn cuối GT', 'Chờ tính giá'].map(h => ({ header: h, numeric: true }))]}
          rows={(rows ?? []).map(r => [r.warehouseCode, r.itemCode, r.itemName, r.unit, r.openingQty, r.openingValue, r.inQty, r.inValue, r.outQty, r.outValue, r.closingQty, r.closingValue, r.pendingOutQty])} />
      </div>
      <ErrorBox error={error} />
      {recon.map(r => <div key={r.accountCode} className={`msg ${r.difference === '0' ? 'ok' : 'error'}`}>
        TK {r.accountCode}: {r.difference === '0' ? 'sổ kho khớp sổ cái' : `sổ kho lệch sổ cái ${formatMoney(r.difference)}`}{r.pendingIssueQty !== '0' ? ` · ${formatMoney(r.pendingIssueQty)} đơn vị xuất chờ tính giá cuối tháng` : ''}</div>)}
      {rows && <div className="scroll"><table className="grid">
        <thead><tr><th>Kho</th><th>Vật tư</th><th>ĐVT</th><th className="num">Tồn đầu SL</th><th className="num">Tồn đầu GT</th><th className="num">Nhập SL</th><th className="num">Nhập GT</th><th className="num">Xuất SL</th><th className="num">Xuất GT</th><th className="num">Tồn cuối SL</th><th className="num" title="Tồn cuối theo đơn vị quy đổi cố định của vật tư (1 đơn vị quy đổi = hệ số × đơn vị chính)">Tồn quy đổi</th><th className="num">Tồn cuối GT</th><th className="num">Chờ tính giá</th></tr></thead>
        <tbody>{rows.map(r => <tr key={r.warehouseCode + r.itemCode}><td>{r.warehouseCode}</td><td>{r.itemCode} – {r.itemName}</td><td>{r.unit}</td>
          {[r.openingQty, r.openingValue, r.inQty, r.inValue, r.outQty, r.outValue, r.closingQty].map((v, i) => <td key={i} className="num">{f(v)}</td>)}
          <td className="num">{altText(items.find(i => i.code === r.itemCode), r.closingQty)}</td>
          {[r.closingValue, r.pendingOutQty].map((v, i) => <td key={i} className="num">{f(v)}</td>)}</tr>)}</tbody>
      </table></div>}
    </>
  )
}

interface CardRow { date: string; documentNumber: string; description: string | null; inQty: string; outQty: string; value: string | null; balanceQty: string; balanceValue: string | null }

function StockCard() {
  const { warehouses, items } = useStockCatalog()
  const [warehouseId, setWarehouseId] = useState('')
  const [itemId, setItemId] = useState('')
  const [from, setFrom] = useState(today().slice(0, 8) + '01')
  const [to, setTo] = useState(today())
  const [rows, setRows] = useState<CardRow[] | null>(null)
  const selItem = items.find(i => i.id === itemId)
  const { busy, error, run } = useAction()
  const load = () => run(async () => setRows(await get(`/reports/stock-card?warehouseId=${warehouseId}&itemId=${itemId}&from=${from}&to=${to}`)))
  return (
    <>
      <div className="toolbar">
        <label>Kho<select value={warehouseId} onChange={e => setWarehouseId(e.target.value)}><option value="">— chọn —</option>{warehouses.map(w => <option key={w.id} value={w.id}>{w.code}</option>)}</select></label>
        <label>Vật tư<select value={itemId} onChange={e => setItemId(e.target.value)}><option value="">— chọn —</option>{items.map(i => <option key={i.id} value={i.id}>{i.code} – {i.name}</option>)}</select></label>
        <label>Từ ngày<input type="date" value={from} onChange={e => setFrom(e.target.value)} /></label>
        <label>Đến ngày<input type="date" value={to} onChange={e => setTo(e.target.value)} /></label>
        <button className="primary" onClick={load} disabled={busy || !warehouseId || !itemId}>Xem</button>
      </div>
      <ErrorBox error={error} />
      {rows && <table className="grid">
        <thead><tr><th>Ngày</th><th>Số phiếu</th><th>Diễn giải</th><th className="num">Nhập</th><th className="num">Xuất</th><th className="num">Giá trị</th><th className="num">Tồn SL</th>{selItem?.altUnit && <th className="num">Tồn quy đổi ({selItem.altUnit})</th>}<th className="num">Tồn GT</th></tr></thead>
        <tbody>{rows.map((r, i) => <tr key={i}><td>{formatDate(r.date)}</td><td>{r.documentNumber}</td><td>{r.description}</td>
          <td className="num">{r.inQty === '0' ? '' : formatMoney(r.inQty)}</td><td className="num">{r.outQty === '0' ? '' : formatMoney(r.outQty)}</td>
          <td className="num">{r.value == null ? <span className="muted">chờ tính giá</span> : formatMoney(r.value)}</td>
          <td className="num">{formatMoney(r.balanceQty)}</td>{selItem?.altUnit && <td className="num">{altText(selItem, r.balanceQty)}</td>}<td className="num">{r.balanceValue == null ? '—' : formatMoney(r.balanceValue)}</td></tr>)}</tbody>
      </table>}
    </>
  )
}

interface Run { id: string; warehouseId: string; periodStart: string; status: string; resultJson: string }

function Costing() {
  const { warehouses } = useStockCatalog()
  const [periods, setPeriods] = useState<Period[]>([])
  const [runs, setRuns] = useState<Run[]>([])
  const [warehouseId, setWarehouseId] = useState('')
  const [periodId, setPeriodId] = useState('')
  const [info, setInfo] = useState('')
  const { busy, error, run } = useAction()
  const load = useCallback(() => run(async () => { setRuns(await get('/costing-runs')); setPeriods(await get('/periods')) }), [run])
  useEffect(() => { load() }, [load])
  const monthly = warehouses.filter(w => w.policies.some(p => p.method === 'MonthlyAverage'))
  return (
    <>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      <div className="toolbar">
        <label>Kho (bình quân cuối tháng)<select value={warehouseId} onChange={e => setWarehouseId(e.target.value)}><option value="">— chọn —</option>{monthly.map(w => <option key={w.id} value={w.id}>{w.code} – {w.name}</option>)}</select></label>
        <label>Kỳ<select value={periodId} onChange={e => setPeriodId(e.target.value)}><option value="">— chọn —</option>{periods.map(p => <option key={p.id} value={p.id}>{formatMonth(p.startDate)}{p.status === 'Locked' ? ' (đã khóa)' : ''}</option>)}</select></label>
        <button className="primary" disabled={busy || !warehouseId || !periodId} onClick={() => run(async () => { await post('/costing-runs', { warehouseId, periodId }); setInfo('Đã tính giá và ghi sổ giá vốn xuất kho của kỳ.'); await load() })}>Tính giá</button>
      </div>
      <table className="grid">
        <thead><tr><th>Kỳ</th><th>Kho</th><th>Trạng thái</th><th>Kết quả</th><th /></tr></thead>
        <tbody>{runs.map(r => (
          <tr key={r.id}><td>{formatMonth(r.periodStart)}</td><td>{warehouses.find(w => w.id === r.warehouseId)?.code}</td><td>{r.status === 'Active' ? 'Hiệu lực' : 'Đã hủy'}</td>
            <td style={{ whiteSpace: 'normal' }}>{(JSON.parse(r.resultJson || '[]') as { item: string; issuedQty: number | string; issuedValue: number | string }[])
              .map(x => `${x.item}: xuất ${formatMoney(String(x.issuedQty))} = ${formatMoney(String(x.issuedValue))}`).join('; ')}</td>
            <td>{r.status === 'Active' && <button className="danger" onClick={() => { const reason = prompt('Lý do hủy lần tính giá:'); if (reason) run(async () => { await post(`/costing-runs/${r.id}/reverse`, { reason }); await load() }) }}>Hủy</button>}</td></tr>))}
          {runs.length === 0 && <tr><td colSpan={5} className="muted">Chưa có lần tính giá nào.</td></tr>}
        </tbody>
      </table>
    </>
  )
}

interface Pending { documentId: string; documentNumber: string; type: string; postingDate: string; lineNo: number; itemCode: string; quantity: string; expectedKind: string }
interface Measurement { id: string; documentNumber: string; lineNo: number; kind: string; measuredMt: string; lineMt: string; drynessPercent: string; bdmt: string; methodCode: string; evidenceType: string; evidenceNo: string; evidenceIssuer: string | null; evidenceDate: string; supersededAt: string | null; supersedeReason: string | null }
interface DrySummary { warehouseCode: string; itemCode: string; openingMt: string; openingBdmt: string; inMt: string; inBdmt: string; outMt: string; outBdmt: string; closingMt: string; closingBdmt: string; unmeasuredInMt: string; unmeasuredOutMt: string; estimatedInBdmt: string; estimatedOutBdmt: string; estimatedClosingBdmt: string; closingDrynessPercent: string | null }

const KIND_VI: Record<string, string> = { Receipt: 'Đo khi nhập', Shipment: 'Đo ở tàu (xuất)', Other: 'Khác' }

/** BDMT = MT × độ khô% / 100 (phương pháp đã được doanh nghiệp phê duyệt). Đo khi nhập và đo ở tàu khi xuất (chứng thư). */
interface DailyRate { id: string; itemId: string; itemCode: string; date: string; drynessPercent: string; note: string | null }

/** Độ khô công bố theo ngày: đơn vị tính phụ BDMT dùng như tỷ giá (ngày không có số thì lấy ngày gần nhất trước đó). */
function DailyRates() {
  const { items } = useStockCatalog()
  const tracked = items.filter(i => i.tracksDryness)
  const [rows, setRows] = useState<DailyRate[]>([])
  const [itemId, setItemId] = useState('')
  const [date, setDate] = useState(today())
  const [pct, setPct] = useState('')
  const [note, setNote] = useState('')
  const { busy, error, setError, run } = useAction()
  const load = useCallback(() => run(async () => setRows(await get('/dryness-rates'))), [run])
  useEffect(() => { load() }, [load])
  const save = () => run(async () => {
    const v = parseMoney(pct)
    if (!v || !(itemId || tracked[0])) { setError(new Error('Chọn vật tư và nhập độ khô % (dùng dấu phẩy, ví dụ 52,35).')); return }
    await post('/dryness-rates', { itemId: itemId || tracked[0].id, date, drynessPercent: v, note: note || null })
    setPct(''); setNote(''); await load()
  })
  const del = (r: DailyRate) => run(async () => { await api('DELETE', `/dryness-rates/${r.id}`); await load() })
  return (
    <div className="panel">
      <strong>Độ khô theo ngày (đơn vị phụ BDMT, như tỷ giá)</strong>
      <p className="muted">Nhập độ khô công bố hằng ngày. Dòng nhập chưa có kết quả đo được tạm tính BDMT theo độ khô của ngày nhập (ngày không có số lấy ngày gần nhất trước đó). Kết quả đo / chứng thư luôn được ưu tiên. Không ghi sổ cái.</p>
      <ErrorBox error={error} />
      <div className="toolbar">
        <label>Vật tư<select value={itemId || tracked[0]?.id || ''} onChange={e => setItemId(e.target.value)}>{tracked.map(i => <option key={i.id} value={i.id}>{i.code} – {i.name}</option>)}</select></label>
        <label>Ngày<input type="date" value={date} onChange={e => setDate(e.target.value)} /></label>
        <label>Độ khô %<input className="num" size={7} value={pct} onChange={e => setPct(e.target.value)} /></label>
        <label>Ghi chú<input value={note} onChange={e => setNote(e.target.value)} /></label>
        <button className="primary" disabled={busy || tracked.length === 0} onClick={save}>Ghi độ khô</button>
      </div>
      <table className="grid">
        <thead><tr><th>Ngày</th><th>Vật tư</th><th className="num">Độ khô %</th><th>Ghi chú</th><th /></tr></thead>
        <tbody>{rows.slice(0, 60).map(r => <tr key={r.id}><td>{formatDate(r.date)}</td><td>{r.itemCode}</td><td className="num">{formatMoney(r.drynessPercent)}</td><td>{r.note}</td><td><button disabled={busy} onClick={() => del(r)}>Xóa</button></td></tr>)}
          {rows.length === 0 && <tr><td colSpan={5} className="muted">Chưa có độ khô theo ngày.</td></tr>}</tbody>
      </table>
    </div>
  )
}

function Bdmt() {
  const [from, setFrom] = useState(today().slice(0, 8) + '01')
  const [to, setTo] = useState(today())
  const [rows, setRows] = useState<DrySummary[]>([])
  const [pending, setPending] = useState<Pending[]>([])
  const [history, setHistory] = useState<Measurement[]>([])
  const [form, setForm] = useState<Record<string, { dryness: string; evidenceNo: string; issuer: string; date: string; mt: string }>>({})
  const [info, setInfo] = useState('')
  const { busy, error, setError, run } = useAction()
  const load = useCallback(() => run(async () => {
    setRows(await get(`/reports/dryness-summary?from=${from}&to=${to}`))
    setPending(await get('/measurements/pending'))
    setHistory(await get('/measurements'))
  }), [run, from, to])
  useEffect(() => { load() }, [load])
  const key = (p: Pending) => `${p.documentId}:${p.lineNo}`
  const f = (p: Pending) => form[key(p)] ?? { dryness: '', evidenceNo: '', issuer: '', date: p.postingDate, mt: '' }
  const setF = (p: Pending, patch: Partial<ReturnType<typeof f>>) => setForm(s => ({ ...s, [key(p)]: { ...f(p), ...patch } }))

  const save = (p: Pending) => run(async () => {
    const v = f(p)
    const dryness = parseMoney(v.dryness)
    const mt = v.mt ? parseMoney(v.mt) : null
    if (!dryness || (v.mt && !mt)) { setError(new Error('Độ khô / MT không hợp lệ (dùng dấu phẩy cho phần thập phân, ví dụ 52,35).')); return }
    const r = await post<{ bdmt: string }>('/measurements', {
      documentId: p.documentId, lineNo: p.lineNo, drynessPercent: dryness, evidenceType: p.expectedKind === 'Shipment' ? 'Certificate' : 'InternalReport',
      evidenceNo: v.evidenceNo, evidenceDate: v.date, evidenceIssuer: v.issuer || null, measuredMt: mt, note: null,
    })
    setInfo(`${p.documentNumber} dòng ${p.lineNo}: ${formatMoney(r.bdmt)} BDMT.`)
    await load()
  })

  const supersede = (m: Measurement) => {
    const dryness = parseMoney(prompt('Độ khô (%) mới:', m.drynessPercent.replace('.', ',')) ?? ''); if (!dryness) return
    const evidenceNo = prompt('Số chứng thư / phiếu đo mới:', m.evidenceNo); if (!evidenceNo) return
    const reason = prompt('Lý do thay thế (bắt buộc):'); if (!reason) return
    const doc = pending.find(p => p.documentNumber === m.documentNumber)
    run(async () => {
      const docs = await get<{ id: string; number: string }[]>('/stock-documents')
      const documentId = doc?.documentId ?? docs.find(d => d.number === m.documentNumber)!.id
      await post(`/measurements/${m.id}/supersede`, {
        reason, measurement: { documentId, lineNo: m.lineNo, drynessPercent: dryness, evidenceType: m.evidenceType, evidenceNo, evidenceDate: m.evidenceDate, evidenceIssuer: m.evidenceIssuer, measuredMt: m.measuredMt, note: null },
      })
      setInfo(`Đã thay thế kết quả đo ${m.documentNumber} dòng ${m.lineNo}.`)
      await load()
    })
  }

  const n = (v: string) => (v === '0' ? '' : formatMoney(v))
  return (
    <>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      <div className="toolbar">
        <label>Từ ngày<input type="date" value={from} onChange={e => setFrom(e.target.value)} /></label>
        <label>Đến ngày<input type="date" value={to} onChange={e => setTo(e.target.value)} /></label>
        <button onClick={() => load()} disabled={busy}>Xem</button>
        <span className="muted">BDMT = MT × độ khô % ÷ 100 · không tự hạch toán chênh lệch BDMT</span>
      </div>
      <div className="panel">
        <strong>Tồn MT / BDMT</strong>
        <table className="grid">
          <thead><tr><th>Kho</th><th>Vật tư</th><th className="num">Đầu MT</th><th className="num">Đầu BDMT</th><th className="num">Nhập MT</th><th className="num">Nhập BDMT</th><th className="num">Xuất MT</th><th className="num">Xuất BDMT</th><th className="num">Cuối MT</th><th className="num">Cuối BDMT (đã đo)</th><th className="num" title="Tồn BDMT gồm cả phần xuất, chuyển kho chưa đo được tính tạm theo độ khô bình quân của kho lúc xuất">Cuối BDMT (kể cả tạm tính)</th><th className="num">Độ khô bình quân tồn %</th><th className="num">MT chưa có kết quả đo</th></tr></thead>
          <tbody>{rows.map(r => <tr key={r.warehouseCode + r.itemCode}><td>{r.warehouseCode}</td><td>{r.itemCode}</td>
            {[r.openingMt, r.openingBdmt, r.inMt, r.inBdmt, r.outMt, r.outBdmt, r.closingMt, r.closingBdmt].map((v, i) => <td key={i} className="num">{n(v)}</td>)}
            <td className="num" title={r.estimatedOutBdmt !== '0' ? `Xuất tạm tính ${formatMoney(r.estimatedOutBdmt)} BDMT theo bình quân thời điểm` : undefined}>{n(r.estimatedClosingBdmt)}{r.estimatedOutBdmt !== '0' || r.estimatedInBdmt !== '0' ? ' *' : ''}</td>
            <td className="num">{r.closingDrynessPercent == null ? '' : formatMoney(r.closingDrynessPercent)}</td>
            <td className="num">{n(r.unmeasuredInMt)}{r.unmeasuredOutMt !== '0' ? ` / xuất ${formatMoney(r.unmeasuredOutMt)}` : ''}</td></tr>)}</tbody>
        </table>
        {rows.some(r => r.estimatedOutBdmt !== '0' || r.estimatedInBdmt !== '0') && <p className="muted">* Có phần tạm tính: xuất và chuyển kho chưa có kết quả đo được tính BDMT theo <b>độ khô bình quân thời điểm</b> của kho lúc xuất (BDMT tồn ÷ MT tồn); hàng chuyển kho mang đúng BDMT đó sang kho nhận. Có chứng thư hoặc kết quả đo thì số đo thay số tạm tính. Không ghi sổ cái.</p>}
      </div>
      <div className="panel">
        <strong>Chờ kết quả đo / chứng thư ({pending.length})</strong>
        <table className="grid">
          <thead><tr><th>Ngày</th><th>Phiếu</th><th>Vật tư</th><th className="num">MT trên phiếu</th><th>Loại đo</th><th>Độ khô %</th><th>MT theo chứng thư</th><th>Số chứng thư / phiếu đo</th><th>Đơn vị giám định</th><th>Ngày</th><th /></tr></thead>
          <tbody>{pending.map(p => (
            <tr key={key(p)}><td>{formatDate(p.postingDate)}</td><td>{p.documentNumber}/{p.lineNo}</td><td>{p.itemCode}</td><td className="num">{formatMoney(p.quantity)}</td><td>{KIND_VI[p.expectedKind]}</td>
              <td><input className="num" aria-label="Độ khô %" size={6} value={f(p).dryness} onChange={e => setF(p, { dryness: e.target.value })} /></td>
              <td><input className="num" aria-label="MT theo chứng thư" size={8} placeholder="= phiếu" value={f(p).mt} onChange={e => setF(p, { mt: e.target.value })} /></td>
              <td><input aria-label="Số chứng thư" value={f(p).evidenceNo} onChange={e => setF(p, { evidenceNo: e.target.value })} placeholder={p.expectedKind === 'Shipment' ? 'Chứng thư (bắt buộc)' : 'Phiếu đo'} /></td>
              <td><input aria-label="Đơn vị giám định" value={f(p).issuer} onChange={e => setF(p, { issuer: e.target.value })} /></td>
              <td><input type="date" aria-label="Ngày chứng từ" value={f(p).date} onChange={e => setF(p, { date: e.target.value })} /></td>
              <td><button className="primary" disabled={busy} onClick={() => save(p)}>Ghi nhận</button></td></tr>))}
            {pending.length === 0 && <tr><td colSpan={11} className="muted">Không có dòng chờ kết quả đo.</td></tr>}
          </tbody>
        </table>
      </div>
      <div className="panel">
        <strong>Lịch sử kết quả đo</strong>
        <table className="grid">
          <thead><tr><th>Phiếu</th><th>Loại</th><th className="num">MT</th><th className="num">Độ khô %</th><th className="num">BDMT</th><th>Chứng từ</th><th>Trạng thái</th><th /></tr></thead>
          <tbody>{history.map(m => (
            <tr key={m.id} style={m.supersededAt ? { opacity: .6 } : undefined}><td>{m.documentNumber}/{m.lineNo}</td><td>{KIND_VI[m.kind]}</td>
              <td className="num">{formatMoney(m.measuredMt)}{m.measuredMt !== m.lineMt ? ` (phiếu ${formatMoney(m.lineMt)})` : ''}</td>
              <td className="num">{formatMoney(m.drynessPercent)}</td><td className="num">{formatMoney(m.bdmt)}</td>
              <td>{m.evidenceType === 'Certificate' ? 'Chứng thư' : 'Phiếu đo'} {m.evidenceNo}{m.evidenceIssuer ? ` · ${m.evidenceIssuer}` : ''} · {formatDate(m.evidenceDate)}</td>
              <td>{m.supersededAt ? `Đã thay thế: ${m.supersedeReason}` : 'Hiệu lực'}</td>
              <td>{!m.supersededAt && <button onClick={() => supersede(m)}>Thay thế</button>}</td></tr>))}
          </tbody>
        </table>
      </div>
    </>
  )
}