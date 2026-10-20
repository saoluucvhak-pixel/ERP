import { useEffect, useState } from 'react'
import { get } from './api'
import { ErrorBox, useAction } from './common'
import { formatDate, formatMoney } from './money'

interface JournalEvent { date: string; group: string; title: string; detail: string | null; amount: string | null; reference: string | null; by: string | null; voided: boolean }
interface Journal { assetId: string; code: string; name: string; class: 'FixedAsset' | 'Tool'; status: string; originalCost: string; accumulated: string; netBookValue: string; department: string | null; custodian: string | null; repairCost: string; repairCount: number; events: JournalEvent[] }

const GROUPS = ['Hình thành', 'Sử dụng', 'Sửa chữa', 'Kiểm kê', 'Giá trị', 'Kết thúc']
const STATUS_VI: Record<string, string> = { Draft: 'Nháp', Active: 'Đang sử dụng', Disposed: 'Đã ghi giảm', Cancelled: 'Đã hủy' }

/** Nhật ký tài sản: mọi thông tin và biến động của một tài sản theo thời gian (dựng từ dữ liệu gốc). */
export function AssetJournal({ assetId, onClose }: { assetId: string; onClose: () => void }) {
  const [j, setJ] = useState<Journal | null>(null)
  const [hidden, setHidden] = useState<string[]>([])
  const { error, run } = useAction()
  useEffect(() => { run(async () => setJ(await get<Journal>(`/assets/${assetId}/journal`))) }, [assetId, run])
  if (!j) return <section className="panel"><ErrorBox error={error} /></section>
  const shown = j.events.filter(e => !hidden.includes(e.group))
  return (
    <section className="panel" aria-label="Nhật ký tài sản">
      <div className="toolbar"><h3>Nhật ký tài sản {j.code} – {j.name}</h3><button onClick={onClose}>Đóng</button></div>
      <ErrorBox error={error} />
      <p>{j.class === 'FixedAsset' ? 'TSCĐ' : 'CCDC'} · {STATUS_VI[j.status] ?? j.status}{j.department ? ` · ${j.department}` : ''}{j.custodian ? ` · ${j.custodian}` : ''}</p>
      <div className="toolbar" style={{ gap: 24 }}>
        <span>Nguyên giá <b>{formatMoney(j.originalCost)}</b></span>
        <span>Hao mòn / đã phân bổ <b>{formatMoney(j.accumulated)}</b></span>
        <span>Giá trị còn lại <b>{formatMoney(j.netBookValue)}</b></span>
        <span>Sửa chữa <b>{j.repairCount}</b> lần, chi phí <b>{formatMoney(j.repairCost)}</b></span>
      </div>
      <div className="toolbar">
        {GROUPS.map(g => <label key={g}><input type="checkbox" checked={!hidden.includes(g)} onChange={e => setHidden(e.target.checked ? hidden.filter(x => x !== g) : [...hidden, g])} /> {g}</label>)}
      </div>
      <table className="grid"><thead><tr><th>Ngày</th><th>Nhóm</th><th>Biến động</th><th>Nội dung</th><th className="num">Số tiền</th><th>Chứng từ / số phiếu</th><th>Người thực hiện</th></tr></thead>
        <tbody>{shown.map((e, i) => <tr key={i} style={e.voided ? { textDecoration: 'line-through', opacity: 0.6 } : undefined}>
          <td>{formatDate(e.date)}</td><td>{e.group}</td><td>{e.title}</td><td>{e.detail ?? ''}</td><td className="num">{e.amount == null ? '' : formatMoney(e.amount)}</td><td>{e.reference ?? ''}</td><td>{e.by ?? ''}</td></tr>)}
          {shown.length === 0 && <tr><td colSpan={7} className="muted">Không có biến động.</td></tr>}</tbody></table>
    </section>
  )
}
