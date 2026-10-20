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
