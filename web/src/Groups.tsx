import { useCallback, useEffect, useState } from 'react'
import { api, get, post, put } from './api'
import { AccountSelect, ErrorBox, ExportButton, Modal, useAction, useCatalog } from './common'
import { formatMoney, parseMoney, today } from './money'
import { DOMAIN_VI, METHOD_CODE, METHOD_NUM, useGroups, type GroupDomain, type GroupRow } from './groupLib'

interface Entity { id: string; code: string; name: string }
interface Member { entityId: string; code: string; name: string; otherGroupIds: string[] }
interface Summary { columns: { key: string; label: string }[]; rows: { groupId: string | null; code: string; name: string; level: number; count: number; values: Record<string, number> }[]; ungroupedValues: Record<string, number>; ungroupedCount: number }

async function entitiesOf(domain: GroupDomain): Promise<Entity[]> {
  if (domain === 'Partner') return get<Entity[]>('/partners')
  if (domain === 'Item') return get<Entity[]>('/items')
  if (domain === 'Warehouse') return get<Entity[]>('/warehouses')
  return get<Entity[]>(`/assets?asOf=${today()}`)
}

/** Nhóm danh mục nhiều cấp: cây nhóm, thành viên (một mục thuộc nhiều nhóm), báo cáo theo nhóm. */
export function GroupsView() {
  const [domain, setDomain] = useState<GroupDomain>('Partner')
  const [tab, setTab] = useState<'tree' | 'report'>('tree')
  return (
    <>
      <div className="toolbar"><h2>Nhóm danh mục</h2>
        {(Object.keys(DOMAIN_VI) as GroupDomain[]).map(d => <button key={d} className={domain === d ? 'primary' : ''} onClick={() => setDomain(d)}>{DOMAIN_VI[d]}</button>)}
        <span className="muted">|</span>
        <button className={tab === 'tree' ? 'primary' : ''} onClick={() => setTab('tree')}>Cây nhóm, thành viên</button>
        <button className={tab === 'report' ? 'primary' : ''} onClick={() => setTab('report')}>Báo cáo theo nhóm</button></div>
      {tab === 'tree' ? <Tree key={domain} domain={domain} /> : <Report key={domain} domain={domain} />}
    </>
  )
}

function Tree({ domain }: { domain: GroupDomain }) {
  const g = useGroups(domain)
  const [sel, setSel] = useState<GroupRow | null>(null)
  const [members, setMembers] = useState<Member[]>([])
  const [entities, setEntities] = useState<Entity[]>([])
  const [form, setForm] = useState<{ id: string | null; code: string; name: string; parentId: string; note: string; active: boolean; limit: string; days: string; vat: string; aAsset: string; aAccum: string; aExp: string; aLife: string; aMethod: string } | null>(null)
  const [add, setAdd] = useState<string[] | null>(null)
  const [q, setQ] = useState('')
  const [info, setInfo] = useState('')
  const { busy, error, run } = useAction()
  const { accounts } = useCatalog()
  useEffect(() => { entitiesOf(domain).then(setEntities).catch(() => setEntities([])) }, [domain])
  const loadMembers = useCallback(async (id: string | null) => setMembers(id ? await get<Member[]>(`/groups/by-id/${id}/members?includeDescendants=true`) : []), [])
  useEffect(() => { run(async () => { await loadMembers(sel?.id ?? null) }) }, [sel?.id, g.links, loadMembers, run])
  const act = (msg: string, fn: () => Promise<unknown>) => run(async () => { await fn(); setInfo(msg); await g.reload() })
  const current = sel ? g.byId[sel.id] ?? null : null
  const direct = new Set(g.links.filter(l => l.groupId === current?.id).map(l => l.entityId))
  const candidates = entities.filter(e => !members.some(m => m.entityId === e.id) && `${e.code} ${e.name}`.toLowerCase().includes(q.toLowerCase()))
  return (
    <>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      <div className="toolbar"><button className="primary" onClick={() => setForm({ id: null, code: '', name: '', parentId: current?.id ?? '', note: '', active: true, limit: '', days: '', vat: '', aAsset: '', aAccum: '', aExp: '', aLife: '', aMethod: '' })}>{current ? `Thêm nhóm con của ${current.code}` : 'Thêm nhóm gốc'}</button>
        {current && <button onClick={() => setForm({ id: current.id, code: current.code, name: current.name, parentId: current.parentId ?? '', note: current.note ?? '', active: current.isActive, limit: current.defaultCreditLimit ?? '', days: current.defaultPaymentTermsDays?.toString() ?? '', vat: current.defaultVatRate ?? '', aAsset: current.asset?.assetAccountId ?? '', aAccum: current.asset?.accumulatedAccountId ?? '', aExp: current.asset?.expenseAccountId ?? '', aLife: current.asset?.usefulLifeMonths?.toString() ?? '', aMethod: current.asset?.method ? METHOD_CODE[current.asset.method] : '' })}>Sửa nhóm {current.code}</button>}
        {current && <button disabled={busy || current.children > 0 || current.directMembers > 0} title="Chỉ xóa được nhóm không có nhóm con và thành viên" onClick={() => { if (confirm(`Xóa nhóm ${current.code}?`)) act('Đã xóa nhóm.', async () => { await api('DELETE', `/groups/by-id/${current.id}`); setSel(null) }) }}>Xóa</button>}
        {sel && <button onClick={() => setSel(null)}>Bỏ chọn</button>}</div>
      <div className="form-row" style={{ alignItems: 'flex-start' }}>
        <div className="panel" style={{ flex: '1 1 320px' }}>
          <strong>Cây nhóm {DOMAIN_VI[domain].toLowerCase()}</strong>
          {g.tree.length === 0 && <p className="muted">Chưa có nhóm. Bấm "Thêm nhóm gốc".</p>}
          {g.tree.map(x => <div key={x.id}><button type="button" className={current?.id === x.id ? 'primary' : ''} style={{ marginLeft: 18 * x.level, marginBottom: 2, opacity: x.isActive ? 1 : 0.6, textAlign: 'left' }} onClick={() => setSel(x)}>
            {x.code} – {x.name} <span className="muted">({x.allMembers})</span></button></div>)}
        </div>
        <div className="panel" style={{ flex: '2 1 420px' }}>
          {!current && <p className="muted">Chọn một nhóm để xem thành viên (gồm cả nhóm con).</p>}
          {current && <>
            <strong>{current.code} – {current.name}</strong> <span className="muted">{current.isActive ? '' : '(ngừng dùng) '}· {current.allMembers} thành viên (trực tiếp {current.directMembers})</span>
            <div className="toolbar"><button className="primary" disabled={!current.isActive} onClick={() => { setAdd([]); setQ('') }}>Thêm thành viên</button>
              <ExportButton fileName={`Nhom_${current.code}`} sheet={current.code} disabled={members.length === 0} columns={[{ header: 'Mã', width: 16 }, { header: 'Tên', width: 36 }, { header: 'Thuộc thêm nhóm', width: 30 }]}
                rows={members.map(m => [m.code, m.name, m.otherGroupIds.map(i => g.byId[i]?.code).filter(Boolean).join(', ')])} /></div>
            <div className="scroll"><table className="grid">
              <thead><tr><th>Mã</th><th>Tên</th><th>Thuộc thêm nhóm</th><th /></tr></thead>
              <tbody>{members.map(m => <tr key={m.entityId}><td>{m.code}</td><td>{m.name}</td><td>{m.otherGroupIds.map(i => g.byId[i]?.code).filter(Boolean).join(', ')}</td>
                <td>{direct.has(m.entityId) ? <button disabled={busy} onClick={() => act('Đã bỏ khỏi nhóm.', () => post(`/groups/by-id/${current.id}/members/remove`, { entityIds: [m.entityId] }))}>Bỏ khỏi nhóm</button> : <span className="muted">qua nhóm con</span>}</td></tr>)}
                {members.length === 0 && <tr><td colSpan={4} className="muted">Chưa có thành viên.</td></tr>}</tbody>
            </table></div>
          </>}
        </div>
      </div>

      {form && <Modal title={form.id ? `Sửa nhóm ${form.code}` : 'Thêm nhóm'} onClose={() => setForm(null)} busy={busy}
        footer={<><button onClick={() => setForm(null)}>Đóng</button><button className="primary" disabled={busy || !form.name.trim() || (!form.id && !form.code.trim())} onClick={() => run(async () => {
          const limit = form.limit.trim() ? parseMoney(form.limit) : null
          if (form.limit.trim() && limit == null) throw new Error('Hạn mức tín dụng không hợp lệ.')
          if (form.id) await put(`/groups/by-id/${form.id}`, { name: form.name, parentId: form.parentId || null, note: form.note || null, isActive: form.active, defaultCreditLimit: limit, defaultPaymentTermsDays: form.days ? Number(form.days) : null, defaultVatRate: form.vat || null, asset: domain === 'Asset' ? { assetAccountId: form.aAsset || null, accumulatedAccountId: form.aAccum || null, expenseAccountId: form.aExp || null, usefulLifeMonths: form.aLife ? Number(form.aLife) : null, method: form.aMethod ? METHOD_NUM[form.aMethod] : null } : null })
          else await post('/groups', { domain, code: form.code, name: form.name, parentId: form.parentId || null, note: form.note || null, defaultCreditLimit: limit, defaultPaymentTermsDays: form.days ? Number(form.days) : null, defaultVatRate: form.vat || null, asset: domain === 'Asset' ? { assetAccountId: form.aAsset || null, accumulatedAccountId: form.aAccum || null, expenseAccountId: form.aExp || null, usefulLifeMonths: form.aLife ? Number(form.aLife) : null, method: form.aMethod ? METHOD_NUM[form.aMethod] : null } : null })
          setForm(null); setInfo('Đã lưu nhóm.'); await g.reload()
        })}>Lưu</button></>}>
        <div className="form-row">
          <label>Mã nhóm<input value={form.code} disabled={!!form.id} maxLength={20} onChange={e => setForm({ ...form, code: e.target.value.toUpperCase() })} placeholder="KV-DN, GO-KEO…" /></label>
          <label style={{ flex: 1 }}>Tên nhóm<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} /></label>
          <label>Nhóm cha<select value={form.parentId} onChange={e => setForm({ ...form, parentId: e.target.value })}><option value="">— nhóm gốc —</option>{g.options.filter(o => o.value !== form.id).map(o => <option key={o.value} value={o.value}>{o.label}</option>)}</select></label>
        </div>
        <label style={{ display: 'block' }}>Ghi chú<input style={{ width: '100%' }} value={form.note} onChange={e => setForm({ ...form, note: e.target.value })} /></label>
        {domain === 'Partner' && <>
          <div className="form-row">
            <label>Hạn mức tín dụng mặc định (VND)<input inputMode="decimal" value={form.limit} onChange={e => setForm({ ...form, limit: e.target.value.replace(/[^0-9.,\s]/g, '') })} /></label>
            <label>Hạn thanh toán mặc định (ngày)<input inputMode="numeric" value={form.days} onChange={e => setForm({ ...form, days: e.target.value.replace(/\D/g, '') })} /></label>
          </div>
          <p className="muted">Chỉ dùng khi chính khách hàng chưa khai báo. Lấy từ nhóm chính của khách (chưa chọn nhóm chính thì nhóm sâu nhất, cùng cấp lấy giá trị chặt nhất); nhóm chưa khai thì kế thừa nhóm cha.</p>
        </>}
        {domain === 'Asset' && <>
          <div className="form-row">
            <label>TK tài sản mặc định<AccountSelect accounts={accounts} value={form.aAsset} onChange={v => setForm({ ...form, aAsset: v })} ariaLabel="TK tài sản mặc định" /></label>
            <label>TK hao mòn mặc định<AccountSelect accounts={accounts} value={form.aAccum} onChange={v => setForm({ ...form, aAccum: v })} ariaLabel="TK hao mòn mặc định" /></label>
            <label>TK chi phí mặc định<AccountSelect accounts={accounts} value={form.aExp} onChange={v => setForm({ ...form, aExp: v })} ariaLabel="TK chi phí mặc định" /></label>
          </div>
          <div className="form-row">
            <label>Thời gian sử dụng mặc định (tháng)<input inputMode="numeric" value={form.aLife} onChange={e => setForm({ ...form, aLife: e.target.value.replace(/\D/g, '') })} /></label>
            <label>Phương pháp mặc định<select value={form.aMethod} onChange={e => setForm({ ...form, aMethod: e.target.value })}><option value="">— không —</option><option value="StraightLine">Đường thẳng theo tháng</option><option value="TimeBased">Theo thời gian (số ngày)</option><option value="UnitsOfProduction">Theo sản lượng</option><option value="DecliningBalance">Số dư giảm dần</option></select></label>
          </div>
          <p className="muted">Điền sẵn khi lập thẻ tài sản mới có chọn nhóm này (nhóm chưa khai thì kế thừa nhóm cha). Thẻ lưu giá trị riêng và vẫn sửa được; đổi mặc định không đổi thẻ đã lập.</p>
        </>}
        {domain === 'Item' && <label>Thuế suất GTGT mặc định (%)<input inputMode="decimal" value={form.vat} onChange={e => setForm({ ...form, vat: e.target.value.replace(/[^0-9.]/g, '') })} /></label>}
        {form.id && <label className="check"><input type="checkbox" checked={form.active} onChange={e => setForm({ ...form, active: e.target.checked })} /> Đang dùng</label>}
        <p className="muted">Mã gồm chữ hoa, số, dấu -; không đổi sau khi tạo. Nhóm chỉ để phân loại và báo cáo, không đổi số liệu đã ghi sổ.</p>
      </Modal>}

      {add && current && <Modal title={`Thêm thành viên vào ${current.code}`} onClose={() => setAdd(null)} busy={busy}
        footer={<><button onClick={() => setAdd(null)}>Đóng</button><button className="primary" disabled={busy || add.length === 0} onClick={() => run(async () => {
          const r = await post<{ added: number }>(`/groups/by-id/${current.id}/members`, { entityIds: add }); setAdd(null); setInfo(`Đã thêm ${r.added} thành viên.`); await g.reload()
        })}>Thêm {add.length || ''}</button></>}>
        <input type="search" placeholder="Tìm mã hoặc tên" value={q} onChange={e => setQ(e.target.value)} style={{ width: '100%' }} />
        <div style={{ maxHeight: 320, overflow: 'auto' }}>{candidates.slice(0, 300).map(e => <label key={e.id} className="check" style={{ display: 'block' }}>
          <input type="checkbox" checked={add.includes(e.id)} onChange={ev => setAdd(a => ev.target.checked ? [...(a ?? []), e.id] : (a ?? []).filter(x => x !== e.id))} /> {e.code} – {e.name}</label>)}
          {candidates.length === 0 && <span className="muted">Không còn mục nào để thêm.</span>}</div>
      </Modal>}
    </>
  )
}

function Report({ domain }: { domain: GroupDomain }) {
  const [asOf, setAsOf] = useState(today())
  const [data, setData] = useState<Summary | null>(null)
  const { busy, error, run } = useAction()
  const load = useCallback(() => run(async () => setData(await get<Summary>(`/groups/${domain}/summary?asOf=${asOf}`))), [run, domain, asOf])
  useEffect(() => { load() }, [load])
  const cols = data?.columns ?? []
  const rows = data?.rows ?? []
  return (
    <>
      <div className="toolbar"><label>Tính đến ngày<input type="date" value={asOf} onChange={e => setAsOf(e.target.value)} /></label><button onClick={() => load()} disabled={busy}>Xem</button>
        <ExportButton fileName={`Theo_nhom_${domain}_${asOf}`} sheet="Theo nhóm" disabled={rows.length === 0} columns={[{ header: 'Mã nhóm', width: 16 }, { header: 'Tên nhóm', width: 34 }, { header: 'Số mục', numeric: true, width: 10 }, ...cols.map(c => ({ header: c.label, numeric: true, width: 18 }))]}
          rows={[...rows.map(r => [r.code, `${'  '.repeat(r.level)}${r.name}`, String(r.count), ...cols.map(c => String(r.values[c.key] ?? 0))]), ['—', 'Chưa thuộc nhóm nào', String(data?.ungroupedCount ?? 0), ...cols.map(c => String(data?.ungroupedValues[c.key] ?? 0))]]} /></div>
      <ErrorBox error={error} />
      <div className="scroll"><table className="grid">
        <thead><tr><th>Nhóm</th><th className="num">Số mục</th>{cols.map(c => <th key={c.key} className="num">{c.label}</th>)}</tr></thead>
        <tbody>{rows.map(r => <tr key={r.groupId}><td style={{ paddingLeft: 8 + 18 * r.level }}>{r.code} – {r.name}</td><td className="num">{r.count}</td>{cols.map(c => <td key={c.key} className="num">{formatMoney(String(r.values[c.key] ?? 0))}</td>)}</tr>)}
          {rows.length === 0 && <tr><td colSpan={cols.length + 2} className="muted">Chưa có nhóm nào.</td></tr>}
          {data && <tr style={{ fontStyle: 'italic' }}><td>Chưa thuộc nhóm nào</td><td className="num">{data.ungroupedCount}</td>{cols.map(c => <td key={c.key} className="num">{formatMoney(String(data.ungroupedValues[c.key] ?? 0))}</td>)}</tr>}</tbody>
      </table></div>
      <p className="muted">Một mục thuộc nhiều nhóm được tính vào từng nhóm, nhóm cha gồm cả nhóm con — nên các dòng không cộng lại thành tổng. Số liệu theo đối tượng: phải thu / phải trả còn mở; vật tư, kho: giá trị tồn; tài sản: nguyên giá, hao mòn, giá trị còn lại (tài sản đang dùng).</p>
    </>
  )
}
