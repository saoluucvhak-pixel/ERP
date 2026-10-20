import { useCallback, useEffect, useMemo, useState } from 'react'
import { get, put } from './api'
import { ErrorBox, useAction } from './common'
import { matchesQuery } from './filter'
import { SmartFilter, useFilterState } from './SmartFilter'

interface Row { key: string; name: string; cycle: string; defaultPrefix: string; prefix: string; width: number; customized: boolean; example: string; lastIssued: number }

/** Bộ mã chứng từ: xem tiền tố từng loại, đổi tiền tố / độ rộng số thứ tự cho công ty (chỉ áp dụng số cấp sau, số thứ tự không đứt). Quy tắc: docs/11-QUY-TAC-MA.md. */
export function NumberingView() {
  const [rows, setRows] = useState<Row[]>([])
  const [edit, setEdit] = useState<Record<string, { prefix: string; width: string }>>({})
  const [f, pf, cf] = useFilterState('numbering')
  const [info, setInfo] = useState('')
  const { busy, error, run } = useAction()
  const load = useCallback(() => run(async () => setRows(await get<Row[]>('/numbering'))), [run])
  useEffect(() => { load() }, [load])
  const cycles = useMemo(() => [...new Set(rows.map(r => r.cycle))], [rows])
  const shown = rows.filter(r => (!f.sel.cycle || r.cycle === f.sel.cycle) && matchesQuery(`${r.name} ${r.prefix} ${r.key}`, null, f.q))
  const val = (r: Row) => edit[r.key] ?? { prefix: r.prefix, width: String(r.width) }
  const save = (r: Row, reset: boolean) => run(async () => {
    const v = val(r)
    await put(`/numbering/${encodeURIComponent(r.key)}`, reset ? { prefix: null, width: null } : { prefix: v.prefix === r.defaultPrefix ? null : v.prefix, width: Number(v.width) === 6 ? null : Number(v.width) })
    setEdit(s => { const n = { ...s }; delete n[r.key]; return n }); setInfo(reset ? `Đã đặt ${r.name} về mặc định.` : `Đã lưu bộ mã ${r.name}.`); await load()
  })
  return (
    <>
      <p className="muted">Số chứng từ có dạng <b>TIỀN TỐ + NĂM TÀI CHÍNH – SỐ THỨ TỰ</b> (ví dụ <code>HDB2026-000123</code>). Đổi tiền tố / độ rộng chỉ áp dụng cho số cấp sau đó; số đã cấp giữ nguyên và số thứ tự vẫn liên tục.
        Tiền tố gồm 2–5 chữ in hoa không dấu, không trùng và không là phần đầu của tiền tố khác.</p>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      <SmartFilter label="Bộ mã" state={f} patch={pf} clear={cf} shown={shown.length} total={rows.length} placeholder="Tìm tên chứng từ hoặc tiền tố"
        selects={[{ key: 'cycle', label: 'Chu trình', options: cycles.map(c => ({ value: c, label: c })) }]} />
      <div className="scroll"><table className="grid">
        <thead><tr><th>Chu trình</th><th>Chứng từ</th><th>Tiền tố mặc định</th><th>Tiền tố đang dùng</th><th className="num">Độ rộng số</th><th>Số kế tiếp (ví dụ)</th><th className="num">Đã cấp năm nay</th><th /></tr></thead>
        <tbody>{shown.map(r => { const v = val(r); const dirty = !!edit[r.key]; return <tr key={r.key}>
          <td>{r.cycle}</td><td>{r.name}</td><td><code>{r.defaultPrefix}</code></td>
          <td><input aria-label={`Tiền tố ${r.name}`} size={6} value={v.prefix} maxLength={5} onChange={e => setEdit(s => ({ ...s, [r.key]: { ...v, prefix: e.target.value.toUpperCase() } }))} /></td>
          <td className="num"><select aria-label={`Độ rộng ${r.name}`} value={v.width} onChange={e => setEdit(s => ({ ...s, [r.key]: { ...v, width: e.target.value } }))}>{[4, 5, 6, 7, 8].map(w => <option key={w} value={w}>{w}</option>)}</select></td>
          <td><code>{r.example}</code></td><td className="num">{r.lastIssued || ''}</td>
          <td style={{ whiteSpace: 'nowrap' }}>{dirty && <button className="primary" disabled={busy} onClick={() => save(r, false)}>Lưu</button>}{r.customized && <button disabled={busy} onClick={() => save(r, true)}>Về mặc định</button>}</td></tr> })}</tbody>
      </table></div>
    </>
  )
}
