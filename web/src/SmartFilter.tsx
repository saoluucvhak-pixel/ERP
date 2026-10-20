import { useCallback, useEffect, useState } from 'react'
import { datePresets } from './filter'
import { monthStart, today } from './money'
export { datePresets, inRange, overlaps } from './filter'

export interface FilterState { q: string; sel: Record<string, string>; chip: string | null; from: string; to: string }
export interface FilterSelect { key: string; label: string; options: { value: string; label: string }[] }
export interface FilterChip { key: string; label: string; count?: number }

/** Khoảng ngày mặc định: ngày đầu tháng hiện hành đến ngày hiện hành (tính lại mỗi lần mở màn, không lưu). */
const fresh = (): FilterState => ({ q: '', sel: {}, chip: null, from: monthStart(), to: today() })

/** Trạng thái bộ lọc, nhớ theo từng màn hình trên máy này (localStorage; không có thì vẫn chạy bình thường). */
export function useFilterState(id: string): [FilterState, (patch: Partial<FilterState>) => void, () => void] {
  const key = `hak.filter.${id}`
  const [state, setState] = useState<FilterState>(() => {
    try { const raw = localStorage.getItem(key); if (raw) { const { q, sel, chip } = JSON.parse(raw); return { ...fresh(), q: q ?? '', sel: sel ?? {}, chip: chip ?? null } } } catch { /* bỏ qua */ }
    return fresh()
  })
  useEffect(() => { try { localStorage.setItem(key, JSON.stringify({ q: state.q, sel: state.sel, chip: state.chip })) } catch { /* bỏ qua */ } }, [key, state.q, state.sel, state.chip])
  const patch = useCallback((p: Partial<FilterState>) => setState(s => ({ ...s, ...p })), [])
  const clear = useCallback(() => setState(fresh()), [])
  return [state, patch, clear]
}

export const isFiltering = (s: FilterState, withDates = false) => !!s.q.trim() || !!s.chip || Object.values(s.sel).some(Boolean) || (withDates && (s.from !== monthStart() || s.to !== today()))



/**
 * Thanh lọc dùng chung: ô tìm thông minh (không dấu, nhiều từ, "cụm", so sánh tiền như >500tr), danh sách chọn, nút lọc nhanh có số đếm,
 * nút Xóa lọc và bộ đếm kết quả. Việc lọc dữ liệu do màn hình gọi (matchesQuery trong filter.ts).
 */
export function SmartFilter({ label, state, patch, clear, selects = [], chips = [], shown, total, placeholder, hint, dateLabel, noSearch }: {
  dateLabel?: string; noSearch?: boolean; label: string; state: FilterState; patch: (p: Partial<FilterState>) => void; clear: () => void
  selects?: FilterSelect[]; chips?: FilterChip[]; shown: number; total: number; placeholder?: string; hint?: string
}) {
  return (
    <div className="smart-filter" role="search" aria-label={label}>
      <div className="sf-row">
        {!noSearch && <label className="sf-search">
          <span className="sf-icon" aria-hidden>🔎</span>
          <input type="search" aria-label={`Tìm trong ${label.toLowerCase()}`} value={state.q} placeholder={placeholder ?? 'Tìm: nhiều từ, không cần dấu, ví dụ >500tr'}
            title={hint ?? 'Gõ nhiều từ để lọc (không cần dấu); "cụm từ" để khớp nguyên cụm; >500tr, <=2ty, =100k để lọc theo số tiền.'}
            onChange={e => patch({ q: e.target.value })} />
        </label>}
        {selects.map(s => <label key={s.key} className="sf-select">{s.label}
          <select aria-label={`Lọc theo ${s.label.toLowerCase()}`} value={state.sel[s.key] ?? ''} onChange={e => patch({ sel: { ...state.sel, [s.key]: e.target.value } })}>
            <option value="">Tất cả</option>{s.options.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
          </select></label>)}
        <span className="sf-count muted" aria-live="polite">{shown}/{total}</span>
        {isFiltering(state, !!dateLabel) && <button type="button" className="sf-clear" onClick={clear}>Xóa lọc</button>}
      </div>
      {dateLabel && <div className="sf-dates" role="group" aria-label={`Khoảng ngày: ${dateLabel}`}>
        <label className="sf-date">Từ ngày<input type="date" aria-label="Từ ngày" value={state.from} max={state.to || undefined} onChange={e => patch({ from: e.target.value })} /></label>
        <label className="sf-date">Đến ngày<input type="date" aria-label="Đến ngày" value={state.to} min={state.from || undefined} onChange={e => patch({ to: e.target.value })} /></label>
        <span className="sf-presets">{(() => { const all = datePresets(); const hit = all.find(x => x.from === state.from && x.to === state.to)?.key; return all.map(p => <button type="button" key={p.key} className={`sf-chip sf-preset${hit === p.key ? ' on' : ''}`} onClick={() => patch({ from: p.from, to: p.to })}>{p.label}</button>) })()}</span>
        <span className="muted sf-hint">Theo {dateLabel}</span>
      </div>}
      {chips.length > 0 && <div className="sf-chips" role="group" aria-label="Lọc nhanh">
        {chips.map(c => <button type="button" key={c.key} className={`sf-chip${state.chip === c.key ? ' on' : ''}`} aria-pressed={state.chip === c.key}
          onClick={() => patch({ chip: state.chip === c.key ? null : c.key })}>{c.label}{c.count != null ? <span className="sf-n">{c.count}</span> : null}</button>)}
      </div>}
    </div>
  )
}
