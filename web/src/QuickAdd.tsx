import { useEffect, useRef, useState } from 'react'
import { navStore } from './common'

const GROUPS: { title: string; items: { label: string; screen: string }[] }[] = [
  { title: 'Tiền', items: [{ label: 'Thu tiền', screen: 'cash' }, { label: 'Chi tiền', screen: 'cash' }, { label: 'Lệnh thanh toán ngân hàng', screen: 'bank' }] },
  { title: 'Mua hàng', items: [{ label: 'Đơn mua hàng', screen: 'orders' }, { label: 'Nhận hàng, nhập kho', screen: 'procurement' }, { label: 'Hóa đơn mua', screen: 'trade' }] },
  { title: 'Bán hàng', items: [{ label: 'Đơn bán hàng', screen: 'orders' }, { label: 'Lệnh bán, giao hàng', screen: 'revenue' }, { label: 'Hóa đơn bán', screen: 'trade' }] },
  { title: 'Kho', items: [{ label: 'Nhập kho', screen: 'stock' }, { label: 'Xuất kho', screen: 'stock' }, { label: 'Kiểm kê', screen: 'stock' }] },
  { title: 'Sản xuất, tài sản', items: [{ label: 'Lệnh sản xuất', screen: 'production' }, { label: 'Đề nghị mua sắm tài sản', screen: 'assets' }] },
  { title: 'Tổng hợp', items: [{ label: 'Chứng từ nghiệp vụ khác', screen: 'vouchers' }] },
]

/** Nút "Thêm nhanh" ở đầu thanh bên: bảng lối tắt tới màn hình lập chứng từ của từng phân hệ. */
export function QuickAdd() {
  const [open, setOpen] = useState(false)
  const box = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (!open) return
    const close = (e: MouseEvent) => { if (!box.current?.contains(e.target as Node)) setOpen(false) }
    const esc = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false) }
    document.addEventListener('mousedown', close); document.addEventListener('keydown', esc)
    return () => { document.removeEventListener('mousedown', close); document.removeEventListener('keydown', esc) }
  }, [open])
  return (
    <div className="quick-add" ref={box}>
      <button className="quick-add-btn" aria-expanded={open} onClick={() => setOpen(o => !o)}>＋ Thêm nhanh</button>
      {open && <div className="quick-add-pop" role="menu">
        {GROUPS.map(g => <div key={g.title} className="quick-group"><div className="quick-title">{g.title}</div>
          {g.items.map(i => <button key={i.label} role="menuitem" className="link" onClick={() => { setOpen(false); navStore.go?.(i.screen) }}>{i.label}</button>)}</div>)}
      </div>}
    </div>
  )
}
