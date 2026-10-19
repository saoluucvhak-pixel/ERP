import { useCallback, useEffect, useState } from 'react'
import { get, post, put } from './api'
import { ErrorBox, FlowStrip, navStore, useAction } from './common'
import { formatMoney, parseMoney, today } from './money'
import { PeriodCostingView } from './Production'

interface Item { id: string; code: string; name: string; unit: string }
interface Warehouse { id: string; code: string; name: string }
interface CostObject { id: string; code: string; name: string; itemId: string }
interface Bom { id: string; itemId: string; itemCode: string; itemName: string; note: string | null; lines: { rawItemId: string; rawCode: string; rawName: string; unit: string; quantityPerUnit: string; scrapRate: string }[] }
interface WorkCenter { id: string; code: string; name: string; laborRatePerHour: string; overheadRatePerHour: string; isActive: boolean }
type OrderStatus = 'Draft' | 'Released' | 'InProgress' | 'Completed' | 'Closed' | 'Cancelled'
interface Order { id: string; number: string; itemId: string; itemCode: string; itemName: string; unit: string; plannedQuantity: string; actualQuantity: string | null; plannedStart: string; completedDate: string | null; status: OrderStatus; totalCost: string; unitCost: string; receiptDocumentId: string | null; note: string | null }
interface Requirement { rawItemId: string; code: string; name: string; unit: string; required: string; issued: string; remaining: string; onHand: string; shortage: string }
interface Confirmation { id: string; workDate: string; workCenterCode: string; workCenterName: string; laborHours: string; machineHours: string; laborCost: string; overheadCost: string; note: string | null; voided: boolean; voidReason: string | null }
interface Issue { stockDocumentId: string; number: string; status: string; date: string }
interface Usage { rawItemId: string; code: string; name: string; unit: string; standardQuantity: string; issuedQuantity: string; variance: string; cost: string }
interface Sheet { quantity: number | string; quantityIsActual: boolean; materialCost: string; laborCost: string; overheadCost: string; totalCost: string; unitCost: string; materials: Usage[]; laborHours: string; machineHours: string }

const STATUS_VI: Record<OrderStatus, string> = { Draft: 'Đang lập', Released: 'Đã ban hành', InProgress: 'Đang sản xuất', Completed: 'Đã hoàn thành, chờ ghi sổ nhập kho', Closed: 'Đã đóng', Cancelled: 'Đã hủy' }
const DOC_VI: Record<string, string> = { Draft: 'Nháp', Posted: 'Đã ghi sổ', Reversed: 'Đã đảo', Cancelled: 'Đã hủy' }
const STEPS = [{ key: 'Draft', label: 'Lập' }, { key: 'Released', label: 'Ban hành' }, { key: 'InProgress', label: 'Sản xuất' }, { key: 'Completed', label: 'Hoàn thành' }, { key: 'Closed', label: 'Đóng' }]

/** Chu trình sản xuất (tài liệu thiết kế chu trình sản xuất): định mức → lệnh sản xuất → xuất nguyên vật liệu → báo công → nhập thành phẩm và giá thành. */
export function ProductionView() {
  const [tab, setTab] = useState<'orders' | 'master' | 'period'>('orders')
  return (
    <>
      <div className="toolbar">
        <h2>Sản xuất</h2>
        <button className={tab === 'orders' ? 'primary' : ''} onClick={() => setTab('orders')}>Lệnh sản xuất</button>
        <button className={tab === 'master' ? 'primary' : ''} onClick={() => setTab('master')}>Định mức &amp; tổ xưởng</button>
        <button className={tab === 'period' ? 'primary' : ''} onClick={() => setTab('period')}>Giá thành cuối kỳ</button>
      </div>
      {tab === 'orders' && <Orders />}
      {tab === 'master' && <Master />}
      {tab === 'period' && <PeriodCostingView />}
    </>
  )
}

function useMaster() {
  const [items, setItems] = useState<Item[]>([])
  const [warehouses, setWarehouses] = useState<Warehouse[]>([])
  const [objects, setObjects] = useState<CostObject[]>([])
  const [boms, setBoms] = useState<Bom[]>([])
  const [centers, setCenters] = useState<WorkCenter[]>([])
  const load = useCallback(async () => {
    setItems(await get<Item[]>('/items')); setWarehouses(await get<Warehouse[]>('/warehouses')); setObjects(await get<CostObject[]>('/production/cost-objects'))
    setBoms(await get<Bom[]>('/production/boms')); setCenters(await get<WorkCenter[]>('/production/work-centers'))
  }, [])
  return { items, warehouses, objects, boms, centers, load }
}

function Orders() {
  const m = useMaster()
  const [orders, setOrders] = useState<Order[]>([])
  const [sel, setSel] = useState<string | null>(null)
  const [form, setForm] = useState({ itemId: '', costObjectId: '', warehouseId: '', qty: '', start: today(), note: '' })
  const [info, setInfo] = useState('')
  const { busy, error, run, setError } = useAction()
  const load = useCallback(() => run(async () => { await m.load(); setOrders(await get<Order[]>('/production/orders')) }), [run, m.load])  // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { load() }, [load])
  const act = (msg: string, fn: () => Promise<unknown>) => run(async () => { await fn(); setInfo(msg); await load() })
  const ask = (q: string) => { const v = prompt(q); return v && v.trim() ? v.trim() : null }
  const order = orders.find(o => o.id === sel) ?? null
  const count = (s: OrderStatus) => orders.filter(o => o.status === s).length
  const fgItems = m.boms.map(b => m.items.find(i => i.id === b.itemId)).filter((i): i is Item => !!i)

  return (
    <>
      <FlowStrip steps={[
        { label: 'Lập kế hoạch sản xuất và định mức nguyên vật liệu', journal: false, count: `${m.boms.length} thành phẩm có định mức` },
        { label: 'Ban hành lệnh sản xuất', journal: false, count: `${count('Draft')} lệnh chờ ban hành` },
        { label: 'Xuất nguyên vật liệu (phiếu kho, đối ứng TK dở dang)', journal: true, screen: 'stock', count: `${count('Released') + count('InProgress')} lệnh đang chạy` },
        { label: 'Báo công: giờ nhân công, giờ máy', journal: false },
        { label: 'Nhập thành phẩm và phân bổ chi phí chế biến → giá thành', journal: true, count: `${count('Completed')} lệnh chờ ghi sổ nhập kho` },
        { label: 'Đóng lệnh', journal: false },
      ]} />
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}

      <section className="panel">
        <h3>Lập lệnh sản xuất</h3>
        <div className="form-row">
          <label>Thành phẩm (đã có định mức)<select value={form.itemId} onChange={e => setForm({ ...form, itemId: e.target.value, costObjectId: m.objects.find(o => o.itemId === e.target.value)?.id ?? '' })}>
            <option value="">— chọn —</option>{fgItems.map(i => <option key={i.id} value={i.id}>{i.code} – {i.name}</option>)}</select></label>
          <label>Đối tượng tính giá thành<select value={form.costObjectId} onChange={e => setForm({ ...form, costObjectId: e.target.value })}>
            <option value="">— chọn —</option>{m.objects.filter(o => !form.itemId || o.itemId === form.itemId).map(o => <option key={o.id} value={o.id}>{o.code} – {o.name}</option>)}</select></label>
          <label>Kho<select value={form.warehouseId} onChange={e => setForm({ ...form, warehouseId: e.target.value })}>
            <option value="">— chọn —</option>{m.warehouses.map(w => <option key={w.id} value={w.id}>{w.code} – {w.name}</option>)}</select></label>
          <label>Số lượng kế hoạch<input inputMode="decimal" value={form.qty} onChange={e => setForm({ ...form, qty: e.target.value })} /></label>
          <label>Ngày bắt đầu<input type="date" value={form.start} onChange={e => setForm({ ...form, start: e.target.value })} /></label>
          <label>Ghi chú<input value={form.note} onChange={e => setForm({ ...form, note: e.target.value })} /></label>
          <button className="primary" disabled={busy || !form.itemId || !form.costObjectId || !form.warehouseId} onClick={() => {
            const qty = parseMoney(form.qty)
            if (!qty) { setError(new Error('Số lượng kế hoạch không hợp lệ.')); return }
            act('Đã lập lệnh sản xuất; người khác ban hành.', async () => { const o = await post<{ id: string }>('/production/orders', { itemId: form.itemId, costObjectId: form.costObjectId, warehouseId: form.warehouseId, plannedQuantity: qty, plannedStart: form.start, note: form.note || null }); setSel(o.id); setForm({ ...form, qty: '', note: '' }) })
          }}>Lập lệnh</button>
        </div>
      </section>

      <table className="grid">
        <thead><tr><th>Số lệnh</th><th>Thành phẩm</th><th className="num">Kế hoạch</th><th className="num">Thực nhập</th><th>Bắt đầu</th><th>Trạng thái</th><th className="num">Giá thành</th><th className="num">Giá thành đơn vị</th></tr></thead>
        <tbody>{orders.map(o => <tr key={o.id} className={o.id === sel ? 'selected' : ''} style={{ cursor: 'pointer' }} onClick={() => setSel(o.id)}>
          <td>{o.number}</td><td>{o.itemCode} – {o.itemName}</td><td className="num">{formatMoney(o.plannedQuantity)} {o.unit}</td><td className="num">{o.actualQuantity ? `${formatMoney(o.actualQuantity)} ${o.unit}` : ''}</td>
          <td>{o.plannedStart}</td><td>{STATUS_VI[o.status]}</td><td className="num">{o.status === 'Completed' || o.status === 'Closed' ? formatMoney(o.totalCost) : ''}</td><td className="num">{o.status === 'Completed' || o.status === 'Closed' ? formatMoney(o.unitCost) : ''}</td></tr>)}
          {orders.length === 0 && <tr><td colSpan={8} className="muted">Chưa có lệnh sản xuất. Lập định mức ở tab &quot;Định mức &amp; tổ xưởng&quot; trước.</td></tr>}</tbody>
      </table>

      {order && <OrderDetail key={order.id} order={order} centers={m.centers} busy={busy} act={act} ask={ask} onError={setError} />}
    </>
  )
}

function OrderDetail({ order: o, centers, busy, act, ask, onError }: { order: Order; centers: WorkCenter[]; busy: boolean; act: (msg: string, fn: () => Promise<unknown>) => void; ask: (q: string) => string | null; onError: (e: unknown) => void }) {
  const [reqs, setReqs] = useState<Requirement[]>([])
  const [confs, setConfs] = useState<Confirmation[]>([])
  const [issues, setIssues] = useState<Issue[]>([])
  const [sheet, setSheet] = useState<Sheet | null>(null)
  const [conf, setConf] = useState({ centerId: '', date: today(), labor: '', machine: '', note: '' })
  const [actual, setActual] = useState('')
  useEffect(() => {
    let stop = false
    Promise.all([get<Requirement[]>(`/production/orders/${o.id}/requirements`), get<Confirmation[]>(`/production/orders/${o.id}/confirmations`), get<Issue[]>(`/production/orders/${o.id}/issues`), get<Sheet>(`/production/orders/${o.id}/cost-sheet`)])
      .then(([r, c, i, s]) => { if (!stop) { setReqs(r); setConfs(c); setIssues(i); setSheet(s) } }).catch(onError)
    return () => { stop = true }
  }, [o.id, o.status, o.totalCost, busy, onError])
  const running = o.status === 'Released' || o.status === 'InProgress'
  return (
    <section className="panel">
      <div className="toolbar"><strong>{o.number}</strong><span className="muted">{o.itemCode} – {o.itemName} · kế hoạch {formatMoney(o.plannedQuantity)} {o.unit}</span></div>
      <div className="statusbar" role="group" aria-label="Trạng thái lệnh">
        {STEPS.map((s, i) => { const idx = STEPS.findIndex(x => x.key === o.status); return <span key={s.key} className={`step${o.status === 'Cancelled' ? '' : i < idx ? ' done' : i === idx ? ' current' : ''}`}>{s.label}</span> })}
        {o.status === 'Cancelled' && <span className="step stopped">Đã hủy</span>}
        <span className="actions">
          {o.status === 'Draft' && <button className="primary" disabled={busy} onClick={() => act(`Đã ban hành ${o.number}.`, () => post(`/production/orders/${o.id}/release`))}>Ban hành</button>}
          {(o.status === 'Draft' || o.status === 'Released') && <button className="danger" disabled={busy} onClick={() => { const reason = ask('Lý do hủy lệnh:'); if (reason) act(`Đã hủy ${o.number}.`, () => post(`/production/orders/${o.id}/cancel`, { reason })) }}>Hủy</button>}
          {o.status === 'Completed' && <button className="primary" disabled={busy} onClick={() => act(`Đã đóng ${o.number}.`, () => post(`/production/orders/${o.id}/close`))}>Đóng lệnh</button>}
        </span>
      </div>

      <h4>Nhu cầu nguyên vật liệu theo định mức</h4>
      <table className="grid"><thead><tr><th>Nguyên vật liệu</th><th className="num">Cần theo định mức</th><th className="num">Đã xuất</th><th className="num">Còn phải xuất</th><th className="num">Tồn kho</th><th className="num">Thiếu</th></tr></thead>
        <tbody>{reqs.map(r => <tr key={r.rawItemId}><td>{r.code} – {r.name}</td><td className="num">{formatMoney(r.required)} {r.unit}</td><td className="num">{formatMoney(r.issued)}</td><td className="num">{formatMoney(r.remaining)}</td><td className="num">{formatMoney(r.onHand)}</td>
          <td className="num" style={{ color: r.shortage !== '0' ? 'var(--danger)' : undefined }}>{r.shortage !== '0' ? formatMoney(r.shortage) : ''}</td></tr>)}</tbody></table>

      <h4>Phiếu xuất nguyên vật liệu</h4>
      <table className="grid"><thead><tr><th>Phiếu kho</th><th>Ngày</th><th>Trạng thái</th></tr></thead>
        <tbody>{issues.map(i => <tr key={i.stockDocumentId}><td><a href="#kho" onClick={e => { e.preventDefault(); navStore.go?.('stock') }}>{i.number}</a></td><td>{i.date}</td><td>{DOC_VI[i.status] ?? i.status}{i.status === 'Draft' ? ' — ghi sổ ở màn Kho' : ''}</td></tr>)}
          {issues.length === 0 && <tr><td colSpan={3} className="muted">Chưa xuất nguyên vật liệu.</td></tr>}</tbody></table>
      {running && <div className="toolbar"><button disabled={busy || reqs.every(r => r.remaining === '0')} onClick={() => act('Đã lập phiếu xuất nguyên vật liệu nháp theo phần còn phải xuất; ghi sổ ở màn Kho.', () => post(`/production/orders/${o.id}/issues`, { date: today(), lines: null }))}>Lập phiếu xuất nguyên vật liệu theo định mức</button></div>}

      <h4>Báo công</h4>
      <table className="grid"><thead><tr><th>Ngày</th><th>Tổ xưởng</th><th className="num">Giờ công</th><th className="num">Giờ máy</th><th className="num">CP nhân công</th><th className="num">CP máy, SXC</th><th>Ghi chú</th><th /></tr></thead>
        <tbody>{confs.map(c => <tr key={c.id} style={{ opacity: c.voided ? 0.5 : 1, textDecoration: c.voided ? 'line-through' : undefined }}><td>{c.workDate}</td><td>{c.workCenterCode} – {c.workCenterName}</td><td className="num">{c.laborHours}</td><td className="num">{c.machineHours}</td>
          <td className="num">{formatMoney(c.laborCost)}</td><td className="num">{formatMoney(c.overheadCost)}</td><td>{c.voided ? `Đã hủy: ${c.voidReason}` : c.note ?? ''}</td>
          <td>{running && !c.voided && <button disabled={busy} onClick={() => { const reason = ask('Lý do hủy báo công:'); if (reason) act('Đã hủy báo công.', () => post(`/production/confirmations/${c.id}/void`, { reason })) }}>Hủy</button>}</td></tr>)}
          {confs.length === 0 && <tr><td colSpan={8} className="muted">Chưa có báo công.</td></tr>}</tbody></table>
      {running && <div className="form-row">
        <label>Tổ xưởng<select value={conf.centerId} onChange={e => setConf({ ...conf, centerId: e.target.value })}><option value="">— chọn —</option>{centers.filter(c => c.isActive).map(c => <option key={c.id} value={c.id}>{c.code} – {c.name}</option>)}</select></label>
        <label>Ngày<input type="date" value={conf.date} onChange={e => setConf({ ...conf, date: e.target.value })} /></label>
        <label>Giờ nhân công<input inputMode="decimal" value={conf.labor} onChange={e => setConf({ ...conf, labor: e.target.value })} /></label>
        <label>Giờ máy<input inputMode="decimal" value={conf.machine} onChange={e => setConf({ ...conf, machine: e.target.value })} /></label>
        <label>Ghi chú<input value={conf.note} onChange={e => setConf({ ...conf, note: e.target.value })} /></label>
        <button disabled={busy || !conf.centerId || (!conf.labor && !conf.machine)} onClick={() => {
          const labor = parseMoney(conf.labor || '0'); const machine = parseMoney(conf.machine || '0')
          if (labor == null || machine == null) { onError(new Error('Số giờ không hợp lệ.')); return }
          act('Đã ghi nhận báo công.', async () => { await post(`/production/orders/${o.id}/confirmations`, { workCenterId: conf.centerId, date: conf.date, laborHours: labor, machineHours: machine, note: conf.note || null }); setConf({ ...conf, labor: '', machine: '', note: '' }) })
        }}>Ghi nhận báo công</button></div>}

      {sheet && <>
        <h4>Giá thành {sheet.quantityIsActual ? '(đã chốt)' : '(tạm tính theo số lượng kế hoạch)'}</h4>
        <table className="grid"><thead><tr><th>Khoản mục</th><th className="num">Số tiền</th></tr></thead>
          <tbody>
            <tr><td>Nguyên vật liệu trực tiếp</td><td className="num">{formatMoney(sheet.materialCost)}</td></tr>
            <tr><td>Nhân công trực tiếp ({sheet.laborHours} giờ)</td><td className="num">{formatMoney(sheet.laborCost)}</td></tr>
            <tr><td>Máy và sản xuất chung ({sheet.machineHours} giờ máy)</td><td className="num">{formatMoney(sheet.overheadCost)}</td></tr>
          </tbody>
          <tfoot><tr><td><b>Tổng chi phí dồn vào dở dang</b></td><td className="num"><b>{formatMoney(sheet.totalCost)}</b></td></tr>
            <tr><td><b>Giá thành đơn vị</b> (÷ {formatMoney(String(sheet.quantity))} {o.unit})</td><td className="num"><b>{formatMoney(sheet.unitCost)}</b></td></tr></tfoot></table>
        {sheet.materials.some(u => u.issuedQuantity !== '0' || u.standardQuantity !== '0') && <table className="grid"><thead><tr><th>Nguyên vật liệu</th><th className="num">Theo định mức</th><th className="num">Thực xuất</th><th className="num">Chênh lệch</th><th className="num">Giá trị xuất</th></tr></thead>
          <tbody>{sheet.materials.map(u => <tr key={u.rawItemId}><td>{u.code} – {u.name}</td><td className="num">{formatMoney(u.standardQuantity)}</td><td className="num">{formatMoney(u.issuedQuantity)}</td>
            <td className="num" style={{ color: u.variance.startsWith('-') || u.variance === '0' ? undefined : 'var(--danger)' }}>{formatMoney(u.variance)}</td><td className="num">{formatMoney(u.cost)}</td></tr>)}</tbody></table>}
      </>}

      {o.status === 'InProgress' && <div className="form-row">
        <label>Số lượng thành phẩm thực nhập ({o.unit})<input inputMode="decimal" value={actual} onChange={e => setActual(e.target.value)} /></label>
        <button className="primary" disabled={busy || !actual} title="Ghi Nhật ký phân bổ chi phí chế biến và lập phiếu nhập thành phẩm Nháp" onClick={() => {
          const q = parseMoney(actual)
          if (!q) { onError(new Error('Số lượng thực nhập không hợp lệ.')); return }
          act(`Đã hoàn thành ${o.number}; phiếu nhập thành phẩm nháp chờ người khác ghi sổ ở màn Kho.`, () => post(`/production/orders/${o.id}/complete`, { actualQuantity: q, date: today() }))
        }}>Hoàn thành lệnh</button>
        <span className="muted">Cần mọi phiếu xuất nguyên vật liệu đã ghi sổ và đã có giá xuất.</span></div>}
    </section>
  )
}

function Master() {
  const m = useMaster()
  const [bomItem, setBomItem] = useState('')
  const [lines, setLines] = useState<{ rawItemId: string; qty: string; scrap: string }[]>([{ rawItemId: '', qty: '', scrap: '0' }])
  const [bomNote, setBomNote] = useState('')
  const [wc, setWc] = useState({ code: '', name: '', labor: '', overhead: '' })
  const [info, setInfo] = useState('')
  const { busy, error, run, setError } = useAction()
  const load = useCallback(() => run(async () => { await m.load() }), [run, m.load])  // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { load() }, [load])
  const act = (msg: string, fn: () => Promise<unknown>) => run(async () => { await fn(); setInfo(msg); await load() })
  const edit = (b: Bom) => { setBomItem(b.itemId); setBomNote(b.note ?? ''); setLines(b.lines.map(l => ({ rawItemId: l.rawItemId, qty: l.quantityPerUnit, scrap: l.scrapRate }))) }
  return (
    <>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      <section className="panel">
        <h3>Định mức nguyên vật liệu <span className="muted">· số lượng nguyên vật liệu cho 1 đơn vị thành phẩm và tỷ lệ hao hụt cho phép; mỗi thành phẩm một định mức hiện hành, sửa được và có nhật ký kiểm toán</span></h3>
        <div className="form-row">
          <label>Thành phẩm<select value={bomItem} onChange={e => setBomItem(e.target.value)}><option value="">— chọn —</option>{m.items.map(i => <option key={i.id} value={i.id}>{i.code} – {i.name}</option>)}</select></label>
          <label>Ghi chú<input value={bomNote} onChange={e => setBomNote(e.target.value)} /></label>
        </div>
        {lines.map((l, i) => <div className="form-row" key={i}>
          <label>Nguyên vật liệu<select value={l.rawItemId} onChange={e => setLines(lines.map((x, j) => j === i ? { ...x, rawItemId: e.target.value } : x))}><option value="">— chọn —</option>{m.items.filter(it => it.id !== bomItem).map(it => <option key={it.id} value={it.id}>{it.code} – {it.name}</option>)}</select></label>
          <label>Số lượng / 1 đơn vị TP<input inputMode="decimal" value={l.qty} onChange={e => setLines(lines.map((x, j) => j === i ? { ...x, qty: e.target.value } : x))} /></label>
          <label>Hao hụt cho phép (%)<input inputMode="decimal" value={l.scrap} onChange={e => setLines(lines.map((x, j) => j === i ? { ...x, scrap: e.target.value } : x))} /></label>
          {lines.length > 1 && <button onClick={() => setLines(lines.filter((_, j) => j !== i))}>Bỏ dòng</button>}
        </div>)}
        <div className="toolbar">
          <button onClick={() => setLines([...lines, { rawItemId: '', qty: '', scrap: '0' }])}>Thêm nguyên vật liệu</button>
          <button className="primary" disabled={busy || !bomItem || lines.some(l => !l.rawItemId || !l.qty)} onClick={() => {
            const parsed = lines.map(l => ({ rawItemId: l.rawItemId, quantityPerUnit: parseMoney(l.qty), scrapRate: parseMoney(l.scrap || '0') }))
            if (parsed.some(l => l.quantityPerUnit == null || l.scrapRate == null)) { setError(new Error('Số lượng hoặc tỷ lệ hao hụt không hợp lệ.')); return }
            act('Đã lưu định mức.', () => put(`/production/boms/${bomItem}`, { lines: parsed, note: bomNote || null }))
          }}>Lưu định mức</button></div>
        <table className="grid"><thead><tr><th>Thành phẩm</th><th>Nguyên vật liệu</th><th className="num">Số lượng / 1 ĐV</th><th className="num">Hao hụt %</th><th /></tr></thead>
          <tbody>{m.boms.flatMap(b => b.lines.map((l, i) => <tr key={b.id + l.rawItemId}><td>{i === 0 ? `${b.itemCode} – ${b.itemName}` : ''}</td><td>{l.rawCode} – {l.rawName}</td><td className="num">{l.quantityPerUnit} {l.unit}</td><td className="num">{l.scrapRate}</td>
            <td>{i === 0 && <button onClick={() => edit(b)}>Sửa</button>}</td></tr>))}
            {m.boms.length === 0 && <tr><td colSpan={5} className="muted">Chưa có định mức.</td></tr>}</tbody></table>
      </section>
      <section className="panel">
        <h3>Tổ xưởng <span className="muted">· đơn giá nhân công và máy + sản xuất chung theo giờ, chốt vào từng báo công</span></h3>
        <div className="form-row">
          <label>Mã<input value={wc.code} onChange={e => setWc({ ...wc, code: e.target.value.toUpperCase() })} /></label>
          <label>Tên<input value={wc.name} onChange={e => setWc({ ...wc, name: e.target.value })} /></label>
          <label>Đơn giá nhân công / giờ<input inputMode="decimal" value={wc.labor} onChange={e => setWc({ ...wc, labor: e.target.value })} /></label>
          <label>Đơn giá máy, SXC / giờ<input inputMode="decimal" value={wc.overhead} onChange={e => setWc({ ...wc, overhead: e.target.value })} /></label>
          <button className="primary" disabled={busy || !wc.code || !wc.name} onClick={() => {
            const l = parseMoney(wc.labor || '0'); const o = parseMoney(wc.overhead || '0')
            if (l == null || o == null) { setError(new Error('Đơn giá không hợp lệ.')); return }
            act('Đã lưu tổ xưởng.', async () => { await post('/production/work-centers', { id: m.centers.find(c => c.code === wc.code)?.id ?? null, code: wc.code, name: wc.name, laborRatePerHour: l, overheadRatePerHour: o, isActive: true }); setWc({ code: '', name: '', labor: '', overhead: '' }) })
          }}>Lưu tổ xưởng</button></div>
        <table className="grid"><thead><tr><th>Mã</th><th>Tên</th><th className="num">Nhân công / giờ</th><th className="num">Máy, SXC / giờ</th><th /></tr></thead>
          <tbody>{m.centers.map(c => <tr key={c.id}><td>{c.code}</td><td>{c.name}</td><td className="num">{formatMoney(c.laborRatePerHour)}</td><td className="num">{formatMoney(c.overheadRatePerHour)}</td>
            <td><button onClick={() => setWc({ code: c.code, name: c.name, labor: c.laborRatePerHour, overhead: c.overheadRatePerHour })}>Sửa</button></td></tr>)}
            {m.centers.length === 0 && <tr><td colSpan={5} className="muted">Chưa có tổ xưởng.</td></tr>}</tbody></table>
      </section>
      <p className="muted">Vai trò tài khoản cần ánh xạ ở Thiết lập để hoàn thành lệnh có báo công: PROD_LABOR_APPLIED (nhân công phân bổ) và PROD_OVERHEAD_APPLIED (máy, sản xuất chung phân bổ). Tài khoản do kế toán trưởng chọn, chưa seed số hiệu pháp lý.</p>
    </>
  )
}
