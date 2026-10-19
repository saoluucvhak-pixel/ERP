/** Biểu đồ SVG nhỏ cho màn Tổng quan (không dùng thư viện). Số liệu chỉ để trực quan; số chính xác nằm trong bảng và thẻ. */
export const CHART_COLORS = ['#1a9a4a', '#f0832e', '#3b82f6', '#e05a77', '#8b5cf6', '#14b8a6']

const W = 640, H = 230, L = 46, R = 12, T = 12, B = 28

function niceMax(v: number) {
  if (v <= 0) return 1
  const p = Math.pow(10, Math.floor(Math.log10(v)))
  const n = v / p
  return (n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10) * p
}
const tick = (v: number) => (Math.abs(v) >= 1000 ? v.toLocaleString('vi-VN', { maximumFractionDigits: 0 }) : v.toLocaleString('vi-VN', { maximumFractionDigits: 1 }))

function Axes({ labels, min, max }: { labels: string[]; min: number; max: number }) {
  const y = (v: number) => T + (H - T - B) * (1 - (v - min) / (max - min || 1))
  const ticks = [0, 1, 2, 3, 4].map(i => min + ((max - min) * i) / 4)
  const band = (W - L - R) / labels.length
  return <>
    {ticks.map((t, i) => <g key={i}><line x1={L} x2={W - R} y1={y(t)} y2={y(t)} stroke="var(--line)" strokeDasharray="3 3" /><text x={L - 6} y={y(t) + 4} textAnchor="end" fontSize="11" fill="var(--muted)">{tick(t)}</text></g>)}
    {labels.map((l, i) => <text key={l} x={L + band * i + band / 2} y={H - 8} textAnchor="middle" fontSize="11" fill="var(--muted)">{l}</text>)}
  </>
}

export interface Series { name: string; color: string; values: number[] }

export function BarChart({ labels, series, label }: { labels: string[]; series: Series[]; label: string }) {
  const all = series.flatMap(s => s.values)
  const max = niceMax(Math.max(0, ...all)); const min = Math.min(0, ...all) < 0 ? -niceMax(-Math.min(...all)) : 0
  const y = (v: number) => T + (H - T - B) * (1 - (v - min) / (max - min || 1))
  const band = (W - L - R) / labels.length; const bw = Math.min(26, (band - 8) / series.length)
  return (
    <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label={label} style={{ width: '100%', height: 'auto' }}>
      <Axes labels={labels} min={min} max={max} />
      {labels.map((_, i) => series.map((s, j) => {
        const v = s.values[i] ?? 0; const x = L + band * i + band / 2 - (bw * series.length) / 2 + bw * j
        return <rect key={`${i}-${j}`} x={x} y={Math.min(y(v), y(0))} width={bw - 2} height={Math.abs(y(v) - y(0))} fill={s.color} rx="2"><title>{`${s.name} ${labels[i]}: ${tick(v)}`}</title></rect>
      }))}
    </svg>
  )
}

export function LineChart({ labels, series, label }: { labels: string[]; series: Series[]; label: string }) {
  const all = series.flatMap(s => s.values)
  const max = niceMax(Math.max(0, ...all)); const min = Math.min(0, ...all) < 0 ? -niceMax(-Math.min(...all)) : 0
  const y = (v: number) => T + (H - T - B) * (1 - (v - min) / (max - min || 1))
  const band = (W - L - R) / labels.length
  return (
    <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label={label} style={{ width: '100%', height: 'auto' }}>
      <Axes labels={labels} min={min} max={max} />
      {series.map(s => <g key={s.name}>
        <polyline fill="none" stroke={s.color} strokeWidth="2" points={s.values.map((v, i) => `${L + band * i + band / 2},${y(v)}`).join(' ')} />
        {s.values.map((v, i) => <circle key={i} cx={L + band * i + band / 2} cy={y(v)} r="3.5" fill="var(--panel)" stroke={s.color} strokeWidth="2"><title>{`${s.name} ${labels[i]}: ${tick(v)}`}</title></circle>)}
      </g>)}
    </svg>
  )
}

export function Donut({ parts, label }: { parts: { name: string; value: number; color: string }[]; label: string }) {
  const total = parts.reduce((n, p) => n + p.value, 0)
  const r = 70, c = 2 * Math.PI * r
  let offset = 0
  return (
    <svg viewBox="0 0 200 200" role="img" aria-label={label} style={{ width: 200, height: 200 }}>
      <circle cx="100" cy="100" r={r} fill="none" stroke="var(--line)" strokeWidth="34" />
      {total > 0 && parts.map(p => {
        const len = (p.value / total) * c; const el = <circle key={p.name} cx="100" cy="100" r={r} fill="none" stroke={p.color} strokeWidth="34" strokeDasharray={`${len} ${c - len}`} strokeDashoffset={-offset} transform="rotate(-90 100 100)"><title>{`${p.name}: ${tick(p.value)}`}</title></circle>
        offset += len; return el
      })}
    </svg>
  )
}

export function Legend({ items }: { items: { name: string; color: string }[] }) {
  return <div style={{ display: 'flex', gap: 14, flexWrap: 'wrap', justifyContent: 'center', fontSize: 12, marginTop: 4 }}>
    {items.map(i => <span key={i.name} style={{ display: 'inline-flex', alignItems: 'center', gap: 5 }}><span style={{ width: 10, height: 10, borderRadius: 999, background: i.color, display: 'inline-block' }} />{i.name}</span>)}
  </div>
}
