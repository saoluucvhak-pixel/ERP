import qrcode from 'qrcode-generator'

/** Mã QR dạng SVG (mức sửa lỗi M — nhãn dán ngoài xưởng có thể trầy xước), tự chọn cỡ nhỏ nhất đủ chứa nội dung. */
export function qrSvg(text: string, cellSize = 4, margin = 2): string {
  const qr = qrcode(0, 'M')
  qr.addData(text, 'Byte')
  qr.make()
  return qr.createSvgTag({ cellSize, margin, scalable: true })
}

const PUBLIC_KEY = 'hak.publicUrl'

/** Địa chỉ máy chủ mà điện thoại truy cập được (nhớ trên máy này); mặc định là địa chỉ đang mở trang. */
export function getPublicUrl(): string {
  try { const v = localStorage.getItem(PUBLIC_KEY); if (v) return v } catch { /* bỏ qua */ }
  return window.location.origin
}
export function setPublicUrl(v: string) {
  try { if (v.trim()) localStorage.setItem(PUBLIC_KEY, v.trim().replace(/\/+$/, '')); else localStorage.removeItem(PUBLIC_KEY) } catch { /* bỏ qua */ }
}
/** localhost / 127.x không dùng được trên điện thoại. */
export const isLocalAddress = (u: string) => /^https?:\/\/(localhost|127\.|\[::1\]|0\.0\.0\.0)/i.test(u)

/** Địa chỉ mở trang di động của một tài sản: quét QR bằng camera điện thoại sẽ mở đúng trang báo sự cố của máy này. */
export const assetUrl = (base: string, code: string) => `${base.replace(/\/+$/, '')}/#/m/a/${encodeURIComponent(code)}`

/** Mã tài sản từ nội dung quét được (địa chỉ đầy đủ hoặc chính mã); null nếu không nhận ra. */
export function assetCodeFromScan(text: string): string | null {
  const t = text.trim()
  const m = /#\/m\/a\/([^/?#\s]+)/.exec(t)
  if (m) { try { return decodeURIComponent(m[1]) } catch { return m[1] } }
  return /^[\w.\-/]{1,40}$/.test(t) ? t : null
}
