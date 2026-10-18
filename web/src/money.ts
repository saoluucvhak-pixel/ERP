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

/** Accepts "1.234.567,5", "1234567,5" or "1234567.5" and returns the canonical "1234567.5"; null if invalid. */
export function parseMoney(input: string): string | null {
  const s = input.trim().replace(/\s/g, '')
  if (s === '') return null
  let canonical: string
  if (s.includes(',')) canonical = s.replace(/\./g, '').replace(',', '.')
  else if (/^\d{1,3}(\.\d{3})+$/.test(s)) canonical = s.replace(/\./g, '')
  else canonical = s
  if (!/^\d+(\.\d{1,4})?$/.test(canonical)) return null
  return canonical.replace(/^0+(?=\d)/, '')
}

/** Exchange rate (functional units per foreign unit, up to 6 decimals), same separators as parseMoney; null if invalid. */
export function parseRate(input: string): string | null {
  const s = input.trim().replace(/\s/g, '')
  let canonical: string
  if (s.includes(',')) canonical = s.replace(/\./g, '').replace(',', '.')
  else if (/^\d{1,3}(\.\d{3})+$/.test(s)) canonical = s.replace(/\./g, '')
  else canonical = s
  if (!/^\d+(\.\d{1,6})?$/.test(canonical)) return null
  return canonical.replace(/^0+(?=\d)/, '')
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
/** "2026-10-18" → "18/10/2026" (Việt Nam). Chuỗi không phải ngày được trả về nguyên văn. */
export function formatDate(value: string | null | undefined): string {
  if (!value) return ''
  const m = /^(\d{4})-(\d{2})-(\d{2})(?:$|T)/.exec(value)
  return m ? `${m[3]}/${m[2]}/${m[1]}` : value
}

/** Thời điểm UTC từ máy chủ → "18/10/2026 14:05" theo giờ máy. */
export function formatDateTime(value: string | null | undefined): string {
  if (!value) return ''
  const d = new Date(value)
  if (Number.isNaN(d.getTime())) return value
  const p = (n: number) => String(n).padStart(2, '0')
  return `${p(d.getDate())}/${p(d.getMonth() + 1)}/${d.getFullYear()} ${p(d.getHours())}:${p(d.getMinutes())}`
}

/** "18/10/2026", "18-10-26", "181026" … → "2026-10-18"; null nếu không phải ngày hợp lệ. Năm 2 chữ số hiểu là 20xx. */
export function parseDate(input: string): string | null {
  const s = input.trim()
  if (s === '') return null
  let d: string, m: string, y: string
  const sep = /^(\d{1,2})[/.\-](\d{1,2})[/.\-](\d{2}|\d{4})$/.exec(s)
  const flat = /^(\d{2})(\d{2})(\d{4})$/.exec(s)
  const iso = /^(\d{4})-(\d{2})-(\d{2})$/.exec(s)
  if (sep) [, d, m, y] = sep
  else if (flat) [, d, m, y] = flat
  else if (iso) [, y, m, d] = iso
  else return null
  if (y.length === 2) y = '20' + y
  const dt = new Date(Number(y), Number(m) - 1, Number(d))
  if (dt.getFullYear() !== Number(y) || dt.getMonth() !== Number(m) - 1 || dt.getDate() !== Number(d)) return null
  return `${y}-${m.padStart(2, '0')}-${d.padStart(2, '0')}`
}

/** Exact comparison of decimal strings (up to 4 fraction digits) via BigInt; non-numeric input sorts as 0. */
export function compareMoney(a: string | null | undefined, b: string | null | undefined): number {
  const scaled = (v: string | null | undefined): bigint => {
    if (!v || !DEC.test(v)) return 0n
    const neg = v.startsWith('-')
    const [i, f = ''] = (neg ? v.slice(1) : v).split('.')
    const n = BigInt(i) * 10000n + BigInt((f + '0000').slice(0, 4))
    return neg ? -n : n
  }
  const x = scaled(a), y = scaled(b)
  return x < y ? -1 : x > y ? 1 : 0
}
