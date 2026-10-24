import { useEffect, useState } from 'react'
import { get } from './api'
import { navStore } from './common'
import { formatDate } from './money'
import type { OpenType } from './quickadd'

interface Task {
  screen: string; kind: 'Sales' | 'Purchase' | null; entityType: OpenType; entityId: string; number: string; title: string; action: string
  ownerId: string; ownerName: string; date: string; ageDays: number; blockedReason: string | null; due: string | null
}

/** Nguồn dữ liệu của việc (màn hình sở hữu chứng từ), để người dùng biết việc đến từ đâu. */
const SOURCE: Record<string, string> = {
  vouchers: 'Chứng từ kế toán', trade: 'Hóa đơn', stock: 'Kho', bank: 'Ngân hàng', orders: 'Đơn hàng', contracts: 'Hợp đồng', advances: 'Tạm ứng', services: 'Nghiệm thu dịch vụ',
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
        <thead><tr><th>Việc cần làm</th><th>Chứng từ</th><th>Nội dung</th><th>Nguồn</th><th>Người lập</th><th>Ngày</th><th>Hạn</th><th className="num">Tuổi (ngày)</th><th>Tình trạng</th><th /></tr></thead>
        <tbody>{shown.map(t => (
          <tr key={t.entityType + t.entityId}>
            <td><strong>{t.action}</strong></td><td>{t.number}</td><td>{t.title}</td><td>{SOURCE[t.screen] ?? t.screen}{t.kind ? ` · ${t.kind === 'Sales' ? 'bán' : 'mua'}` : ''}</td>
            <td>{t.ownerName}</td><td>{formatDate(t.date)}</td><td>{t.due ? formatDate(t.due) : ''}</td><td className="num">{t.ageDays}</td>
            <td>{t.blockedReason ? <span className="muted" title={t.blockedReason}>⛔ {t.blockedReason}</span> : 'Làm được ngay'}</td>
            <td><button className={t.blockedReason ? '' : 'primary'} onClick={() => open(t)}>Mở</button></td>
          </tr>))}</tbody>
      </table></div>
      {tasks.length > limit && <p className="muted">{all ? '' : `Đang hiện ${limit}/${tasks.length}. `}<button onClick={() => setAll(a => !a)}>{all ? 'Thu gọn' : 'Xem tất cả'}</button></p>}
    </section>
  )
}
