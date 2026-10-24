import { useEffect, useRef, useState } from 'react'
import { navStore } from './common'
import { QUICK_GROUPS } from './quickadd'

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
        {QUICK_GROUPS.map(g => <div key={g.title} className="quick-group"><div className="quick-title">{g.title}</div>
          {g.items.map(i => <button key={i.label} role="menuitem" className="link" onClick={() => { setOpen(false); navStore.go?.(i.screen, i.kind, i.intent) }}>{i.label}</button>)}</div>)}
      </div>}
    </div>
  )
}
