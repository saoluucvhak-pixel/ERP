/** Bộ lọc thông minh: tìm không phân biệt hoa thường và dấu tiếng Việt, nhiều từ (và), cụm trong ngoặc kép, so sánh số tiền (>500tr, <=2ty, =100000). Chỉ dùng để lọc hiển thị, không dùng để tính sổ. */

export function fold(s: string): string {
  return s.normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/đ/g, 'd').replace(/Đ/g, 'd').toLowerCase()
}

export interface AmountCond { op: '>' | '<' | '>=' | '<=' | '='; value: number }
export interface ParsedQuery { words: string[]; amounts: AmountCond[] }

const UNITS: Record<string, number> = { k: 1e3, nghin: 1e3, tr: 1e6, m: 1e6, trieu: 1e6, ty: 1e9, b: 1e9 }

/** ">500tr" → {op:'>', value:500000000}; trả null nếu không phải điều kiện số. */
export function parseAmount(token: string): AmountCond | null {
  const m = /^(>=|<=|>|<|=)(\d+(?:[.,]\d+)?)(k|nghin|tr|m|trieu|ty|b)?$/.exec(fold(token))
  if (!m) return null
  const n = Number(m[2].replace(',', '.'))
  return { op: m[1] as AmountCond['op'], value: n * (m[3] ? UNITS[m[3]] : 1) }
}

export function parseQuery(q: string): ParsedQuery {
  const words: string[] = []; const amounts: AmountCond[] = []
  const re = /"([^"]+)"|(\S+)/g
  let m: RegExpExecArray | null
  while ((m = re.exec(q))) {
    if (m[1]) { words.push(fold(m[1].trim())); continue }
    const cond = parseAmount(m[2])
    if (cond) amounts.push(cond); else words.push(fold(m[2]))
  }
  return { words: words.filter(Boolean), amounts }
}

function holds(c: AmountCond, v: number): boolean {
  switch (c.op) { case '>': return v > c.value; case '<': return v < c.value; case '>=': return v >= c.value; case '<=': return v <= c.value; default: return v === c.value }
}

/** Khớp khi mọi từ đều có trong haystack và mọi điều kiện số đều đúng (điều kiện số cần có `amount`). */
export function matchesQuery(haystack: string, amount: number | null, q: string): boolean {
  const p = parseQuery(q)
  if (p.words.length === 0 && p.amounts.length === 0) return true
  const h = fold(haystack)
  if (!p.words.every(w => h.includes(w))) return false
  if (p.amounts.length && amount == null) return false
  return p.amounts.every(c => holds(c, amount as number))
}

export function toNumber(s: string | null | undefined): number | null {
  if (s == null || s === '') return null
  const n = Number(s)
  return Number.isFinite(n) ? n : null
}

/** Ngày yyyy-mm-dd nằm trong khoảng đã chọn (bỏ trống một đầu = không giới hạn đầu đó). */
export const inRange = (s: { from: string; to: string }, date: string | null | undefined) => {
  if (!date) return false
  // Thời điểm có giờ (ISO, UTC) đổi sang ngày theo giờ địa phương; ngày thuần giữ nguyên.
  const d = date.length > 10 ? localDay(date) : date
  return (!s.from || d >= s.from) && (!s.to || d <= s.to)
}

/** Hai khoảng giao nhau (hợp đồng có hiệu lực trong khoảng đã chọn). */
export const overlaps = (s: { from: string; to: string }, start: string, end: string) => (!s.to || start <= s.to) && (!s.from || end >= s.from)

function localDay(iso: string): string { const d = new Date(iso); return Number.isNaN(d.getTime()) ? iso.slice(0, 10) : ymd(d) }
const ymd = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
/** Các mốc chọn nhanh theo lịch địa phương. */
export function datePresets(now = new Date()): { key: string; label: string; from: string; to: string }[] {
  const y = now.getFullYear(), m = now.getMonth(), d = now.getDate(), dow = (now.getDay() + 6) % 7
  const q = Math.floor(m / 3) * 3
  return [
    { key: 'today', label: 'Hôm nay', from: ymd(now), to: ymd(now) },
    { key: 'week', label: 'Tuần này', from: ymd(new Date(y, m, d - dow)), to: ymd(now) },
    { key: 'month', label: 'Tháng này', from: ymd(new Date(y, m, 1)), to: ymd(now) },
    { key: 'lastmonth', label: 'Tháng trước', from: ymd(new Date(y, m - 1, 1)), to: ymd(new Date(y, m, 0)) },
    { key: 'quarter', label: 'Quý này', from: ymd(new Date(y, q, 1)), to: ymd(now) },
    { key: 'year', label: 'Năm nay', from: ymd(new Date(y, 0, 1)), to: ymd(now) },
    { key: 'all', label: 'Tất cả ngày', from: '', to: '' },
  ]
}
