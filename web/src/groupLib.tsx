import { useCallback, useEffect, useMemo, useState } from 'react'
import { get, put } from './api'
import { ErrorBox, Modal, useAction } from './common'

export type GroupDomain = 'Partner' | 'Item' | 'Asset' | 'Warehouse'
export interface GroupRow { id: string; domain: GroupDomain; code: string; name: string; parentId: string | null; level: number; isActive: boolean; note: string | null; directMembers: number; allMembers: number; children: number; defaultCreditLimit?: string | null; defaultPaymentTermsDays?: number | null; defaultVatRate?: string | null; asset?: AssetDefaults | null }
export interface AssetDefaults { assetAccountId?: string | null; accumulatedAccountId?: string | null; expenseAccountId?: string | null; usefulLifeMonths?: number | null; method?: number | null }
export const METHOD_CODE: Record<number, string> = { 1: 'StraightLine', 2: 'TimeBased', 3: 'UnitsOfProduction', 4: 'DecliningBalance' }
export const METHOD_NUM: Record<string, number> = { StraightLine: 1, TimeBased: 2, UnitsOfProduction: 3, DecliningBalance: 4 }
export interface GroupLink { entityId: string; groupId: string; isPrimary?: boolean }
export const DOMAIN_VI: Record<GroupDomain, string> = { Partner: 'Đối tượng', Item: 'Vật tư, hàng hóa', Asset: 'Tài sản, CCDC', Warehouse: 'Kho' }

/** Cây nhóm và quan hệ mục – nhóm của một loại danh mục; lọc "thuộc nhóm" tính cả mọi nhóm con. */
export function useGroups(domain: GroupDomain) {
  const [tree, setTree] = useState<GroupRow[]>([])
  const [links, setLinks] = useState<GroupLink[]>([])
  const reload = useCallback(async () => {
    try {
      setTree(await get<GroupRow[]>(`/groups/${domain}`)); setLinks(await get<GroupLink[]>(`/groups/${domain}/links`))
    } catch { setTree([]); setLinks([]) }
  }, [domain])
  useEffect(() => { reload() }, [reload])
  const byId = useMemo(() => Object.fromEntries(tree.map(g => [g.id, g])), [tree])
  /** Mã nhóm và các nhóm con của nó. */
  const subtree = useCallback((id: string) => {
    const set = new Set<string>([id]); let grew = true
    while (grew) { grew = false; for (const g of tree) if (g.parentId && set.has(g.parentId) && !set.has(g.id)) { set.add(g.id); grew = true } }
    return set
  }, [tree])
  const groupsOf = useCallback((entityId: string) => links.filter(l => l.entityId === entityId).map(l => byId[l.groupId]).filter(Boolean), [links, byId])
  const inGroup = useCallback((entityId: string, groupId: string) => { const s = subtree(groupId); return links.some(l => l.entityId === entityId && s.has(l.groupId)) }, [links, subtree])
  /** Mặc định tài sản của nhóm: giá trị của nhóm, thiếu thì kế thừa nhóm cha gần nhất có khai (từng chỉ tiêu riêng). */
  const assetDefaults = useCallback((id: string): AssetDefaults => {
    const out: AssetDefaults = {}
    for (let cur: GroupRow | undefined = byId[id]; cur; cur = cur.parentId ? byId[cur.parentId] : undefined) {
      const a = cur.asset; if (!a || !cur.isActive) continue
      for (const k of ['assetAccountId', 'accumulatedAccountId', 'expenseAccountId', 'usefulLifeMonths', 'method'] as const) if (out[k] == null && a[k] != null) (out as Record<string, unknown>)[k] = a[k]
    }
    return out
  }, [byId])
  const options = useMemo(() => tree.map(g => ({ value: g.id, label: `${'  '.repeat(g.level)}${g.code} – ${g.name}${g.isActive ? '' : ' (ngừng)'}` })), [tree])
  return { tree, links, reload, byId, groupsOf, inGroup, options, assetDefaults }
}

/** Nút "Nhóm" của một mục: hộp chọn các nhóm (nhiều nhóm, cây nhiều cấp). */
export function EntityGroupsButton({ g, domain, entityId, title, onSaved }: { g: ReturnType<typeof useGroups>; domain: GroupDomain; entityId: string; title: string; onSaved?: () => void }) {
  const [open, setOpen] = useState(false)
  const [picked, setPicked] = useState<Set<string>>(new Set())
  const [primary, setPrimary] = useState('')
  const { busy, error, run } = useAction()
  const mine = g.groupsOf(entityId)
  return <>
    <button type="button" onClick={() => { setPicked(new Set(mine.map(x => x.id))); setPrimary(g.links.find(l => l.entityId === entityId && l.isPrimary)?.groupId ?? ''); setOpen(true) }} title="Xếp mục này vào các nhóm">{mine.length ? mine.map(x => x.code).join(', ') : 'Nhóm…'}</button>
    {open && <Modal title={`Nhóm của ${title}`} onClose={() => setOpen(false)} busy={busy}
      footer={<><button onClick={() => setOpen(false)}>Đóng</button><button className="primary" disabled={busy} onClick={() => run(async () => { await put(`/groups/${domain}/entity/${entityId}`, { groupIds: [...picked] }); await put(`/groups/${domain}/entity/${entityId}/primary`, { groupId: primary && picked.has(primary) ? primary : null }); setOpen(false); await g.reload(); onSaved?.() })}>Lưu</button></>}>
      <ErrorBox error={error} />
      {g.tree.length === 0 && <p className="muted">Chưa có nhóm nào cho loại danh mục này — tạo ở Danh mục, hệ thống › Nhóm danh mục.</p>}
      {g.tree.map(x => <label key={x.id} className="check" style={{ paddingLeft: 18 * x.level, display: 'block', opacity: x.isActive ? 1 : 0.6 }}>
        <input type="checkbox" checked={picked.has(x.id)} disabled={!x.isActive && !picked.has(x.id)} onChange={e => setPicked(s => { const n = new Set(s); if (e.target.checked) n.add(x.id); else n.delete(x.id); return n })} /> {x.code} – {x.name}</label>)}
      {picked.size > 1 && <label style={{ display: 'block', marginTop: 8 }}>Nhóm chính (lấy mặc định theo nhóm)<select value={picked.has(primary) ? primary : ''} onChange={e => setPrimary(e.target.value)}><option value="">— chưa chọn: nhóm sâu nhất, giá trị chặt nhất —</option>{g.tree.filter(x => picked.has(x.id)).map(x => <option key={x.id} value={x.id}>{x.code} – {x.name}</option>)}</select></label>}
      <p className="muted">Một mục thuộc được nhiều nhóm; lọc và báo cáo theo nhóm cha gồm cả các nhóm con.</p>
    </Modal>}
  </>
}
