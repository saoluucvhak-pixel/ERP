export const SKINS = [
  { id: 'luxe', label: 'Luxe (navy + champagne)' },
  { id: 'classic', label: 'Classic (xanh ngọc)' },
  { id: 'neutral', label: 'Neutral (xám xanh)' },
  { id: 'champagne', label: 'Champagne (vàng ấm)' },
] as const
export type Skin = typeof SKINS[number]['id']
export type Mode = 'auto' | 'light' | 'dark'

export interface Theme { skin: Skin; mode: Mode }

const KEY = 'hak.theme'

export function loadTheme(): Theme {
  try {
    const t = JSON.parse(localStorage.getItem(KEY) ?? '{}')
    return {
      skin: SKINS.some(s => s.id === t.skin) ? t.skin : 'luxe',
      mode: t.mode === 'light' || t.mode === 'dark' ? t.mode : 'auto',
    }
  } catch { return { skin: 'luxe', mode: 'auto' } }
}

/** Đặt data-skin/data-mode lên <html>; chế độ "auto" theo cài đặt hệ điều hành. */
export function applyTheme(t: Theme): void {
  const dark = t.mode === 'dark' || (t.mode === 'auto' && window.matchMedia?.('(prefers-color-scheme: dark)').matches)
  document.documentElement.dataset.skin = t.skin
  document.documentElement.dataset.mode = dark ? 'dark' : 'light'
}

export function saveTheme(t: Theme): void {
  try { localStorage.setItem(KEY, JSON.stringify(t)) } catch { /* trình duyệt chặn lưu: vẫn áp dụng cho phiên này */ }
  applyTheme(t)
}
