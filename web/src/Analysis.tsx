import { useCallback, useEffect, useState } from 'react'
import { get } from './api'
import { ErrorBox, ExportButton, useAction } from './common'
import { today } from './money'

interface Ratio { group: string; name: string; unit: string; previous: number | null; current: number | null; formula: string }
interface Analysis { from: string; to: string; previousFrom: string; previousTo: string; templatesVerified: boolean; ratios: Ratio[] }

const fmt = (v: number | null) => (v == null ? '—' : v.toLocaleString('vi-VN', { minimumFractionDigits: 2, maximumFractionDigits: 2 }))
const d8 = (s: string) => s.split('-').reverse().join('/')
const monthStart = () => today().slice(0, 8) + '01'

/** Phân tích tài chính: các chỉ số tính từ B01-DN, B02-DN đã nhập cho kỳ này so với kỳ liền trước cùng độ dài (mã số chỉ tiêu theo mẫu). */
export function AnalysisView() {
  const [from, setFrom] = useState(monthStart)
  const [to, setTo] = useState(today)
  const [data, setData] = useState<Analysis | null>(null)
  const { busy, error, run } = useAction()
  const load = useCallback(() => run(async () => setData(await get<Analysis>(`/analysis/ratios?from=${from}&to=${to}`))), [run, from, to])
  useEffect(() => { load() }, [load])
  const groups = data ? [...new Set(data.ratios.map(r => r.group))] : []
  return (
    <>
      <div className="toolbar">
        <h2>Các chỉ số phân tích</h2>
        <label>Từ ngày<input type="date" value={from} onChange={e => setFrom(e.target.value)} /></label>
        <label>Đến ngày<input type="date" value={to} onChange={e => setTo(e.target.value)} /></label>
        <button onClick={() => load()} disabled={busy}>Xem</button>
        {data && <ExportButton fileName={`Chi_so_phan_tich_${from}_${to}`} sheet="Chỉ số" title="Các chỉ số phân tích tài chính" subtitle={`Kỳ này ${d8(data.from)}–${d8(data.to)}; kỳ trước ${d8(data.previousFrom)}–${d8(data.previousTo)}`}
          columns={[{ header: 'Chỉ số', width: 50 }, { header: 'ĐVT' }, { header: 'Kỳ trước', numeric: true }, { header: 'Kỳ này', numeric: true }, { header: 'Cách tính', width: 60 }]}
          rows={data.ratios.map(r => [r.name, r.unit, r.previous == null ? '' : String(r.previous), r.current == null ? '' : String(r.current), r.formula])} />}
      </div>
      <ErrorBox error={error} />
      {data && !data.templatesVerified && <div className="msg error">Mẫu B01-DN / B02-DN chưa được xác minh nên chỉ số chỉ để tham khảo.</div>}
      {data && <p className="muted">Kỳ này {d8(data.from)}–{d8(data.to)}, so với kỳ trước {d8(data.previousFrom)}–{d8(data.previousTo)}. Chỉ số lấy từ báo cáo theo mã số chỉ tiêu của mẫu; ô trống là chưa tính được (mẫu số bằng 0 hoặc kỳ chưa có số liệu). Vòng quay tính theo kỳ đã chọn, không quy năm.</p>}
      {data && groups.map(g => (
        <section key={g} className="panel">
          <h3 style={{ marginTop: 0 }}>{g}</h3>
          <table className="grid"><thead><tr><th>Tên</th><th>ĐVT</th><th className="num">Kỳ trước</th><th className="num">Kỳ này</th><th>+/−</th><th>Cách tính</th></tr></thead>
            <tbody>{data.ratios.filter(r => r.group === g).map(r => {
              const up = r.previous != null && r.current != null ? Math.sign(r.current - r.previous) : 0
              return <tr key={r.name}><td>{r.name}</td><td>{r.unit}</td><td className="num">{fmt(r.previous)}</td><td className="num">{fmt(r.current)}</td>
                <td style={{ color: up > 0 ? 'var(--ok)' : up < 0 ? 'var(--danger)' : undefined }}>{up > 0 ? '▲' : up < 0 ? '▼' : ''}</td><td className="muted">{r.formula}</td></tr>
            })}</tbody></table>
        </section>))}
    </>
  )
}
