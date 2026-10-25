import { useEffect, useState } from 'react'
import { get } from './api'
import { navStore } from './common'
import { formatDate } from './money'
import type { OpenType } from './navIntents'

interface Task {
  screen: string; kind: 'Sales' | 'Purchase' | null; entityType: OpenType; entityId: string; number: string; title: string; action: string
  ownerId: string; ownerName: string; date: string; ageDays: number; blockedReason: string | null; due: string | null; assignees: string[] | null
}

/** Nguồn dữ liệu của việc (màn hình sở hữu chứng từ), để người dùng biết việc đến từ đâu. */
const SOURCE: Record<string, string> = {
  vouchers: 'Chứng từ kế toán', trade: 'Hóa đơn', stock: 'Kho', bank: 'Ngân hàng', orders: 'Đơn hàng', contracts: 'Hợp đồng', advances: 'Tạm ứng', services: 'Nghiệm thu dịch vụ', production: 'Sản xuất', assets: 'Tài sản / CCDC',
}

/** Việc thuộc chu trình nào: bán hàng và mua hàng tách riêng; còn lại theo phân hệ. Thứ tự hiện cố định. */
const CYCLES = [
  { key: 'sales', label: 'Chu trình bán hàng' }, { key: 'purchase', label: 'Chu trình mua hàng' },
  { key: 'money', label: 'Tiền, thanh toán' }, { key: 'stock', label: 'Kho' }, { key: 'production', label: 'Chu trình sản xuất' }, { key: 'assets', label: 'Tài sản, CCDC' }, { key: 'gl', label: 'Kế toán tổng hợp' },
] as const
function cycleOf(t: Task): (typeof CYCLES)[number]['key'] {
  if (t.kind === 'Sales') return 'sales'
  if (t.kind === 'Purchase') return 'purchase'
  if (t.screen === 'bank' || t.screen === 'advances') return 'money'
  if (t.screen === 'stock') return 'stock'
  if (t.screen === 'production') return 'production'
  if (t.screen === 'assets') return 'assets'
  return 'gl'
}

/**
 * Việc cần làm theo từng chứng từ: mỗi dòng là một chứng từ cụ thể kèm hành động kế tiếp, người lập, tuổi việc và (nếu chưa làm được) lý do bị chặn.
 * Bấm "Mở" đi thẳng tới đúng chứng từ ở màn hình của nó — không chỉ tới màn danh sách chung.
 */
export function Worklist({ limit = 20 }: { limit?: number }) {
  const [tasks, setTasks] = useState<Task[] | null>(null)
  const [all, setAll] = useState(false)
  useEffect(() => {
    let live = true
    get<Task[]>('/cycles/home/tasks?take=200').then(t => { if (live) setTasks(t) }).catch(() => { if (live) setTasks([]) })
    return () => { live = false }
  }, [])
  if (tasks === null || tasks.length === 0) return null
  const shown = all ? tasks : tasks.slice(0, limit)
  const open = (t: Task) => navStore.go?.(t.screen, t.kind ?? undefined, { open: { type: t.entityType, id: t.entityId } })
  return (
    <section className="panel" aria-label="Việc cần làm theo chứng từ">
      <div className="toolbar"><h3 style={{ margin: 0 }}>Chứng từ đang chờ bạn <span className="badge-count">{tasks.length}</span></h3>
        <span className="muted">Việc làm được ngay xếp trước; việc bị chặn ghi rõ lý do.</span></div>
      <div className="scroll"><table className="grid">
        <thead><tr><th>Việc cần làm</th><th>Chứng từ</th><th>Nội dung</th><th>Nguồn</th><th>Người lập</th><th>Người có thể làm</th><th>Ngày</th><th>Hạn</th><th className="num">Tuổi (ngày)</th><th>Tình trạng</th><th /></tr></thead>
        <tbody>{CYCLES.flatMap(c => {
          const rows = shown.filter(t => cycleOf(t) === c.key)
          if (rows.length === 0) return []
          return [<tr key={'h:' + c.key} className="group-row"><th colSpan={11} style={{ textAlign: 'left' }}>{c.label} <span className="badge-count">{rows.length}</span></th></tr>, ...rows.map(t => (
          <tr key={t.entityType + t.entityId}>
            <td><strong>{t.action}</strong></td><td>{t.number}</td><td>{t.title}</td><td>{SOURCE[t.screen] ?? t.screen}</td>
            <td>{t.ownerName}</td><td>{(t.assignees ?? []).join(', ')}</td><td>{formatDate(t.date)}</td><td>{t.due ? formatDate(t.due) : ''}</td><td className="num">{t.ageDays}</td>
            <td>{t.blockedReason ? <span className="muted" title={t.blockedReason}>⛔ {t.blockedReason}</span> : 'Làm được ngay'}</td>
            <td><button className={t.blockedReason ? '' : 'primary'} onClick={() => open(t)}>Mở</button></td>
          </tr>))]
        })}</tbody>
      </table></div>
      {tasks.length > limit && <p className="muted">{all ? '' : `Đang hiện ${limit}/${tasks.length}. `}<button onClick={() => setAll(a => !a)}>{all ? 'Thu gọn' : 'Xem tất cả'}</button></p>}
    </section>
  )
}
