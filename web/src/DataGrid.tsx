import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { DateInput } from './common'
import { addMoney, compareMoney, formatDate, formatMoney, parseMoney, today } from './money'

export type ColType = 'text' | 'number' | 'date' | 'select'

export interface Col<T> {
  key: string
  label: string
  type: ColType
  /** Raw value used to filter and sort: text, decimal string (number), "yyyy-mm-dd" (date). */
  value: (row: T) => string | null | undefined
  /** Display text for select columns and for the quick search (defaults to the value, dates dd/MM/yyyy, numbers 1.234,5). */
  display?: (row: T) => string
  render?: (row: T) => ReactNode
  headerRender?: () => ReactNode
  sum?: boolean
  noFilter?: boolean
}

interface Cond { col: string; op: string; a: string; b: string }
interface SavedFilter { name: string; mode: 'and' | 'or'; q: string; conds: Cond[] }

const OPS: Record<ColType, [string, string][]> = {
  text: [['contains', 'chứa'], ['notContains', 'không chứa'], ['starts', 'bắt đầu bằng'], ['eq', 'bằng']],
  number: [['eq', '='], ['gt', '>'], ['gte', '≥'], ['lt', '<'], ['lte', '≤'], ['between', 'trong khoảng']],
  date: [['preset', 'khoảng nhanh'], ['between', 'trong khoảng'], ['eq', 'đúng ngày'], ['before', 'trước ngày'], ['after', 'sau ngày']],
  select: [['eq', 'là'], ['neq', 'không phải']],
}
const PRESETS: [string, string][] = [['today', 'Hôm nay'], ['thisMonth', 'Tháng này'], ['lastMonth', 'Tháng trước'], ['thisQuarter', 'Quý này'], ['thisYear', 'Năm nay']]
const pad = (n: number) => String(n).padStart(2, '0')
const iso = (y: number, m: number, d: number) => `${y}-${pad(m)}-${pad(d)}`

/** Date range [from, to] (inclusive, ISO) for a preset, relative to today. */
export function presetRange(preset: string, now = today()): [string, string] {
  const [y, m] = now.split('-').map(Number)
  const lastDay = (yy: number, mm: number) => new Date(yy, mm, 0).getDate()
  switch (preset) {
    case 'today': return [now, now]
    case 'lastMonth': { const pm = m === 1 ? 12 : m - 1, py = m === 1 ? y - 1 : y; return [iso(py, pm, 1), iso(py, pm, lastDay(py, pm))] }
    case 'thisQuarter': { const q0 = Math.floor((m - 1) / 3) * 3 + 1; return [iso(y, q0, 1), iso(y, q0 + 2, lastDay(y, q0 + 2))] }
    case 'thisYear': return [iso(y, 1, 1), iso(y, 12, 31)]
    default: return [iso(y, m, 1), iso(y, m, lastDay(y, m))]
  }
}

function matches<T>(col: Col<T>, row: T, c: Cond): boolean {
  const raw = col.value(row) ?? ''
  if (col.type === 'text') {
    const t = raw.toLowerCase(), q = c.a.trim().toLowerCase()
    if (q === '') return true
    return c.op === 'notContains' ? !t.includes(q) : c.op === 'starts' ? t.startsWith(q) : c.op === 'eq' ? t === q : t.includes(q)
  }
  if (col.type === 'select') return c.a === '' ? true : c.op === 'neq' ? raw !== c.a : raw === c.a
  if (col.type === 'number') {
    const a = parseMoney(c.a), b = parseMoney(c.b)
    if (a === null) return true
    const r = compareMoney(raw, a)
    switch (c.op) {
      case 'gt': return r > 0
      case 'gte': return r >= 0
      case 'lt': return r < 0
      case 'lte': return r <= 0
      case 'between': return b === null ? r >= 0 : r >= 0 && compareMoney(raw, b) <= 0
      default: return r === 0
    }
  }
  // date
  if (raw === '') return false
  if (c.op === 'preset') { const [f, t] = presetRange(c.a || 'thisMonth'); return raw >= f && raw <= t }
  const a = c.a, b = c.b
  if (a === '') return true
  switch (c.op) {
    case 'before': return raw < a
    case 'after': return raw > a
    case 'between': return raw >= a && (b === '' || raw <= b)
    default: return raw === a
  }
}

const load = (key: string): SavedFilter[] => { try { return JSON.parse(localStorage.getItem(`hak.filters.${key}`) ?? '[]') } catch { return [] } }
const save = (key: string, list: SavedFilter[]) => { try { localStorage.setItem(`hak.filters.${key}`, JSON.stringify(list)) } catch { /* private mode */ } }

/**
 * Bảng dữ liệu có bộ lọc thông minh: tìm nhanh, lọc từng cột (văn bản / số tiền / ngày / danh sách), ghép nhiều điều kiện (và / hoặc),
 * sắp xếp theo cột, tổng các cột tiền của dòng đang lọc, lưu bộ lọc theo tên (trên trình duyệt này). Lọc trong các dòng đã tải về màn hình.
 */
export function DataGrid<T>({ columns, rows, rowKey, storageKey, onRowClick, rowClassName, rowRender, empty = 'Không có dữ liệu.', note }: {
  columns: Col<T>[]; rows: T[]; rowKey: (row: T) => string; storageKey: string
  onRowClick?: (row: T) => void; rowClassName?: (row: T) => string; empty?: string; note?: string
  /** Cells of a row (<td>…</td>) when a screen needs custom cells or action buttons; headers, filter and sort still come from `columns`. */
  rowRender?: (row: T) => ReactNode
}) {
  const [q, setQ] = useState('')
  const [conds, setConds] = useState<Cond[]>([])
  const [mode, setMode] = useState<'and' | 'or'>('and')
  const [open, setOpen] = useState(false)
  const [sort, setSort] = useState<{ key: string; dir: 1 | -1 } | null>(null)
  const [saved, setSaved] = useState<SavedFilter[]>(() => load(storageKey))
  const [name, setName] = useState('')
  useEffect(() => { setSaved(load(storageKey)) }, [storageKey])

  const filterable = columns.filter(c => !c.noFilter)
  const colOf = (key: string) => columns.find(c => c.key === key)
  const textOf = (c: Col<T>, r: T) => c.display ? c.display(r) : c.type === 'date' ? formatDate(c.value(r)) : c.type === 'number' ? formatMoney(c.value(r)) : (c.value(r) ?? '')

  const shown = useMemo(() => {
    const needle = q.trim().toLowerCase()
    const active = conds.filter(c => colOf(c.col))
    let out = rows.filter(r => {
      if (needle && !filterable.some(c => textOf(c, r).toLowerCase().includes(needle))) return false
      if (active.length === 0) return true
      const test = (c: Cond) => matches(colOf(c.col)!, r, c)
      return mode === 'and' ? active.every(test) : active.some(test)
    })
    if (sort) {
      const col = colOf(sort.key)
      if (col) out = [...out].sort((x, y) => {
        const a = col.value(x) ?? '', b = col.value(y) ?? ''
        return sort.dir * (col.type === 'number' ? compareMoney(a, b) : a.localeCompare(b, 'vi'))
      })
    }
    return out
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rows, q, conds, mode, sort, columns])

  const options = (c: Col<T>) => [...new Set(rows.map(r => c.value(r) ?? ''))].filter(v => v !== '').sort((a, b) => a.localeCompare(b, 'vi'))
  const labelOfValue = (c: Col<T>, v: string) => { const r = rows.find(x => (c.value(x) ?? '') === v); return r && c.display ? c.display(r) : v }
  const addCond = () => { const c = filterable[0]; if (c) setConds(x => [...x, { col: c.key, op: OPS[c.type][0][0], a: '', b: '' }]); setOpen(true) }
  const setCond = (i: number, patch: Partial<Cond>) => setConds(x => x.map((c, j) => (j === i ? { ...c, ...patch } : c)))
  const changeCol = (i: number, key: string) => { const c = colOf(key)!; setCond(i, { col: key, op: OPS[c.type][0][0], a: c.type === 'date' ? 'thisMonth' : '', b: '' }) }
  const summary = (c: Cond) => {
    const col = colOf(c.col); if (!col) return ''
    const op = OPS[col.type].find(o => o[0] === c.op)?.[1] ?? c.op
    const val = col.type === 'date' ? (c.op === 'preset' ? PRESETS.find(p => p[0] === c.a)?.[1] ?? '' : [formatDate(c.a), c.op === 'between' && c.b ? formatDate(c.b) : ''].filter(Boolean).join(' – '))
      : col.type === 'select' ? labelOfValue(col, c.a) : [c.a, c.op === 'between' && c.b ? c.b : ''].filter(Boolean).join(' – ')
    return `${col.label} ${op} ${val}`.trim()
  }
  const filtered = q.trim() !== '' || conds.length > 0
  const reset = () => { setQ(''); setConds([]) }
  const sums = columns.filter(c => c.sum).map(c => [c.key, addMoney(shown.map(r => c.value(r) ?? ''))] as const)

  return (
    <div className="datagrid">
      <div className="dg-bar">
        <input className="dg-search" type="search" placeholder="Tìm nhanh trong bảng…" aria-label="Tìm nhanh trong bảng" value={q} onChange={e => setQ(e.target.value)} />
        <button className={open || conds.length ? 'primary' : ''} onClick={() => setOpen(o => !o)}>Bộ lọc{conds.length ? ` (${conds.length})` : ''}</button>
        {saved.length > 0 && <select aria-label="Bộ lọc đã lưu" value="" onChange={e => { const s = saved.find(x => x.name === e.target.value); if (s) { setConds(s.conds); setMode(s.mode); setQ(s.q); setOpen(false) } }}>
          <option value="">Bộ lọc đã lưu…</option>{saved.map(s => <option key={s.name} value={s.name}>{s.name}</option>)}</select>}
        {filtered && <button onClick={reset}>Xóa lọc</button>}
        <span className="muted dg-count">{filtered ? `${shown.length} / ${rows.length} dòng` : `${rows.length} dòng`}{note ? ` · ${note}` : ''}</span>
      </div>
      {conds.length > 0 && !open && <div className="dg-chips">{conds.map((c, i) => <span key={i} className="chip">{summary(c)} <button aria-label="Bỏ điều kiện" onClick={() => setConds(x => x.filter((_, j) => j !== i))}>×</button></span>)}</div>}
      {open && (
        <div className="dg-panel">
          {conds.length > 1 && <div className="form-row"><span>Hiện dòng thỏa</span>
            <select value={mode} onChange={e => setMode(e.target.value as 'and' | 'or')} aria-label="Cách ghép điều kiện"><option value="and">tất cả điều kiện</option><option value="or">một trong các điều kiện</option></select></div>}
          {conds.map((c, i) => { const col = colOf(c.col)!; return (
            <div key={i} className="dg-cond">
              <select aria-label="Cột" value={c.col} onChange={e => changeCol(i, e.target.value)}>{filterable.map(x => <option key={x.key} value={x.key}>{x.label}</option>)}</select>
              <select aria-label="Phép so sánh" value={c.op} onChange={e => setCond(i, { op: e.target.value, a: col.type === 'date' && e.target.value === 'preset' ? 'thisMonth' : '', b: '' })}>{OPS[col.type].map(o => <option key={o[0]} value={o[0]}>{o[1]}</option>)}</select>
              {col.type === 'select' && <select aria-label="Giá trị" value={c.a} onChange={e => setCond(i, { a: e.target.value })}><option value="">— chọn —</option>{options(col).map(v => <option key={v} value={v}>{labelOfValue(col, v)}</option>)}</select>}
              {col.type === 'text' && <input aria-label="Giá trị" value={c.a} onChange={e => setCond(i, { a: e.target.value })} />}
              {col.type === 'number' && <><input aria-label="Giá trị" inputMode="decimal" className="num" value={c.a} onChange={e => setCond(i, { a: e.target.value })} />
                {c.op === 'between' && <><span>đến</span><input aria-label="Đến giá trị" inputMode="decimal" className="num" value={c.b} onChange={e => setCond(i, { b: e.target.value })} /></>}</>}
              {col.type === 'date' && (c.op === 'preset'
                ? <select aria-label="Khoảng nhanh" value={c.a || 'thisMonth'} onChange={e => setCond(i, { a: e.target.value })}>{PRESETS.map(p => <option key={p[0]} value={p[0]}>{p[1]}</option>)}</select>
                : <><DateInput value={c.a} onChange={e => setCond(i, { a: e.target.value })} aria-label="Ngày" />
                  {c.op === 'between' && <><span>đến</span><DateInput value={c.b} onChange={e => setCond(i, { b: e.target.value })} aria-label="Đến ngày" /></>}</>)}
              <button aria-label="Xóa điều kiện" onClick={() => setConds(x => x.filter((_, j) => j !== i))}>Xóa</button>
            </div>) })}
          <div className="form-row">
            <button onClick={addCond}>Thêm điều kiện</button>
            {conds.length > 0 && <>
              <input aria-label="Tên bộ lọc" placeholder="Tên bộ lọc để lưu" value={name} onChange={e => setName(e.target.value)} />
              <button disabled={!name.trim()} onClick={() => { const list = [...saved.filter(s => s.name !== name.trim()), { name: name.trim(), mode, q, conds }]; setSaved(list); save(storageKey, list); setName('') }}>Lưu bộ lọc</button></>}
            {saved.length > 0 && <select aria-label="Xóa bộ lọc đã lưu" value="" onChange={e => { const list = saved.filter(s => s.name !== e.target.value); setSaved(list); save(storageKey, list) }}>
              <option value="">Xóa bộ lọc đã lưu…</option>{saved.map(s => <option key={s.name} value={s.name}>{s.name}</option>)}</select>}
            <button onClick={() => setOpen(false)}>Đóng</button>
          </div>
        </div>
      )}
      <div className="scroll">
        <table className="grid">
          <thead><tr>{columns.map(c => (
            <th key={c.key} className={c.type === 'number' ? 'num' : ''} aria-sort={sort?.key === c.key ? (sort.dir === 1 ? 'ascending' : 'descending') : undefined}>
              {c.headerRender ? c.headerRender() : (
                <button className="dg-th" onClick={() => setSort(s => (s?.key === c.key ? (s.dir === 1 ? { key: c.key, dir: -1 } : null) : { key: c.key, dir: 1 }))} title="Sắp xếp theo cột này">
                  {c.label}{sort?.key === c.key ? (sort.dir === 1 ? ' ▲' : ' ▼') : ''}</button>)}
            </th>))}</tr></thead>
          <tbody>
            {shown.map(r => (
              <tr key={rowKey(r)} className={rowClassName?.(r) ?? ''} onClick={onRowClick ? () => onRowClick(r) : undefined} style={onRowClick ? { cursor: 'pointer' } : undefined}>
                {rowRender ? rowRender(r) : columns.map(c => <td key={c.key} className={c.type === 'number' ? 'num' : ''}>{c.render ? c.render(r) : textOf(c, r)}</td>)}
              </tr>))}
            {shown.length === 0 && <tr><td colSpan={columns.length} className="muted">{filtered ? 'Không có dòng nào khớp bộ lọc.' : empty}</td></tr>}
          </tbody>
          {sums.length > 0 && shown.length > 0 && <tfoot><tr>{columns.map((c, i) => {
            const s = sums.find(x => x[0] === c.key)
            return <td key={c.key} className={c.type === 'number' ? 'num' : ''}>{s ? formatMoney(s[1]) : i === 0 ? (filtered ? 'Cộng (đang lọc)' : 'Cộng') : ''}</td> })}</tr></tfoot>}
        </table>
      </div>
    </div>
  )
}
