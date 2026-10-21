import { useEffect, useState } from 'react'
import { get } from './api'
import { ErrorBox, navStore, useAction } from './common'
import { formatDate, formatMoney } from './money'

interface Axis { axis: string; state: string; label: string; detail: string }
interface FlowDoc { kind: string; id: string; number: string; status: string; date: string | null; quantity: string | null; amount: string | null; screen: string }
interface Next { code: string; label: string; screen: string; ownerId: string; ownerName: string; due: string | null }
export interface OrderFlowData { orderId: string; number: string; kind: string; status: string; partnerName: string; axes: Axis[]; documents: FlowDoc[]; next: Next | null }
interface WorkRow { orderId: string; number: string; kind: string; partnerName: string; actionCode: string; action: string; screen: string; ownerName: string; due: string | null; ageDays: number; overdue: boolean }

const AXIS_VI: Record<string, string> = { Approval: 'Duyệt', Fulfilment: 'Thực hiện', Invoicing: 'Hóa đơn', Payment: 'Tiền' }
const STATE_COLOR: Record<string, string> = { None: 'var(--muted, #888)', Partial: 'var(--warn, #b7791f)', Full: 'var(--ok, #2f855a)', Closed: 'var(--muted, #888)', NA: 'var(--muted, #888)' }

/** Hồ sơ chu trình của một đơn: bốn trục trạng thái độc lập, hành động kế tiếp kèm người phụ trách và hạn, và các chứng từ liên quan để bấm đi tới. */
export function OrderFlowPanel({ orderId, onClose }: { orderId: string; onClose: () => void }) {
  const [flow, setFlow] = useState<OrderFlowData | null>(null)
  const { error, run } = useAction()
  useEffect(() => { run(async () => setFlow(await get<OrderFlowData>(`/orders/${orderId}/flow`))) }, [orderId, run])
  return (
    <div className="panel">
      <div className="toolbar"><strong>Hồ sơ chu trình {flow?.number ?? ''}{flow ? ` · ${flow.partnerName}` : ''}</strong><button onClick={onClose}>Đóng</button></div>
      <ErrorBox error={error} />
      {flow && <>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(190px, 1fr))', gap: 8, margin: '8px 0' }}>
          {flow.axes.map(a => (
            <div key={a.axis} style={{ border: '1px solid var(--line)', borderRadius: 6, padding: '6px 8px', borderLeft: `4px solid ${STATE_COLOR[a.state] ?? 'var(--line)'}` }}>
              <div className="muted">{AXIS_VI[a.axis] ?? a.axis}</div>
              <strong>{a.label}</strong>
              <div className="muted">{a.detail}</div>
            </div>))}
        </div>
        {flow.next
          ? <div className="msg ok">Việc kế tiếp: <strong>{flow.next.label}</strong> · phụ trách {flow.next.ownerName || '—'}{flow.next.due ? ` · hạn ${formatDate(flow.next.due)}` : ''}
              {' '}<button onClick={() => navStore.go?.(flow.next!.screen)}>Mở màn hình</button></div>
          : <div className="muted">Không còn việc kế tiếp.</div>}
        <table className="grid">
          <thead><tr><th>Loại</th><th>Số</th><th>Trạng thái</th><th>Ngày</th><th className="num">Số lượng</th><th className="num">Số tiền</th><th /></tr></thead>
          <tbody>{flow.documents.map((d, i) => (
            <tr key={`${d.kind}-${d.id}-${i}`}><td>{d.kind}</td><td>{d.number}</td><td>{d.status}</td><td>{d.date ? formatDate(d.date) : ''}</td>
              <td className="num">{d.quantity != null ? formatMoney(d.quantity) : ''}</td><td className="num">{d.amount != null ? formatMoney(d.amount) : ''}</td>
              <td><button onClick={() => navStore.go?.(d.screen)}>Mở</button></td></tr>))}
            {flow.documents.length === 0 && <tr><td colSpan={7} className="muted">Chưa có chứng từ phát sinh.</td></tr>}
          </tbody>
        </table>
      </>}
    </div>
  )
}

/** Việc cần làm theo đơn: mỗi đơn chưa xong một dòng với hành động kế tiếp, người phụ trách, hạn; đơn quá hạn lên đầu. */
export function OrderWorklist({ kind, onOpen }: { kind: 'Sales' | 'Purchase'; onOpen: (orderId: string) => void }) {
  const [rows, setRows] = useState<WorkRow[] | null>(null)
  const { error, run } = useAction()
  useEffect(() => { run(async () => setRows(await get<WorkRow[]>(`/cycles/order-worklist?kind=${kind}`))) }, [kind, run])
  return (
    <div className="panel">
      <strong>Việc cần làm theo đơn</strong>
      <ErrorBox error={error} />
      <table className="grid">
        <thead><tr><th>Đơn</th><th>Đối tượng</th><th>Việc kế tiếp</th><th>Phụ trách</th><th>Hạn</th><th className="num">Tuổi (ngày)</th><th /></tr></thead>
        <tbody>{(rows ?? []).map(r => (
          <tr key={r.orderId} style={r.overdue ? { color: 'var(--danger)' } : undefined}>
            <td>{r.number}</td><td>{r.partnerName}</td><td>{r.action}</td><td>{r.ownerName}</td><td>{r.due ? formatDate(r.due) : ''}{r.overdue ? ' (quá hạn)' : ''}</td><td className="num">{r.ageDays}</td>
            <td style={{ display: 'flex', gap: 4 }}><button onClick={() => onOpen(r.orderId)}>Hồ sơ</button><button onClick={() => navStore.go?.(r.screen)}>Làm tiếp</button></td></tr>))}
          {rows && rows.length === 0 && <tr><td colSpan={7} className="muted">Không có đơn nào đang chờ xử lý.</td></tr>}
        </tbody>
      </table>
    </div>
  )
}
