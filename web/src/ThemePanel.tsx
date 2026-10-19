import { useEffect, useRef, useState } from 'react'
import { Icon } from './Home'
import { SKINS, loadTheme, saveTheme, type Density, type FontSize, type Mode, type Radius, type Sidebar, type Skin, type Theme } from './theme'

const MODES: { id: Mode; label: string; icon: string }[] = [
  { id: 'light', label: 'Sáng', icon: 'sun' }, { id: 'dark', label: 'Tối', icon: 'moon' }, { id: 'auto', label: 'Theo máy', icon: 'auto' },
]

function Seg<T extends string>({ value, options, onChange }: { value: T; options: { id: T; label: string; icon?: string }[]; onChange: (v: T) => void }) {
  return (
    <div className="seg">
      {options.map(o => (
        <button key={o.id} className={o.id === value ? 'on' : ''} aria-pressed={o.id === value} onClick={() => onChange(o.id)}>
          {o.icon && <Icon name={o.icon} size={16} />}{o.label}
        </button>))}
    </div>
  )
}

/** Nút sáng/tối nhanh + bảng "Giao diện": kiểu màu, chế độ, mật độ, bo góc, cỡ chữ, thanh bên. Lưu theo trình duyệt từng máy. */
export function ThemePanel() {
  const [t, setT] = useState<Theme>(loadTheme)
  const [open, setOpen] = useState(false)
  const box = useRef<HTMLDivElement>(null)
  const change = (patch: Partial<Theme>) => { const n = { ...t, ...patch }; setT(n); saveTheme(n) }
  useEffect(() => {
    if (!open) return
    const off = (e: MouseEvent) => { if (box.current && !box.current.contains(e.target as Node)) setOpen(false) }
    const esc = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false) }
    document.addEventListener('mousedown', off); document.addEventListener('keydown', esc)
    return () => { document.removeEventListener('mousedown', off); document.removeEventListener('keydown', esc) }
  }, [open])
  const isDark = document.documentElement.dataset.mode === 'dark'
  return (
    <div ref={box} style={{ display: 'inline-flex', gap: 6, alignItems: 'center' }}>
      <button className="theme-mode-btn" title={isDark ? 'Chuyển sang giao diện sáng' : 'Chuyển sang giao diện tối'} aria-label="Đổi sáng / tối"
        onClick={() => change({ mode: isDark ? 'light' : 'dark' })}><Icon name={isDark ? 'sun' : 'moon'} size={18} /></button>
      <button className="theme-btn" aria-expanded={open} onClick={() => setOpen(o => !o)}><Icon name="palette" size={18} /> Giao diện</button>
      {open && (
        <div className="theme-pop" role="dialog" aria-label="Tùy chỉnh giao diện">
          <h4>Chế độ</h4>
          <Seg value={t.mode} options={MODES} onChange={mode => change({ mode })} />
          <h4>Kiểu màu</h4>
          <div className="skins">
            {SKINS.map(k => (
              <button key={k.id} className={`skin-card${k.id === t.skin ? ' on' : ''}`} aria-pressed={k.id === t.skin} onClick={() => change({ skin: k.id as Skin })}>
                <span className="skin-prev" style={{ ['--sw-acc' as string]: k.sw[1] }}><i style={{ background: k.sw[0] }} /><b style={{ background: 'rgba(128,128,128,.12)' }} /></span>
                <span>{k.label}</span>
              </button>))}
          </div>
          <h4>Thanh bên</h4>
          <Seg<Sidebar> value={t.sidebar} options={[{ id: 'dark', label: 'Đậm' }, { id: 'light', label: 'Sáng' }]} onChange={sidebar => change({ sidebar })} />
          <h4>Mật độ bảng</h4>
          <Seg<Density> value={t.density} options={[{ id: 'compact', label: 'Gọn' }, { id: 'normal', label: 'Vừa' }, { id: 'comfortable', label: 'Thoáng' }]} onChange={density => change({ density })} />
          <h4>Bo góc</h4>
          <Seg<Radius> value={t.radius} options={[{ id: 'sharp', label: 'Vuông' }, { id: 'normal', label: 'Vừa' }, { id: 'round', label: 'Tròn' }]} onChange={radius => change({ radius })} />
          <h4>Cỡ chữ</h4>
          <Seg<FontSize> value={t.font} options={[{ id: 'small', label: 'Nhỏ' }, { id: 'normal', label: 'Vừa' }, { id: 'large', label: 'Lớn' }]} onChange={font => change({ font })} />
          <p className="muted" style={{ marginTop: 10 }}>Lưu theo từng máy, không ảnh hưởng dữ liệu kế toán.</p>
        </div>)}
    </div>
  )
}
