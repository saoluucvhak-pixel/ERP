import { useCallback, useEffect, useState } from 'react'
import { get, post, put, type Partner } from './api'
import { ErrorBox, useAction } from './common'
import { formatMoney, parseMoney } from './money'

/** Danh mục đối tượng: thêm, sửa tên / mã số thuế, ngừng sử dụng (không xóa đối tượng đã phát sinh). */
export function CatalogView() {
  const [list, setList] = useState<Partner[]>([])
  const [edit, setEdit] = useState<Record<string, { name: string; taxCode: string; isActive: boolean; address: string; phone: string; email: string; creditLimit: string; terms: string }>>({})
  const [nw, setNw] = useState({ code: '', name: '', taxCode: '', roles: 'Customer' })
  const [filter, setFilter] = useState('')
  const [info, setInfo] = useState('')
  const { busy, error, run } = useAction()
  const load = useCallback(() => run(async () => setList(await get<Partner[]>('/partners'))), [run])
  useEffect(() => { load() }, [load])
  const val = (p: Partner) => edit[p.id] ?? { name: p.name, taxCode: p.taxCode ?? '', isActive: p.isActive, address: p.address ?? '', phone: p.phone ?? '', email: p.email ?? '',
    creditLimit: p.creditLimit ? formatMoney(p.creditLimit) : '', terms: p.paymentTermsDays == null ? '' : String(p.paymentTermsDays) }
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
        <thead><tr><th>Mã</th><th>Tên</th><th>Mã số thuế</th><th>Địa chỉ</th><th>Điện thoại</th><th>Email</th><th className="num" title="Để trống = không kiểm tra khi xác nhận đơn bán">Hạn mức tín dụng</th><th title="Số ngày từ ngày hóa đơn đến hạn thanh toán">Hạn TT (ngày)</th><th>Đang dùng</th><th /></tr></thead>
        <tbody>{shown.map(p => {
          const v = val(p)
          const base = { name: p.name, taxCode: p.taxCode ?? '', isActive: p.isActive, address: p.address ?? '', phone: p.phone ?? '', email: p.email ?? '',
            creditLimit: p.creditLimit ? formatMoney(p.creditLimit) : '', terms: p.paymentTermsDays == null ? '' : String(p.paymentTermsDays) }
          const dirty = (Object.keys(base) as (keyof typeof base)[]).some(k => v[k] !== base[k])
          return <tr key={p.id}><td>{p.code}</td>
            <td><input aria-label="Tên" value={v.name} onChange={e => set(p, { name: e.target.value })} style={{ minWidth: 260 }} /></td>
            <td><input aria-label="Mã số thuế" value={v.taxCode} onChange={e => set(p, { taxCode: e.target.value })} /></td>
            <td><input aria-label="Địa chỉ" value={v.address} onChange={e => set(p, { address: e.target.value })} style={{ minWidth: 200 }} /></td>
            <td><input aria-label="Điện thoại" value={v.phone} onChange={e => set(p, { phone: e.target.value })} size={12} /></td>
            <td><input aria-label="Email" value={v.email} onChange={e => set(p, { email: e.target.value })} /></td>
            <td><input className="num" aria-label="Hạn mức tín dụng" value={v.creditLimit} onChange={e => set(p, { creditLimit: e.target.value })} size={14} /></td>
            <td><input className="num" aria-label="Hạn thanh toán (ngày)" value={v.terms} onChange={e => set(p, { terms: e.target.value })} size={4} /></td>
            <td><input type="checkbox" aria-label="Đang dùng" checked={v.isActive} onChange={e => set(p, { isActive: e.target.checked })} /></td>
            <td>{dirty && <button className="primary" disabled={busy} onClick={() => run(async () => {
              const limit = v.creditLimit.trim() ? parseMoney(v.creditLimit) : null
              if (v.creditLimit.trim() && limit == null) throw new Error('Hạn mức tín dụng không hợp lệ.')
              const terms = v.terms.trim() ? Number.parseInt(v.terms, 10) : null
              if (v.terms.trim() && (terms == null || Number.isNaN(terms))) throw new Error('Hạn thanh toán phải là số ngày.')
              await put(`/partners/${p.id}`, { name: v.name, taxCode: v.taxCode || null, isActive: v.isActive })
              await put(`/partners/${p.id}/commercial`, { address: v.address || null, phone: v.phone || null, email: v.email || null, creditLimit: limit, paymentTermsDays: terms }); setEdit(s => { const c = { ...s }; delete c[p.id]; return c }); setInfo(`Đã cập nhật ${p.code}.`); await load()
            })}>Lưu</button>}</td></tr>
        })}</tbody>
      </table></div>
    </>
  )
}
