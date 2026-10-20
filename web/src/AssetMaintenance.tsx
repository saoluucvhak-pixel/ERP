import { PhotosToggle } from './Photos'
import { Fragment, useCallback, useEffect, useState } from 'react'
import { get, post, put, type Account } from './api'
import { AccountSelect, ErrorBox, useAction } from './common'
import { formatDate, formatDateTime, formatMoney, parseMoney, today } from './money'
import { SmartFilter, inRange, useFilterState } from './SmartFilter'
import { matchesQuery, toNumber } from './filter'

type Status = 'Draft' | 'PendingApproval' | 'Approved' | 'InProgress' | 'Testing' | 'Completed' | 'Closed' | 'Rejected' | 'Cancelled'
interface Material { id: string; itemId: string; itemCode: string; itemName: string; unit: string; qtyRequested: string; qtyIssued: string; unitPrice: string; status: 'Pending' | 'Issued' | 'Cancelled'; stockDocumentNumber: string | null }
interface Repair { id: string; number: string; assetId: string; assetCode: string; assetName: string; type: 'Emergency' | 'Preventive'; status: Status; reportedDate: string; description: string; requestNumber: string | null; vendor: string | null; assignedTech: string | null; rootCause: string | null; solutionPlan: string | null; estimatedLaborCost: string; estimatedMaterialCost: string; estimatedTotal: string; actualLaborCost: string | null; actualMaterialCost: string; actualTotal: string; startTime: string | null; endTime: string | null; note: string | null; materials: Material[] }
interface Req { id: string; number: string; assetId: string; assetCode: string; assetName: string; priority: 'Low' | 'Medium' | 'High' | 'Urgent'; description: string; reportedAt: string; status: 'New' | 'Surveying' | 'Proposed' | 'Approved' | 'InProgress' | 'Done' | 'Rejected' | 'Cancelled'; note: string | null; workOrderNumber: string | null }
interface Sched { id: string; assetId: string; assetCode: string; assetName: string; taskName: string; frequencyDays: number; lastPerformedAt: string | null; nextDueDate: string; isActive: boolean; daysToDue: number; overdue: boolean }
interface Kpi { assetId: string; assetCode: string; assetName: string; department: string | null; failures: number; repairHours: string; mttr: string | null; mtbf: string | null; downtimeRate: string; laborCost: string; materialCost: string; totalCost: string; preventiveCount: number }
interface ItemRef { id: string; code: string; name: string; unit: string }
interface Wh { id: string; code: string; name: string }
interface AssetRef { id: string; code: string; name: string; status: string }
interface MatLine { itemId: string; qty: string; price: string }

const STATUS_VI: Record<Status, string> = { Draft: 'Nháp', PendingApproval: 'Chờ duyệt', Approved: 'Đã duyệt', InProgress: 'Đang thi công', Testing: 'Chờ nghiệm thu', Completed: 'Đã nghiệm thu', Closed: 'Đã đóng', Rejected: 'Bị từ chối', Cancelled: 'Đã hủy' }
const REQ_VI = { New: 'Mới', Surveying: 'Đang khảo sát', Proposed: 'Đã đề xuất phương án', Approved: 'Đã duyệt', InProgress: 'Đang sửa', Done: 'Hoàn tất', Rejected: 'Bị từ chối', Cancelled: 'Đã hủy' } as const
const PRIORITY_VI = { Low: 'Thấp', Medium: 'Trung bình', High: 'Cao', Urgent: 'Khẩn cấp' } as const
const ACTIVE: Status[] = ['Draft', 'PendingApproval', 'Approved', 'InProgress', 'Testing', 'Completed']

const monthStart = () => today().slice(0, 8) + '01'

/** Bảo trì &amp; sửa chữa tài sản (tệp phân tích CMMS): YCSC → phương án, dự toán → duyệt → thi công, xuất vật tư → nghiệm thu → đóng; lịch bảo trì; KPI MTBF, MTTR. */
export function MaintenancePanels({ assets, onOpenCount }: { assets: AssetRef[]; onOpenCount: (n: number) => void }) {
  const [reqs, setReqs] = useState<Req[]>([])
  const [photoN, setPhotoN] = useState<Record<string, number>>({})
  const [wos, setWos] = useState<Repair[]>([])
  const [scheds, setScheds] = useState<Sched[]>([])
  const [kpi, setKpi] = useState<Kpi[]>([])
  const [items, setItems] = useState<ItemRef[]>([])
  const [whs, setWhs] = useState<Wh[]>([])
  const [accounts, setAccounts] = useState<Account[]>([])
  const [range, setRange] = useState({ from: monthStart(), to: today() })
  const [rq, setRq] = useState({ assetId: '', priority: 'Medium', description: '' })
  const [plan, setPlan] = useState<{ id: string | null; requestId: string | null; assetId: string; type: 'Emergency' | 'Preventive'; description: string; vendor: string; tech: string; cause: string; solution: string; labor: string; mats: MatLine[] } | null>(null)
  const [issue, setIssue] = useState<{ id: string; number: string; warehouseId: string; accountId: string } | null>(null)
  const [test, setTest] = useState<{ id: string; number: string; labor: string; note: string } | null>(null)
  const [sf, setSf] = useState({ assetId: '', task: '', days: '30', due: today() })
  const [open, setOpen] = useState<string | null>(null)
  const [fr, pfr, cfr] = useFilterState('maint-requests')
  const [fw, pfw, cfw] = useFilterState('maint-orders')
  const [info, setInfo] = useState('')
  const { busy, error, run, setError } = useAction()

  const load = useCallback(() => run(async () => {
    const w = await get<Repair[]>('/assets/repairs')
    setWos(w); onOpenCount(w.filter(x => ACTIVE.includes(x.status) && x.status !== 'Completed').length)
    setReqs(await get<Req[]>('/assets/maintenance/requests'))
    setPhotoN({ ...(await get<Record<string, number>>('/attachments/maintenance_request/counts').catch(() => ({}))), ...(await get<Record<string, number>>('/attachments/asset_repair/counts').catch(() => ({}))) })
    setScheds(await get<Sched[]>(`/assets/maintenance/schedules?today=${today()}`))
    setKpi(await get<Kpi[]>(`/assets/maintenance/kpi?from=${range.from}&to=${range.to}`))
  }), [run, range, onOpenCount])
  useEffect(() => { load() }, [load])
  useEffect(() => { run(async () => { setItems(await get<ItemRef[]>('/items')); setWhs(await get<Wh[]>('/warehouses')); setAccounts(await get<Account[]>('/accounts')) }) }, [run])
  const act = (msg: string, fn: () => Promise<unknown>) => run(async () => { await fn(); setInfo(msg); await load() })
  const ask = (q: string) => { const v = prompt(q); return v && v.trim() ? v.trim() : null }
  const active = assets.filter(a => a.status === 'Active')
  const reqChips: Record<string, (r: Req) => boolean> = { urgent: r => (r.priority === 'High' || r.priority === 'Urgent') && ['New', 'Surveying', 'Proposed', 'Approved', 'InProgress'].includes(r.status), noPlan: r => (r.status === 'New' || r.status === 'Surveying') && !r.workOrderNumber }
  const shownReqs = reqs.filter(r => inRange(fr, r.reportedAt) && (!fr.sel.status || r.status === fr.sel.status) && (!fr.sel.priority || r.priority === fr.sel.priority) && (!fr.chip || reqChips[fr.chip]?.(r))
    && matchesQuery(`${r.number} ${r.assetCode} ${r.assetName} ${r.description} ${r.workOrderNumber ?? ''}`, null, fr.q))
  const woChips: Record<string, (r: Repair) => boolean> = { pending: r => r.status === 'PendingApproval', doing: r => r.status === 'InProgress', testing: r => r.status === 'Testing', preventive: r => r.type === 'Preventive' }
  const shownWos = wos.filter(r => inRange(fw, r.reportedDate) && (!fw.sel.status || r.status === fw.sel.status) && (!fw.chip || woChips[fw.chip]?.(r))
    && matchesQuery(`${r.number} ${r.assetCode} ${r.assetName} ${r.description} ${r.assignedTech ?? ''} ${r.vendor ?? ''}`, toNumber(r.estimatedTotal), fw.q))
  const money = (t: string) => (t.trim() ? parseMoney(t) : '0')

  const savePlan = () => {
    if (!plan) return
    const labor = money(plan.labor)
    const materials = plan.mats.filter(m => m.itemId)
    const parsed = materials.map(m => ({ itemId: m.itemId, quantity: parseMoney(m.qty), unitPrice: money(m.price) }))
    if (labor == null || parsed.some(m => m.quantity == null || m.unitPrice == null)) { setError(new Error('Có ô số không hợp lệ.')); return }
    const body = { assetId: plan.assetId, type: plan.type, reportedDate: today(), description: plan.description, requestId: plan.requestId, scheduleId: null, vendor: plan.vendor || null, assignedTech: plan.tech || null,
      rootCause: plan.cause || null, solutionPlan: plan.solution || null, estimatedLaborCost: labor, materials: parsed }
    act(plan.id ? 'Đã lưu phương án.' : 'Đã lập phiếu thi công.', async () => { if (plan.id) await put(`/assets/repairs/${plan.id}/plan`, body); else await post('/assets/repairs', body); setPlan(null) })
  }
  const editPlan = (r: Repair) => setPlan({ id: r.id, requestId: null, assetId: r.assetId, type: r.type, description: r.description, vendor: r.vendor ?? '', tech: r.assignedTech ?? '', cause: r.rootCause ?? '', solution: r.solutionPlan ?? '',
    labor: r.estimatedLaborCost === '0' ? '' : formatMoney(r.estimatedLaborCost), mats: r.materials.map(m => ({ itemId: m.itemId, qty: formatMoney(m.qtyRequested), price: formatMoney(m.unitPrice) })) })
  const setMat = (i: number, patch: Partial<MatLine>) => plan && setPlan({ ...plan, mats: plan.mats.map((m, k) => k === i ? { ...m, ...patch } : m) })

  return (
    <>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}

      <section className="panel" aria-label="Yêu cầu sửa chữa">
        <h3>Yêu cầu sửa chữa / báo sự cố (YCSC) <span className="muted">· người vận hành lập; kỹ thuật viên khảo sát rồi lập phiếu thi công kèm phương án và dự toán</span></h3>
        <div className="form-row">
          <label>Tài sản<select aria-label="Tài sản báo sự cố" value={rq.assetId} onChange={e => setRq({ ...rq, assetId: e.target.value })}><option value="">—</option>{active.map(a => <option key={a.id} value={a.id}>{a.code} – {a.name}</option>)}</select></label>
          <label>Mức ưu tiên<select value={rq.priority} onChange={e => setRq({ ...rq, priority: e.target.value })}>{(Object.keys(PRIORITY_VI) as (keyof typeof PRIORITY_VI)[]).map(k => <option key={k} value={k}>{PRIORITY_VI[k]}</option>)}</select></label>
          <label>Hiện tượng hư hỏng<input aria-label="Hiện tượng hư hỏng" value={rq.description} onChange={e => setRq({ ...rq, description: e.target.value })} size={40} /></label>
          <button className="primary" disabled={busy || !rq.assetId || !rq.description.trim()} onClick={() => act('Đã lập yêu cầu sửa chữa.', async () => { await post('/assets/maintenance/requests', rq); setRq({ ...rq, description: '' }) })}>Báo sự cố</button>
        </div>
        <SmartFilter label="Yêu cầu sửa chữa" dateLabel="ngày báo sự cố" state={fr} patch={pfr} clear={cfr} shown={shownReqs.length} total={reqs.length} placeholder="Tìm số, tài sản, hiện tượng"
          selects={[{ key: 'priority', label: 'Ưu tiên', options: Object.entries(PRIORITY_VI).map(([value, label]) => ({ value, label })) }, { key: 'status', label: 'Trạng thái', options: Object.entries(REQ_VI).map(([value, label]) => ({ value, label })) }]}
          chips={[{ key: 'urgent', label: 'Ưu tiên cao / khẩn cấp đang xử lý', count: reqs.filter(reqChips.urgent).length }, { key: 'noPlan', label: 'Chưa có phương án', count: reqs.filter(reqChips.noPlan).length }]} />
        <table className="grid"><thead><tr><th>Số</th><th>Tài sản</th><th>Ưu tiên</th><th>Hiện tượng</th><th>Báo lúc</th><th>Trạng thái</th><th /></tr></thead>
          <tbody>{shownReqs.map(r => <tr key={r.id}><td>{r.number}</td><td>{r.assetCode} – {r.assetName}</td><td>{PRIORITY_VI[r.priority]}</td><td>{r.description}<PhotosToggle entityType="maintenance_request" entityId={r.id} count={photoN[r.id] ?? 0} onChange={() => load()} /></td><td>{formatDateTime(r.reportedAt)}</td>
            <td>{REQ_VI[r.status]}{r.workOrderNumber ? ` · ${r.workOrderNumber}` : ''}{r.status === 'Rejected' && r.note ? ` — ${r.note}` : ''}</td>
            <td style={{ display: 'flex', gap: 6 }}>{(r.status === 'New' || r.status === 'Surveying') && <>
              {!r.workOrderNumber && <button disabled={busy} onClick={() => setPlan({ id: null, requestId: r.id, assetId: r.assetId, type: 'Emergency', description: r.description, vendor: '', tech: '', cause: '', solution: '', labor: '', mats: [] })}>Khảo sát, lập phương án</button>}
              <button disabled={busy} onClick={() => { const reason = ask('Lý do từ chối yêu cầu:'); if (reason) act(`Đã từ chối ${r.number}.`, () => post(`/assets/maintenance/requests/${r.id}/reject`, { reason })) }}>Từ chối</button></>}</td></tr>)}
            {shownReqs.length === 0 && <tr><td colSpan={7} className="muted">{reqs.length === 0 ? 'Chưa có yêu cầu sửa chữa.' : 'Không có yêu cầu khớp bộ lọc.'}</td></tr>}</tbody></table>
      </section>

      <section className="panel" aria-label="Phiếu thi công">
        <h3>Phiếu thi công sửa chữa, bảo trì <span className="muted">· Nháp → Chờ duyệt → Đã duyệt → Đang thi công → Chờ nghiệm thu → Đã nghiệm thu → Đã đóng. Không ghi Nhật ký; vật tư xuất bằng phiếu xuất kho (ghi sổ ở màn Kho). Người duyệt khác người lập.</span></h3>
        <div className="toolbar"><button onClick={() => setPlan({ id: null, requestId: null, assetId: '', type: 'Emergency', description: '', vendor: '', tech: '', cause: '', solution: '', labor: '', mats: [] })}>Lập phiếu thi công</button></div>
        {plan && <div className="panel" style={{ background: 'var(--bg-soft, #f7f8fa)' }}>
          <div className="form-row">
            <label>Tài sản<select aria-label="Tài sản thi công" value={plan.assetId} disabled={!!plan.id || !!plan.requestId} onChange={e => setPlan({ ...plan, assetId: e.target.value })}><option value="">—</option>{active.map(a => <option key={a.id} value={a.id}>{a.code} – {a.name}</option>)}</select></label>
            <label>Loại<select value={plan.type} disabled={!!plan.id} onChange={e => setPlan({ ...plan, type: e.target.value as 'Emergency' | 'Preventive' })}><option value="Emergency">Sửa chữa khẩn cấp</option><option value="Preventive">Bảo trì phòng ngừa</option></select></label>
            <label>Nội dung<input aria-label="Nội dung thi công" value={plan.description} onChange={e => setPlan({ ...plan, description: e.target.value })} size={34} /></label>
            <label>Kỹ thuật viên phụ trách<input value={plan.tech} onChange={e => setPlan({ ...plan, tech: e.target.value })} /></label>
            <label>Đơn vị sửa bên ngoài<input value={plan.vendor} onChange={e => setPlan({ ...plan, vendor: e.target.value })} /></label>
          </div>
          <div className="form-row">
            <label>Nguyên nhân hư hỏng<input aria-label="Nguyên nhân hư hỏng" value={plan.cause} onChange={e => setPlan({ ...plan, cause: e.target.value })} size={34} /></label>
            <label>Phương án xử lý<input aria-label="Phương án xử lý" value={plan.solution} onChange={e => setPlan({ ...plan, solution: e.target.value })} size={34} /></label>
            <label>Dự toán nhân công<input aria-label="Dự toán nhân công" inputMode="decimal" value={plan.labor} onChange={e => setPlan({ ...plan, labor: e.target.value })} /></label>
          </div>
          <table className="grid"><thead><tr><th>Vật tư, phụ tùng thay thế</th><th className="num">Số lượng</th><th className="num">Đơn giá</th><th /></tr></thead>
            <tbody>{plan.mats.map((m, i) => <tr key={i}>
              <td><select aria-label={`Vật tư ${i + 1}`} value={m.itemId} onChange={e => setMat(i, { itemId: e.target.value })}><option value="">—</option>{items.map(it => <option key={it.id} value={it.id}>{it.code} – {it.name} ({it.unit})</option>)}</select></td>
              <td><input className="num" aria-label={`Số lượng vật tư ${i + 1}`} value={m.qty} onChange={e => setMat(i, { qty: e.target.value })} size={8} /></td>
              <td><input className="num" aria-label={`Đơn giá vật tư ${i + 1}`} value={m.price} onChange={e => setMat(i, { price: e.target.value })} size={10} /></td>
              <td><button onClick={() => setPlan({ ...plan, mats: plan.mats.filter((_, k) => k !== i) })}>Bỏ</button></td></tr>)}</tbody></table>
          <div className="toolbar">
            <button onClick={() => setPlan({ ...plan, mats: [...plan.mats, { itemId: '', qty: '', price: '' }] })}>+ Thêm vật tư</button>
            <button className="primary" disabled={busy || !plan.assetId || !plan.description.trim()} onClick={savePlan}>{plan.id ? 'Lưu phương án' : 'Lập phiếu'}</button>
            <button onClick={() => setPlan(null)}>Đóng</button></div>
        </div>}
        <SmartFilter label="Phiếu thi công" dateLabel="ngày lập phiếu" state={fw} patch={pfw} clear={cfw} shown={shownWos.length} total={wos.length} placeholder="Tìm số, tài sản, nội dung, kỹ thuật viên; ví dụ: >5tr"
          selects={[{ key: 'status', label: 'Trạng thái', options: Object.entries(STATUS_VI).map(([value, label]) => ({ value, label })) }]}
          chips={[{ key: 'pending', label: 'Chờ duyệt', count: wos.filter(woChips.pending).length }, { key: 'doing', label: 'Đang thi công', count: wos.filter(woChips.doing).length },
            { key: 'testing', label: 'Chờ nghiệm thu', count: wos.filter(woChips.testing).length }, { key: 'preventive', label: 'Bảo trì định kỳ', count: wos.filter(woChips.preventive).length }]} />
        <table className="grid"><thead><tr><th>Số</th><th>Tài sản</th><th>Loại</th><th>Nội dung</th><th className="num">Dự toán</th><th className="num">Thực tế</th><th>Trạng thái</th><th /></tr></thead>
          <tbody>{shownWos.map(r => <Fragment key={r.id}>
            <tr><td><button className="link" onClick={() => setOpen(open === r.id ? null : r.id)}>{r.number}</button></td><td>{r.assetCode} – {r.assetName}</td><td>{r.type === 'Emergency' ? 'Sửa chữa' : 'Bảo trì'}</td>
              <td>{r.description}<PhotosToggle entityType="asset_repair" entityId={r.id} count={photoN[r.id] ?? 0} onChange={() => load()} /></td><td className="num">{formatMoney(r.estimatedTotal)}</td><td className="num">{r.status === 'Draft' || r.status === 'PendingApproval' || r.status === 'Approved' ? '' : formatMoney(r.actualTotal)}</td>
              <td>{STATUS_VI[r.status]}{(r.status === 'Rejected' || r.status === 'Cancelled') && r.note ? ` — ${r.note}` : ''}</td>
              <td style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                {r.status === 'Draft' && <><button disabled={busy} onClick={() => editPlan(r)}>Sửa phương án</button><button className="primary" disabled={busy} onClick={() => act(`Đã gửi duyệt ${r.number}.`, () => post(`/assets/repairs/${r.id}/submit`))}>Gửi duyệt</button></>}
                {r.status === 'PendingApproval' && <><button className="primary" disabled={busy} onClick={() => act(`Đã duyệt ${r.number}.`, () => post(`/assets/repairs/${r.id}/decide`, { approve: true, note: null }))}>Duyệt</button>
                  <button disabled={busy} onClick={() => { const note = ask('Lý do từ chối phương án:'); if (note) act(`Đã từ chối ${r.number}.`, () => post(`/assets/repairs/${r.id}/decide`, { approve: false, note })) }}>Từ chối</button></>}
                {r.status === 'Approved' && <button className="primary" disabled={busy} onClick={() => act(`Bắt đầu thi công ${r.number}.`, () => post(`/assets/repairs/${r.id}/start`))}>Bắt đầu thi công</button>}
                {r.status === 'InProgress' && <>
                  {r.materials.some(m => m.status === 'Pending') && <button disabled={busy} onClick={() => setIssue({ id: r.id, number: r.number, warehouseId: whs[0]?.id ?? '', accountId: '' })}>Xuất vật tư</button>}
                  <button className="primary" disabled={busy} onClick={() => setTest({ id: r.id, number: r.number, labor: r.estimatedLaborCost === '0' ? '' : formatMoney(r.estimatedLaborCost), note: '' })}>Yêu cầu nghiệm thu</button></>}
                {r.status === 'Testing' && <><button className="primary" disabled={busy} onClick={() => act(`Đã nghiệm thu ${r.number}.`, () => post(`/assets/repairs/${r.id}/accept`, { passed: true, note: null }))}>Nghiệm thu đạt</button>
                  <button disabled={busy} onClick={() => { const note = ask('Lý do không đạt:'); if (note) act(`Trả ${r.number} về thi công.`, () => post(`/assets/repairs/${r.id}/accept`, { passed: false, note })) }}>Không đạt</button></>}
                {r.status === 'Completed' && <button className="primary" disabled={busy} onClick={() => act(`Đã đóng ${r.number}.`, () => post(`/assets/repairs/${r.id}/close`))}>Đóng phiếu</button>}
                {['Draft', 'PendingApproval', 'Approved', 'InProgress'].includes(r.status) && <button disabled={busy} onClick={() => { const reason = ask('Lý do hủy phiếu:'); if (reason) act(`Đã hủy ${r.number}.`, () => post(`/assets/repairs/${r.id}/cancel`, { reason })) }}>Hủy</button>}
              </td></tr>
            {open === r.id && <tr><td colSpan={8} className="muted">
              {r.requestNumber && <div>Yêu cầu: {r.requestNumber}</div>}
              {r.rootCause && <div>Nguyên nhân: {r.rootCause}</div>}{r.solutionPlan && <div>Phương án: {r.solutionPlan}</div>}
              {r.assignedTech && <div>Kỹ thuật viên: {r.assignedTech}</div>}{r.vendor && <div>Đơn vị sửa: {r.vendor}</div>}
              <div>Dự toán nhân công {formatMoney(r.estimatedLaborCost)} + vật tư {formatMoney(r.estimatedMaterialCost)}{r.actualLaborCost != null ? `; thực tế nhân công ${formatMoney(r.actualLaborCost)} + vật tư ${formatMoney(r.actualMaterialCost)}` : ''}</div>
              {r.materials.length > 0 && <ul>{r.materials.map(m => <li key={m.id}>{m.itemCode} – {m.itemName}: {formatMoney(m.qtyRequested)} {m.unit} × {formatMoney(m.unitPrice)}{m.status === 'Issued' ? ` — đã xuất${m.stockDocumentNumber ? ` (phiếu ${m.stockDocumentNumber})` : ''}` : ''}</li>)}</ul>}
            </td></tr>}</Fragment>)}
            {shownWos.length === 0 && <tr><td colSpan={8} className="muted">{wos.length === 0 ? 'Chưa có phiếu thi công.' : 'Không có phiếu khớp bộ lọc.'}</td></tr>}</tbody></table>
        {issue && <div className="form-row"><strong>{issue.number}</strong>
          <label>Kho xuất<select aria-label="Kho xuất vật tư" value={issue.warehouseId} onChange={e => setIssue({ ...issue, warehouseId: e.target.value })}>{whs.map(w => <option key={w.id} value={w.id}>{w.code} – {w.name}</option>)}</select></label>
          <label>TK chi phí sửa chữa <AccountSelect accounts={accounts} value={issue.accountId} onChange={v => setIssue({ ...issue, accountId: v })} filter={a => a.cashKind === 'None' && a.openItemNature === 'None' && !a.requiresPartner} ariaLabel="TK chi phí sửa chữa" /></label>
          <button className="primary" disabled={busy || !issue.warehouseId || !issue.accountId} onClick={() => act(`Đã lập phiếu xuất kho Nháp cho ${issue.number}; ghi sổ ở màn Kho.`, async () => { await post(`/assets/repairs/${issue.id}/issue`, { warehouseId: issue.warehouseId, counterAccountId: issue.accountId, date: today() }); setIssue(null) })}>Lập phiếu xuất kho</button>
          <button onClick={() => setIssue(null)}>Đóng</button></div>}
        {test && <div className="form-row"><strong>{test.number}</strong>
          <label>Chi phí nhân công thực tế<input aria-label="Chi phí nhân công thực tế" inputMode="decimal" value={test.labor} onChange={e => setTest({ ...test, labor: e.target.value })} /></label>
          <label>Ghi chú<input value={test.note} onChange={e => setTest({ ...test, note: e.target.value })} /></label>
          <button className="primary" disabled={busy} onClick={() => { const labor = money(test.labor); if (labor == null) { setError(new Error('Chi phí không hợp lệ.')); return }
            act(`Đã yêu cầu nghiệm thu ${test.number}.`, async () => { await post(`/assets/repairs/${test.id}/test`, { actualLaborCost: labor, note: test.note || null }); setTest(null) }) }}>Gửi nghiệm thu</button>
          <button onClick={() => setTest(null)}>Đóng</button></div>}
      </section>

      <section className="panel" aria-label="Lịch bảo trì">
        <h3>Lịch bảo trì phòng ngừa định kỳ <span className="muted">· đến hạn thì lập phiếu bảo trì; đóng phiếu thì dời hạn kế tiếp theo chu kỳ</span></h3>
        <div className="form-row">
          <label>Tài sản<select aria-label="Tài sản bảo trì" value={sf.assetId} onChange={e => setSf({ ...sf, assetId: e.target.value })}><option value="">—</option>{active.map(a => <option key={a.id} value={a.id}>{a.code} – {a.name}</option>)}</select></label>
          <label>Hạng mục bảo dưỡng<input aria-label="Hạng mục bảo dưỡng" value={sf.task} onChange={e => setSf({ ...sf, task: e.target.value })} size={30} /></label>
          <label>Chu kỳ (ngày)<input className="num" aria-label="Chu kỳ ngày" value={sf.days} onChange={e => setSf({ ...sf, days: e.target.value })} size={5} /></label>
          <label>Hạn đầu tiên<input type="date" value={sf.due} onChange={e => setSf({ ...sf, due: e.target.value })} /></label>
          <button className="primary" disabled={busy || !sf.assetId || !sf.task.trim() || !/^\d+$/.test(sf.days)} onClick={() => act('Đã lưu lịch bảo trì.', async () => { await post('/assets/maintenance/schedules', { id: null, assetId: sf.assetId, taskName: sf.task, frequencyDays: Number(sf.days), nextDueDate: sf.due, isActive: true }); setSf({ ...sf, task: '' }) })}>Thêm lịch</button>
        </div>
        <table className="grid"><thead><tr><th>Tài sản</th><th>Hạng mục</th><th className="num">Chu kỳ</th><th>Lần gần nhất</th><th>Hạn kế tiếp</th><th>Tình trạng</th><th /></tr></thead>
          <tbody>{scheds.map(s => <tr key={s.id}><td>{s.assetCode} – {s.assetName}</td><td>{s.taskName}</td><td className="num">{s.frequencyDays} ngày</td><td>{formatDate(s.lastPerformedAt)}</td><td>{formatDate(s.nextDueDate)}</td>
            <td>{!s.isActive ? 'Đã tắt' : s.overdue ? `Quá hạn ${-s.daysToDue} ngày` : s.daysToDue <= 7 ? `Còn ${s.daysToDue} ngày` : ''}</td>
            <td>{s.isActive && <button disabled={busy} onClick={() => act('Đã lập phiếu bảo trì Nháp.', () => post(`/assets/maintenance/schedules/${s.id}/work-order?date=${today()}`))}>Lập phiếu bảo trì</button>}</td></tr>)}
            {scheds.length === 0 && <tr><td colSpan={7} className="muted">Chưa có lịch bảo trì.</td></tr>}</tbody></table>
      </section>

      <section className="panel" aria-label="KPI bảo trì">
        <h3>KPI bảo trì và chi phí theo máy <span className="muted">· MTBF = (giờ kế hoạch − giờ dừng) ÷ số sự cố; MTTR = giờ sửa ÷ số sự cố; chỉ tính phiếu đã nghiệm thu trong kỳ</span></h3>
        <div className="toolbar">
          <label>Từ<input type="date" value={range.from} onChange={e => setRange({ ...range, from: e.target.value })} /></label>
          <label>Đến<input type="date" value={range.to} onChange={e => setRange({ ...range, to: e.target.value })} /></label></div>
        <table className="grid"><thead><tr><th>Tài sản</th><th>Bộ phận</th><th className="num">Số sự cố</th><th className="num">Giờ sửa</th><th className="num">MTTR (giờ)</th><th className="num">MTBF (giờ)</th><th className="num">Dừng máy %</th><th className="num">Nhân công</th><th className="num">Vật tư</th><th className="num">Tổng chi phí</th></tr></thead>
          <tbody>{kpi.map(k => <tr key={k.assetId}><td>{k.assetCode} – {k.assetName}</td><td>{k.department ?? ''}</td><td className="num">{k.failures}</td><td className="num">{formatMoney(k.repairHours)}</td><td className="num">{formatMoney(k.mttr)}</td><td className="num">{formatMoney(k.mtbf)}</td><td className="num">{formatMoney(k.downtimeRate)}</td>
            <td className="num">{formatMoney(k.laborCost)}</td><td className="num">{formatMoney(k.materialCost)}</td><td className="num">{formatMoney(k.totalCost)}</td></tr>)}
            {kpi.length === 0 && <tr><td colSpan={10} className="muted">Chưa có phiếu nghiệm thu trong kỳ.</td></tr>}</tbody></table>
      </section>
    </>
  )
}
