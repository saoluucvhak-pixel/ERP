export const SKINS = [
  { id: 'misa', label: 'MISA xanh lá', sw: ['#1c2333', '#17a05d'] },
  { id: 'ocean', label: 'Đại dương', sw: ['#0b2a4d', '#1565c0'] },
  { id: 'luxe', label: 'Luxe navy + vàng', sw: ['#0f1b33', '#b8903f'] },
  { id: 'classic', label: 'Classic xanh ngọc', sw: ['#0f2740', '#0e7c86'] },
  { id: 'violet', label: 'Tím', sw: ['#26104a', '#6d28d9'] },
  { id: 'rose', label: 'Hồng', sw: ['#4a0f2a', '#be185d'] },
  { id: 'orange', label: 'Cam đất', sw: ['#3d1a08', '#c2410c'] },
  { id: 'forest', label: 'Rừng xanh', sw: ['#0f3320', '#166534'] },
  { id: 'champagne', label: 'Champagne', sw: ['#3a2f17', '#8a6a1f'] },
  { id: 'neutral', label: 'Xám xanh', sw: ['#1e293b', '#334155'] },
] as const
export type Skin = typeof SKINS[number]['id']
export type Mode = 'auto' | 'light' | 'dark'
export type Density = 'compact' | 'normal' | 'comfortable'
export type Radius = 'sharp' | 'normal' | 'round'
export type FontSize = 'small' | 'normal' | 'large'
export type Sidebar = 'dark' | 'light'

export interface Theme { skin: Skin; mode: Mode; density: Density; radius: Radius; font: FontSize; sidebar: Sidebar }

export const DEFAULT_THEME: Theme = { skin: 'misa', mode: 'auto', density: 'normal', radius: 'normal', font: 'normal', sidebar: 'dark' }
const KEY = 'hak.theme'

const pick = <T extends string>(v: unknown, allowed: readonly T[], fallback: T): T => (allowed.includes(v as T) ? (v as T) : fallback)

export function loadTheme(): Theme {
  try {
    const t = JSON.parse(localStorage.getItem(KEY) ?? '{}')
    return {
      skin: pick(t.skin, SKINS.map(s => s.id), DEFAULT_THEME.skin),
      mode: pick(t.mode, ['auto', 'light', 'dark'] as const, 'auto'),
      density: pick(t.density, ['compact', 'normal', 'comfortable'] as const, 'normal'),
      radius: pick(t.radius, ['sharp', 'normal', 'round'] as const, 'normal'),
      font: pick(t.font, ['small', 'normal', 'large'] as const, 'normal'),
      sidebar: pick(t.sidebar, ['dark', 'light'] as const, 'dark'),
    }
  } catch { return DEFAULT_THEME }
}

/** Đặt các thuộc tính data-* lên <html>; chế độ "auto" theo cài đặt hệ điều hành. */
export function applyTheme(t: Theme): void {
  const dark = t.mode === 'dark' || (t.mode === 'auto' && window.matchMedia?.('(prefers-color-scheme: dark)').matches)
  const d = document.documentElement.dataset
  d.skin = t.skin; d.mode = dark ? 'dark' : 'light'; d.density = t.density; d.radius = t.radius; d.font = t.font; d.sidebar = t.sidebar
}

export function saveTheme(t: Theme): void {
  try { localStorage.setItem(KEY, JSON.stringify(t)) } catch { /* trình duyệt chặn lưu: vẫn áp dụng cho phiên này */ }
  applyTheme(t)
}
