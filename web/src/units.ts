/** Quy đổi giữa đơn vị chính và đơn vị quy đổi cố định (1 đơn vị quy đổi = factor đơn vị chính). Số thập phân dạng chuỗi, tính bằng BigInt, 4 chữ số lẻ. */
const SCALE = 10000n
const DEC = /^-?\d+(\.\d{1,4})?$/

function scaled(v: string): bigint | null {
  if (!DEC.test(v)) return null
  const neg = v.startsWith('-'); const [i, f = ''] = (neg ? v.slice(1) : v).split('.')
  const n = BigInt(i) * SCALE + BigInt((f + '0000').slice(0, 4))
  return neg ? -n : n
}
function text(n: bigint): string {
  const neg = n < 0n; const abs = neg ? -n : n
  const frac = (abs % SCALE).toString().padStart(4, '0').replace(/0+$/, '')
  return (neg ? '-' : '') + (abs / SCALE).toString() + (frac ? '.' + frac : '')
}
const roundDiv = (a: bigint, b: bigint) => { const q = (a * 2n + (a < 0n ? -b : b)) / (b * 2n); return q }

/** Số lượng theo đơn vị quy đổi = số lượng chính ÷ hệ số (làm tròn nửa ra xa số 0). null nếu dữ liệu không hợp lệ. */
export function toAlt(main: string, factor: string): string | null {
  const m = scaled(main), f = scaled(factor)
  if (m == null || f == null || f <= 0n) return null
  return text(roundDiv(m * SCALE, f))
}

/** Số lượng theo đơn vị chính = số lượng quy đổi × hệ số. null nếu dữ liệu không hợp lệ. */
export function toMain(alt: string, factor: string): string | null {
  const a = scaled(alt), f = scaled(factor)
  if (a == null || f == null || f <= 0n) return null
  return text(roundDiv(a * f, SCALE))
}
