import { useCallback, useEffect, useState } from 'react'
import { get, post, put, type Partner } from './api'
import { ErrorBox, useAction } from './common'

/** Danh mục đối tượng: thêm, sửa tên / mã số thuế, ngừng sử dụng (không xóa đối tượng đã phát sinh). */
export function CatalogView() {
  const [list, setList] = useState<Partner[]>([])
  const [edit, setEdit] = useState<Record<string, { name: string; taxCode: string; isActive: boolean }>>({})
  const [nw, setNw] = useState({ code: '', name: '', taxCode: '', roles: 'Customer' })
  const [filter, setFilter] = useState('')
  const [info, setInfo] = useState('')
  const { busy, error, run } = useAction()
  const load = useCallback(() => run(async () => setList(await get<Partner[]>('/partners'))), [run])
  useEffect(() => { load() }, [load])
  const val = (p: Partner) => edit[p.id] ?? { name: p.name, taxCode: p.taxCode ?? '', isActive: p.isActive }
  const set = (p: Partner, patch: Partial<ReturnType<typeof val>>) => setEdit(s => ({ ...s, [p.id]: { ...val(p), ...patch } }))
  const shown = list.filter(p => !filter || `${p.code} ${p.name} ${p.taxCode ?? ''}`.toLowerCase().includes(filter.toLowerCase()))

  return (
    <>
      <div className="toolbar"><h2>Danh mục đối tượng</h2><label>Tìm<input value={filter} onChange={e => setFilter(e.target.value)} placeholder="mã, tên, MST" /></label></div>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      <div className="panel form-row">
        <label>Mã<input value={nw.code} onChange={e => setNw(n => ({ ...n, code: e.target.value.toUpperCase() }))} /></label>
        <label style={{ flex: 1 }}>Tên<input value={nw.name} onChange={e => setNw(n => ({ ...n, name: e.target.value }))} /></label>
        <label>Mã số thuế<input value={nw.taxCode} onChange={e => setNw(n => ({ ...n, taxCode: e.target.value }))} /></label>
        <label>Vai trò<select value={nw.roles} onChange={e => setNw(n => ({ ...n, roles: e.target.value }))}><option value="Customer">Khách hàng</option><option value="Supplier">Nhà cung cấp</option><option value="Employee">Nhân viên</option><option value="Customer, Supplier">Khách hàng + NCC</option></select></label>
        <button className="primary" style={{ alignSelf: 'flex-end' }} disabled={busy || !nw.code || !nw.name} onClick={() => run(async () => {
          await post('/partners', { code: nw.code, name: nw.name, roles: nw.roles, taxCode: nw.taxCode || null }); setInfo(`Đã thêm ${nw.code}.`); setNw({ code: '', name: '', taxCode: '', roles: 'Customer' }); await load()
        })}>Thêm</button>
      </div>
      <div className="scroll"><table className="grid">
        <thead><tr><th>Mã</th><th>Tên</th><th>Mã số thuế</th><th>Đang dùng</th><th /></tr></thead>
        <tbody>{shown.map(p => {
          const v = val(p)
          const dirty = v.name !== p.name || v.taxCode !== (p.taxCode ?? '') || v.isActive !== p.isActive
          return <tr key={p.id}><td>{p.code}</td>
            <td><input aria-label="Tên" value={v.name} onChange={e => set(p, { name: e.target.value })} style={{ minWidth: 260 }} /></td>
            <td><input aria-label="Mã số thuế" value={v.taxCode} onChange={e => set(p, { taxCode: e.target.value })} /></td>
            <td><input type="checkbox" aria-label="Đang dùng" checked={v.isActive} onChange={e => set(p, { isActive: e.target.checked })} /></td>
            <td>{dirty && <button className="primary" disabled={busy} onClick={() => run(async () => {
              await put(`/partners/${p.id}`, { name: v.name, taxCode: v.taxCode || null, isActive: v.isActive }); setEdit(s => { const c = { ...s }; delete c[p.id]; return c }); setInfo(`Đã cập nhật ${p.code}.`); await load()
            })}>Lưu</button>}</td></tr>
        })}</tbody>
      </table></div>
    </>
  )
}
