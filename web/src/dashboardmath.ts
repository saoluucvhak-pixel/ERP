// Display helpers for the dashboard bars; money stays a decimal string (BigInt compare, no floats for amounts).

export const abs = (v: string) => (v.startsWith('-') ? v.slice(1) : v)
const scaled = (s: string) => { const [i, f = ''] = abs(s).split('.'); return BigInt(i) * 10000n + BigInt((f + '0000').slice(0, 4)) }
/** Largest absolute value among decimal strings (BigInt compare), as a string. */
export function maxAbs(values: string[]): string {
  let best = '0'
  for (const v of values) if (scaled(v) > scaled(best)) best = abs(v)
  return best
}
/** Bar width in % from two decimal strings, via BigInt (display only; money itself never leaves string form). */
export function pct(v: string, max: string): number {
  const m = scaled(max)
  return m === 0n ? 0 : Number((scaled(v) * 1000n) / m) / 10
}

