import { useCallback, useEffect, useState } from 'react'
import { get, put, type Period } from './api'
import { ErrorBox, useAction } from './common'
import { formatDate, formatMoney, parseMoney } from './money'

interface UsageRow { periodId: string; periodStart: string; periodEnd: string; quantity: string; cumulative: string; remaining: string | null; locked: boolean }
export interface UsageAsset { id: string; code: string; name: string; unitName: string | null; totalUnits: string | null }

/** Sản lượng thực tế theo kỳ của các thẻ phân bổ theo sản lượng. Kỳ nào chưa nhập thì không chạy khấu hao được; kỳ đã chạy thì khóa, hủy lần chạy mới sửa lại được. */
export function AssetUsagePanel({ assets, periods }: { assets: UsageAsset[]; periods: Period[] }) {
  const [assetId, setAssetId] = useState('')
  const [periodId, setPeriodId] = useState('')
  const [qty, setQty] = useState('')
  const [rows, setRows] = useState<UsageRow[]>([])
  const [info, setInfo] = useState('')
  const { busy, error, run, setError } = useAction()
  const asset = assets.find(a => a.id === assetId)
  const load = useCallback(() => { if (assetId) run(async () => setRows(await get<UsageRow[]>(`/assets/${assetId}/usage`))); else setRows([]) }, [run, assetId])
  // oxlint-disable-next-line react/set-state-in-effect -- tải sản lượng khi đổi tài sản
  useEffect(() => { load() }, [load])
  if (assets.length === 0) return null
  return (
    <section className="panel" aria-label="Sản lượng thực tế">
      <h3>Sản lượng thực tế (thẻ phân bổ theo sản lượng) <span className="muted">· nhập cho từng kỳ trước khi chạy khấu hao; nhập 0 nếu kỳ không phát sinh</span></h3>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      <div className="form-row">
        <label>Thẻ<select aria-label="Thẻ theo sản lượng" value={assetId} onChange={e => { setAssetId(e.target.value); setInfo('') }}><option value="">—</option>{assets.map(a => <option key={a.id} value={a.id}>{a.code} – {a.name}</option>)}</select></label>
        <label>Kỳ<select aria-label="Kỳ sản lượng" value={periodId} onChange={e => setPeriodId(e.target.value)}><option value="">—</option>{periods.filter(p => p.status === 'Open').map(p => <option key={p.id} value={p.id}>{p.startDate.slice(5, 7)}/{p.startDate.slice(0, 4)}</option>)}</select></label>
        <label>Sản lượng{asset?.unitName ? ` (${asset.unitName})` : ''}<input className="num" aria-label="Sản lượng kỳ" inputMode="decimal" value={qty} onChange={e => setQty(e.target.value)} /></label>
        <button className="primary" disabled={busy || !assetId || !periodId || !qty.trim()} onClick={() => {
          const q = parseMoney(qty)
          if (q == null) { setError(new Error('Sản lượng không hợp lệ.')); return }
          run(async () => { setRows(await put<UsageRow[]>(`/assets/${assetId}/usage`, { periodId, quantity: q })); setInfo('Đã lưu sản lượng kỳ.'); setQty('') })
        }}>Lưu sản lượng</button>
      </div>
      {asset?.totalUnits && <p className="muted">Tổng sản lượng dự kiến: {formatMoney(asset.totalUnits)} {asset.unitName ?? ''}</p>}
      <table className="grid"><thead><tr><th>Kỳ</th><th>Từ – đến</th><th className="num">Sản lượng kỳ</th><th className="num">Lũy kế</th><th className="num">Còn lại</th><th>Trạng thái</th></tr></thead>
        <tbody>{rows.map(r => <tr key={r.periodId}><td>{r.periodStart.slice(5, 7)}/{r.periodStart.slice(0, 4)}</td><td>{formatDate(r.periodStart)} – {formatDate(r.periodEnd)}</td><td className="num">{formatMoney(r.quantity)}</td>
          <td className="num">{formatMoney(r.cumulative)}</td><td className="num">{r.remaining == null ? '' : formatMoney(r.remaining)}</td><td>{r.locked ? 'Đã chạy khấu hao (khóa)' : 'Chưa chạy'}</td></tr>)}
          {rows.length === 0 && <tr><td colSpan={6} className="muted">{assetId ? 'Chưa nhập sản lượng kỳ nào.' : 'Chọn thẻ để xem và nhập sản lượng.'}</td></tr>}</tbody></table>
    </section>
  )
}
