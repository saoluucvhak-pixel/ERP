import { useCallback, useEffect, useState } from 'react'
import { get, post, api } from './api'
import { ErrorBox, useAction } from './common'

interface PermissionInfo { code: string; label: string }
interface PermissionModule { key: string; label: string; permissions: PermissionInfo[] }
interface RoleRow { code: string; name: string; permissions: string[] }

/** Vai trò có sẵn: chỉ xem, không sửa. Muốn khác thì tạo vai trò riêng (có thể lấy một vai trò có sẵn làm mẫu). */
export const DEFAULT_ROLES = ['ADMIN', 'CHIEF_ACCOUNTANT', 'GL_ACCOUNTANT', 'ACCOUNTANT', 'ARAP_ACCOUNTANT', 'BANK_ACCOUNTANT',
  'INVENTORY_ACCOUNTANT', 'CASHIER', 'APPROVER', 'DIRECTOR', 'INTERNAL_AUDITOR']

/** Chọn phân hệ và chức năng cho từng vai trò (system administrators only). */
export function RolesEditor() {
  const [catalog, setCatalog] = useState<PermissionModule[]>([])
  const [roles, setRoles] = useState<RoleRow[]>([])
  const [editing, setEditing] = useState<string | null>(null)   // null = tạo mới
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [picked, setPicked] = useState<Set<string>>(new Set())
  const [info, setInfo] = useState('')
  const { busy, error, run } = useAction()
  const load = useCallback(() => run(async () => {
    setCatalog(await get<PermissionModule[]>('/admin/permission-catalog'))
    setRoles(await get<RoleRow[]>('/admin/roles'))
  }), [run])
  useEffect(() => { load() }, [load])

  const toggle = (perm: string) => setPicked(s => { const n = new Set(s); if (n.has(perm)) n.delete(perm); else n.add(perm); return n })
  const toggleModule = (m: PermissionModule) => setPicked(s => {
    const n = new Set(s)
    const all = m.permissions.every(p => n.has(p.code))
    m.permissions.forEach(p => (all ? n.delete(p.code) : n.add(p.code)))
    return n
  })
  const reset = () => { setEditing(null); setCode(''); setName(''); setPicked(new Set()) }
  const edit = (r: RoleRow) => { setEditing(r.code); setCode(r.code); setName(r.name); setPicked(new Set(r.permissions)); setInfo('') }
  const fromTemplate = (r: RoleRow) => { setEditing(null); setCode(''); setName(`${r.name} (bản sao)`); setPicked(new Set(r.permissions)); setInfo('') }
  const save = () => run(async () => {
    const body = { code, name, permissions: [...picked] }
    if (editing) await api('PUT', `/admin/roles/${editing}`, body); else await post('/admin/roles', body)
    setInfo(editing ? 'Đã cập nhật vai trò; người dùng đang giữ vai trò này có quyền mới ngay ở lần thao tác kế tiếp.' : 'Đã tạo vai trò.')
    reset(); await load()
  })

  return (
    <section className="panel">
      <h3>Vai trò & chức năng</h3>
      <p className="muted">Ba vai trò dùng hằng ngày: <b>Administrator</b> (quản trị người dùng, không tự có quyền kế toán), <b>Kế toán tổng hợp</b> và <b>Kế toán</b>.
        Vai trò có sẵn không sửa được; bấm "Dùng làm mẫu" rồi chọn phân hệ và chức năng cho vai trò riêng.</p>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      <div className="scroll"><table className="grid"><thead><tr><th>Vai trò</th><th>Mã</th><th>Số chức năng</th><th>Loại</th><th></th></tr></thead>
        <tbody>{roles.map(r => {
          const isDefault = DEFAULT_ROLES.includes(r.code)
          return <tr key={r.code}><td>{r.name}</td><td>{r.code}</td><td className="num">{r.permissions.length}</td><td>{isDefault ? 'Có sẵn' : 'Tự tạo'}</td>
            <td style={{ display: 'flex', gap: 6 }}>
              <button disabled={busy} onClick={() => fromTemplate(r)}>Dùng làm mẫu</button>
              {!isDefault && <button disabled={busy} onClick={() => edit(r)}>Sửa</button>}
            </td></tr>
        })}</tbody></table></div>

      <h4>{editing ? `Sửa vai trò ${editing}` : 'Tạo vai trò mới'}</h4>
      <div className="form-row">
        <label>Mã vai trò <input value={code} disabled={!!editing} placeholder="VD: KT_QUY" onChange={e => setCode(e.target.value.toUpperCase())} /></label>
        <label>Tên vai trò <input value={name} onChange={e => setName(e.target.value)} /></label>
      </div>
      <div className="perm-grid">
        {catalog.map(m => {
          const count = m.permissions.filter(p => picked.has(p.code)).length
          return (
            <fieldset key={m.key} className="perm-module">
              <legend>
                <label className="check"><input type="checkbox" checked={count === m.permissions.length} ref={el => { if (el) el.indeterminate = count > 0 && count < m.permissions.length }}
                  onChange={() => toggleModule(m)} /> <b>{m.label}</b></label>
              </legend>
              {m.permissions.map(p => <label key={p.code} className="check"><input type="checkbox" checked={picked.has(p.code)} onChange={() => toggle(p.code)} /> {p.label}</label>)}
            </fieldset>
          )
        })}
      </div>
      <div className="form-row">
        <button className="primary" disabled={busy || !code || !name || picked.size === 0} onClick={save}>{editing ? 'Lưu thay đổi' : 'Tạo vai trò'}</button>
        {(editing || code || name || picked.size > 0) && <button disabled={busy} onClick={reset}>Hủy</button>}
      </div>
    </section>
  )
}
