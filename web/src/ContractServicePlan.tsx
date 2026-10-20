import { useCallback, useEffect, useState } from 'react'
import { get, put } from './api'
import { ErrorBox, useAction } from './common'
import { formatDate, formatMoney, parseMoney, today } from './money'

interface PlanRow { id: string; contractLineNo: number; planNo: number; name: string; itemCode: string; itemName: string; unit: string; plannedDate: string; plannedQty: string; cumulativePlanned: string; accepted: string; status: string }
interface Draft { contractLineNo: number; name: string; plannedDate: string; plannedQty: string }
const STATUS_VI: Record<string, string> = { DONE: 'Đã nghiệm thu', PARTIAL: 'Đang thực hiện', PENDING: 'Chưa đến hạn', OVERDUE: 'Quá hạn' }

/** Lịch nghiệm thu dịch vụ của hợp đồng: các đợt dự kiến (ngày, số lượng) đối chiếu lũy kế với biên bản nghiệm thu đã xác nhận của các đơn kế thừa. */
export function AcceptancePlanPanel({ contractId, status, serviceLines }: { contractId: string; status: string; serviceLines: { lineNo: number; itemCode: string; itemName: string; unit: string; committedQty: string }[] }) {
  const [rows, setRows] = useState<PlanRow[]>([])
  const [edit, setEdit] = useState<Draft[] | null>(null)
  const { busy, error, run, setError } = useAction()
  const load = useCallback(() => run(async () => setRows(await get<PlanRow[]>(`/contracts/${contractId}/acceptance-plan`))), [run, contractId])
  useEffect(() => { load() }, [load])
  const canEdit = !['Completed', 'Cancelled'].includes(status)
  const save = () => run(async () => {
    const plan = (edit ?? []).filter(d => d.name.trim() || d.plannedQty.trim()).map(d => {
      const q = parseMoney(d.plannedQty)
      if (!q || !d.plannedDate) throw new Error(`Đợt "${d.name || '(chưa đặt tên)'}": nhập ngày và số lượng hợp lệ.`)
      return { contractLineNo: d.contractLineNo, name: d.name, plannedDate: d.plannedDate, plannedQty: q }
    })
    await put(`/contracts/${contractId}/acceptance-plan`, { plan })
    setEdit(null); await load()
  })
  return (
    <>
      <h4>Lịch nghiệm thu dịch vụ</h4>
      <ErrorBox error={error} />
      <div className="scroll"><table className="grid"><thead><tr><th>Dịch vụ</th><th>Đợt</th><th>Ngày dự kiến</th><th className="num">Số lượng dự kiến</th><th className="num">Lũy kế kế hoạch</th><th className="num">Đã nghiệm thu (lũy kế)</th><th>Tình trạng</th></tr></thead>
        <tbody>{rows.map(r => <tr key={r.id} style={r.status === 'OVERDUE' ? { color: 'var(--danger, #b42318)' } : undefined}><td>{r.itemCode} – {r.itemName}</td><td>{r.planNo}. {r.name}</td><td>{formatDate(r.plannedDate)}</td>
          <td className="num">{formatMoney(r.plannedQty)} {r.unit}</td><td className="num">{formatMoney(r.cumulativePlanned)}</td><td className="num">{formatMoney(r.accepted)}</td><td>{STATUS_VI[r.status] ?? r.status}</td></tr>)}
          {rows.length === 0 && <tr><td colSpan={7} className="muted">Chưa khai lịch nghiệm thu.</td></tr>}</tbody></table></div>
      {canEdit && !edit && <button onClick={() => setEdit(rows.length ? rows.map(r => ({ contractLineNo: r.contractLineNo, name: r.name, plannedDate: r.plannedDate, plannedQty: r.plannedQty })) : [{ contractLineNo: serviceLines[0].lineNo, name: '', plannedDate: today(), plannedQty: '' }])}>Sửa lịch nghiệm thu</button>}
      {edit && <div className="panel">
        <table className="grid"><thead><tr><th>Dòng dịch vụ</th><th>Tên đợt</th><th>Ngày dự kiến</th><th className="num">Số lượng</th><th /></tr></thead>
          <tbody>{edit.map((d, i) => <tr key={i}>
            <td><select aria-label="Dòng dịch vụ" value={d.contractLineNo} onChange={e => setEdit(es => es!.map((x, j) => j === i ? { ...x, contractLineNo: Number(e.target.value) } : x))}>{serviceLines.map(l => <option key={l.lineNo} value={l.lineNo}>{l.lineNo}. {l.itemCode} (cam kết {formatMoney(l.committedQty)} {l.unit})</option>)}</select></td>
            <td><input aria-label="Tên đợt" value={d.name} onChange={e => setEdit(es => es!.map((x, j) => j === i ? { ...x, name: e.target.value } : x))} /></td>
            <td><input type="date" aria-label="Ngày dự kiến" value={d.plannedDate} onChange={e => setEdit(es => es!.map((x, j) => j === i ? { ...x, plannedDate: e.target.value } : x))} /></td>
            <td className="num"><input inputMode="decimal" aria-label="Số lượng đợt" value={d.plannedQty} onChange={e => setEdit(es => es!.map((x, j) => j === i ? { ...x, plannedQty: e.target.value } : x))} /></td>
            <td><button aria-label="Xóa đợt" onClick={() => setEdit(es => es!.filter((_, j) => j !== i))}>✕</button></td></tr>)}</tbody></table>
        <div className="toolbar"><button onClick={() => setEdit(es => [...es!, { contractLineNo: serviceLines[0].lineNo, name: '', plannedDate: today(), plannedQty: '' }])}>+ Thêm đợt</button>
          <button className="primary" disabled={busy} onClick={save}>Lưu lịch</button><button onClick={() => { setEdit(null); setError(null) }}>Đóng</button></div>
        <p className="muted">Tổng số lượng các đợt của mỗi dòng không vượt số lượng cam kết. Đối chiếu theo lũy kế: đợt nào chưa đạt mà quá ngày dự kiến thì báo quá hạn.</p>
      </div>}
    </>
  )
}
