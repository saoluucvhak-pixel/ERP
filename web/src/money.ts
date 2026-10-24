// Money stays a decimal string end-to-end (master prompt §28). These helpers never convert to Number.

const DEC = /^-?\d+(\.\d+)?$/

/** "1234567.5" → "1.234.567,5" (Vietnamese grouping). Non-decimal input is returned unchanged. */
export function formatMoney(value: string | null | undefined): string {
  if (value == null || value === '') return ''
  if (!DEC.test(value)) return value
  const neg = value.startsWith('-')
  const [int, frac] = (neg ? value.slice(1) : value).split('.')
  const grouped = int.replace(/\B(?=(\d{3})+(?!\d))/g, '.')
  const trimmed = frac?.replace(/0+$/, '')
  return (neg ? '-' : '') + grouped + (trimmed ? ',' + trimmed : '')
}

/**
 * Strict Vietnamese/canonical decimal parser. Accepted shapes only:
 *  - "1.234.567,5" (dot = thousands in groups of exactly 3, comma = decimal)
 *  - "1234567,5" / "1234567.5" (plain digits with one decimal separator)
 *  - "1.234.567" (dot groups of 3 = thousands) and "12 000" (space groups of 3)
 * Anything ambiguous or malformed ("1.2.3,45", "12.34,56", "1,234.56", "1,2,3") returns null.
 */
function parseDecimal(input: string, maxFrac: number): string | null {
  const t = input.trim()
  if (t === '') return null
  let int: string
  let frac = ''
  let m: RegExpMatchArray | null
  if (/\s/.test(t)) {
    m = t.match(/^(\d{1,3}(?:\s\d{3})+)(?:[.,](\d+))?$/)
    if (!m) return null
    int = m[1].replace(/\s/g, '')
    frac = m[2] ?? ''
  } else if (t.includes(',')) {
    m = t.match(/^(\d{1,3}(?:\.\d{3})+|\d+),(\d+)$/)
    if (!m) return null
    int = m[1].replace(/\./g, '')
    frac = m[2]
  } else if (/^\d{1,3}(?:\.\d{3})+$/.test(t)) {
    int = t.replace(/\./g, '')
  } else {
    m = t.match(/^(\d+)(?:\.(\d+))?$/)
    if (!m) return null
    int = m[1]
    frac = m[2] ?? ''
  }
  if (frac.length > maxFrac) return null
  return int.replace(/^0+(?=\d)/, '') + (frac ? '.' + frac : '')
}

/** Money amount (up to 4 decimals); returns the canonical "1234567.5" or null if invalid. */
export function parseMoney(input: string): string | null {
  return parseDecimal(input, 4)
}

/** Exchange rate (functional units per foreign unit, up to 6 decimals), same grammar as parseMoney; null if invalid. */
export function parseRate(input: string): string | null {
  return parseDecimal(input, 6)
}

/** Số nguyên không âm thuần chữ số ("30abc", "1e3", "-5", "3.5" → null; trống → null). parseInt sẽ lặng lẽ đọc "30abc" thành 30. */
export function parseWholeNumber(input: string): number | null {
  const t = input.trim()
  return /^\d{1,9}$/.test(t) ? Number(t) : null
}

/** Exact decimal addition on strings (up to 4 fraction digits) using BigInt. */
export function addMoney(values: string[]): string {
  const SCALE = 10000n
  let total = 0n
  for (const v of values) {
    if (!v || !DEC.test(v)) continue
    const neg = v.startsWith('-')
    const [i, f = ''] = (neg ? v.slice(1) : v).split('.')
    const scaled = BigInt(i) * SCALE + BigInt((f + '0000').slice(0, 4))
    total += neg ? -scaled : scaled
  }
  const neg = total < 0n
  const abs = neg ? -total : total
  const frac = (abs % SCALE).toString().padStart(4, '0').replace(/0+$/, '')
  return (neg ? '-' : '') + (abs / SCALE).toString() + (frac ? '.' + frac : '')
}

/** Local calendar date (yyyy-mm-dd). Not toISOString(): that is UTC and gives yesterday before 07:00 in Vietnam. */
export const today = () => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/** Ngày hiển thị kiểu Việt: 2026-10-19 → 19/10/2026 (cả thời điểm ISO có giờ, đổi sang giờ địa phương). Chuỗi không phải ngày giữ nguyên. */
export function formatDate(value: string | null | undefined): string {
  if (!value) return ''
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)
  if (m) return `${m[3]}/${m[2]}/${m[1]}`
  if (/^\d{4}-\d{2}-\d{2}T/.test(value)) {
    const d = new Date(value)
    if (!Number.isNaN(d.getTime())) return `${String(d.getDate()).padStart(2, '0')}/${String(d.getMonth() + 1).padStart(2, '0')}/${d.getFullYear()}`
  }
  return value
}

/** Thời điểm hiển thị kiểu Việt: ISO (UTC) → 19/10/2026 14:30 theo giờ địa phương. */
export function formatDateTime(value: string | null | undefined): string {
  if (!value) return ''
  const d = new Date(value)
  if (!/^\d{4}-\d{2}-\d{2}T/.test(value) || Number.isNaN(d.getTime())) return formatDate(value)
  const p = (n: number) => String(n).padStart(2, '0')
  return `${p(d.getDate())}/${p(d.getMonth() + 1)}/${d.getFullYear()} ${p(d.getHours())}:${p(d.getMinutes())}`
}

/** Tháng hiển thị kiểu Việt: 2026-10-19 hoặc 2026-10 → 10/2026. */
export function formatMonth(value: string | null | undefined): string {
  const m = value ? /^(\d{4})-(\d{2})/.exec(value) : null
  return m ? `${m[2]}/${m[1]}` : (value ?? '')
}

/** Ngày đầu tháng hiện hành (yyyy-mm-dd, giờ địa phương) — mặc định "từ ngày" của mọi bộ lọc theo khoảng ngày. */
export const monthStart = () => today().slice(0, 8) + '01'

const DIGITS = ['không', 'một', 'hai', 'ba', 'bốn', 'năm', 'sáu', 'bảy', 'tám', 'chín']
const SCALES = ['', 'nghìn', 'triệu', 'tỷ', 'nghìn tỷ', 'triệu tỷ', 'tỷ tỷ']

function readGroup(n: number, leading: boolean): string {
  const h = Math.floor(n / 100), t = Math.floor(n / 10) % 10, u = n % 10
  const words: string[] = []
  if (!leading || h > 0) words.push(DIGITS[h], 'trăm')
  if (t === 0) { if (u > 0 && words.length > 0) words.push('linh') }
  else if (t === 1) words.push('mười')
  else words.push(DIGITS[t], 'mươi')
  if (u === 1 && t > 1) words.push('mốt')
  else if (u === 5 && t > 0) words.push('lăm')
  else if (u > 0) words.push(DIGITS[u])
  return words.join(' ')
}

/**
 * Amount in Vietnamese words for printed documents ("Bằng chữ"): "1250000" → "Một triệu hai trăm năm mươi nghìn đồng".
 * Works on the decimal string (no Number), integer part only; a fractional part is read digit by digit after "phẩy".
 */
export function amountInWords(value: string, unit = 'đồng'): string {
  if (!DEC.test(value)) return ''
  const neg = value.startsWith('-')
  const [intRaw, fracRaw] = (neg ? value.slice(1) : value).split('.')
  const int = intRaw.replace(/^0+(?=\d)/, '')
  const frac = (fracRaw ?? '').replace(/0+$/, '')
  if (int.length > 21) return ''
  const groups: number[] = []
  for (let end = int.length; end > 0; end -= 3) groups.unshift(Number(int.slice(Math.max(0, end - 3), end)))
  const parts: string[] = []
  groups.forEach((g, i) => {
    if (g === 0) return
    const scale = SCALES[groups.length - 1 - i]
    parts.push(readGroup(g, parts.length === 0) + (scale ? ' ' + scale : ''))
  })
  let text = parts.length ? parts.join(' ') : 'không'
  if (frac) text += ' phẩy ' + [...frac].map(d => DIGITS[Number(d)]).join(' ')
  text = (neg ? 'âm ' : '') + text + (unit ? ' ' + unit : '')
  return text.charAt(0).toUpperCase() + text.slice(1)
}
/**
 * Ô số tùy chọn (thuế suất, dung sai): để trống là "không có" (null); đã nhập mà sai định dạng thì báo lỗi
 * thay vì lặng lẽ coi như để trống — nếu không, "10%" sẽ lưu hóa đơn với thuế 0.
 */
export function optionalRate(input: string, label = 'Thuế suất'): string | null {
  if (input.trim() === '') return null
  const parsed = parseMoney(input)
  if (parsed == null) throw new Error(`${label} "${input.trim()}" không hợp lệ: chỉ nhập số (ví dụ 10 hoặc 8,5), không kèm ký hiệu %.`)
  return parsed
}

/** Ngày yyyy-MM-dd lùi `days` ngày, hoặc null nếu chuỗi vào không phải ngày hợp lệ (ô ngày bị xóa trống). */
export function daysBefore(iso: string, days: number): string | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso)
  if (!m) return null
  const t = Date.UTC(Number(m[1]), Number(m[2]) - 1, Number(m[3]))
  if (Number.isNaN(t)) return null
  return new Date(t - days * 86_400_000).toISOString().slice(0, 10)
}

/** Ô CSV: bọc ngoặc kép và vô hiệu hóa công thức (ô bắt đầu bằng = + - @ hoặc ký tự điều khiển sẽ chạy như công thức trong Excel). */
export function csvCell(text: string): string {
  const safe = /^[=+\-@\t\r]/.test(text) ? `'${text}` : text
  return `"${safe.replace(/"/g, '""')}"`
}
