import { useCallback, useEffect, useState } from 'react'
import { get, post, put } from './api'
import { ErrorBox, FlowStrip, useAction } from './common'
import { formatDate, formatMoney, parseMoney, today } from './money'
import { MaintenancePanels } from './AssetMaintenance'

interface Req { id: string; number: string; class: 'FixedAsset' | 'Tool'; name: string; department: string | null; estimatedCost: string; neededDate: string; reason: string; status: 'Pending' | 'Approved' | 'Rejected' | 'Fulfilled' | 'Cancelled'; decisionNote: string | null; assetId: string | null }
interface Cur { assetId: string; code: string; name: string; class: 'FixedAsset' | 'Tool'; department: string | null; custodian: string | null; location: string | null; since: string | null }
interface CountLine { assetId: string; code: string; name: string; bookDepartment: string | null; result: 'NotCounted' | 'Found' | 'Missing' | 'Damaged'; actualDepartment: string | null; note: string | null; netBookValue: string }
interface Count { id: string; number: string; countDate: string; note: string | null; status: 'Draft' | 'Completed' | 'Cancelled'; total: number; counted: number; missing: number; damaged: number; moved: number; missingNetBookValue: string; lines: CountLine[] }
export interface AssetRef { id: string; code: string; name: string; class: 'FixedAsset' | 'Tool'; status: string }

const CLASS_VI = { FixedAsset: 'TSCĐ', Tool: 'CCDC' } as const
const REQ_VI = { Pending: 'Chờ duyệt', Approved: 'Đã duyệt, chờ mua / lập thẻ', Rejected: 'Bị từ chối', Fulfilled: 'Đã lập thẻ', Cancelled: 'Đã hủy' } as const
const RESULT_VI = { NotCounted: 'Chưa kiểm', Found: 'Có, đúng sổ', Missing: 'Không thấy', Damaged: 'Hỏng' } as const

/** Chu trình quản lý TSCĐ / CCDC (tệp phân tích ERP): đề nghị mua sắm → mua → ghi tăng → bàn giao → khấu hao → kiểm kê → ghi giảm. */
export function AssetCyclePanels({ assets, depreciationNote }: { assets: AssetRef[]; depreciationNote: string }) {
  const [reqs, setReqs] = useState<Req[]>([])
  const [cur, setCur] = useState<Cur[]>([])
  const [openWo, setOpenWo] = useState(0)
  const [counts, setCounts] = useState<Count[]>([])
  const [openCount, setOpenCount] = useState<string | null>(null)
  const [draft, setDraft] = useState<Record<string, { result: CountLine['result']; actual: string; note: string }>>({})
  const [form, setForm] = useState({ class: 'FixedAsset', name: '', department: '', cost: '', neededDate: today(), reason: '' })
  const [assign, setAssign] = useState<{ assetId: string; code: string; department: string; custodian: string; location: string } | null>(null)
  const [countDate, setCountDate] = useState(today())
  const [info, setInfo] = useState('')
  const { busy, error, run, setError } = useAction()

  const load = useCallback(() => run(async () => {
    setReqs(await get<Req[]>('/assets/requests')); setCur(await get<Cur[]>('/assets/assignments')); setCounts(await get<Count[]>('/assets/counts'))
  }), [run])
  useEffect(() => { load() }, [load])
  const act = (msg: string, fn: () => Promise<unknown>) => run(async () => { await fn(); setInfo(msg); await load() })
  const ask = (q: string) => { const v = prompt(q); return v && v.trim() ? v.trim() : null }

  const draftCount = counts.find(c => c.status === 'Draft')
  const open = counts.find(c => c.id === openCount) ?? draftCount
  const lineOf = (l: CountLine) => draft[l.assetId] ?? { result: l.result, actual: l.actualDepartment ?? '', note: l.note ?? '' }
  const unassigned = cur.filter(c => !c.department).length
  const pending = reqs.filter(r => r.status === 'Pending').length
  const approved = reqs.filter(r => r.status === 'Approved').length

  return (
    <>
      <FlowStrip steps={[
        { label: 'Đề nghị mua sắm / đầu tư (duyệt)', journal: false, count: `${pending} chờ duyệt` },
        { label: 'Mua qua chu trình cung ứng (hóa đơn mua)', journal: true, screen: 'trade', count: `${approved} đề nghị đã duyệt chờ lập thẻ` },
        { label: 'Lập thẻ, ghi tăng (nguyên giá từ bút toán đã ghi sổ)', journal: true, note: 'ghi Nhật ký khi hóa đơn mua ghi sổ; ghi tăng chỉ đóng băng thẻ' },
        { label: 'Bàn giao cho bộ phận / người sử dụng', journal: false, count: `${unassigned} tài sản chưa bàn giao` },
        { label: 'Khấu hao / phân bổ hằng kỳ → chi phí', journal: true, count: depreciationNote },
        { label: 'Bảo trì, sửa chữa (YCSC → duyệt → thi công → nghiệm thu)', journal: false, count: openWo ? `${openWo} phiếu đang xử lý` : 'xem mục Bảo trì, sửa chữa' },
        { label: 'Kiểm kê định kỳ', journal: false, count: draftCount ? `phiếu ${draftCount.number} đang kiểm` : 'chưa có phiếu đang kiểm' },
        { label: 'Ghi giảm / thanh lý', journal: true },
      ]} />
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}

      <section className="panel">
        <h3>Đề nghị mua sắm / đầu tư <span className="muted">· không ghi Nhật ký; người duyệt khác người lập. Duyệt xong thì mua bằng hóa đơn mua, rồi lập thẻ và gắn vào đề nghị.</span></h3>
        <div className="form-row">
          <label>Loại<select value={form.class} onChange={e => setForm({ ...form, class: e.target.value })}><option value="FixedAsset">TSCĐ</option><option value="Tool">CCDC</option></select></label>
          <label>Tên tài sản<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} /></label>
          <label>Bộ phận đề nghị<input value={form.department} onChange={e => setForm({ ...form, department: e.target.value })} /></label>
          <label>Giá trị dự kiến<input inputMode="decimal" value={form.cost} onChange={e => setForm({ ...form, cost: e.target.value })} /></label>
          <label>Cần trước ngày<input type="date" value={form.neededDate} onChange={e => setForm({ ...form, neededDate: e.target.value })} /></label>
          <label>Lý do<input value={form.reason} onChange={e => setForm({ ...form, reason: e.target.value })} /></label>
          <button className="primary" disabled={busy || !form.name.trim() || !form.reason.trim()} onClick={() => {
            const cost = parseMoney(form.cost)
            if (!cost) { setError(new Error('Giá trị dự kiến không hợp lệ.')); return }
            act('Đã lập đề nghị mua sắm.', async () => { await post('/assets/requests', { class: form.class, name: form.name, department: form.department || null, estimatedCost: cost, neededDate: form.neededDate, reason: form.reason }); setForm({ ...form, name: '', department: '', cost: '', reason: '' }) })
          }}>Lập đề nghị</button>
        </div>
        <table className="grid"><thead><tr><th>Số</th><th>Loại</th><th>Tên</th><th>Bộ phận</th><th className="num">Dự kiến</th><th>Cần trước</th><th>Trạng thái</th><th /></tr></thead>
          <tbody>{reqs.map(r => <tr key={r.id}><td>{r.number}</td><td>{CLASS_VI[r.class]}</td><td title={r.reason}>{r.name}</td><td>{r.department ?? ''}</td><td className="num">{formatMoney(r.estimatedCost)}</td><td>{formatDate(r.neededDate)}</td>
            <td title={r.decisionNote ?? ''}>{REQ_VI[r.status]}{r.decisionNote ? ` — ${r.decisionNote}` : ''}</td>
            <td style={{ display: 'flex', gap: 6 }}>
              {r.status === 'Pending' && <>
                <button disabled={busy} onClick={() => act(`Đã duyệt ${r.number}.`, () => post(`/assets/requests/${r.id}/decide`, { approve: true, note: null }))}>Duyệt</button>
                <button disabled={busy} onClick={() => { const note = ask('Lý do từ chối:'); if (note) act(`Đã từ chối ${r.number}.`, () => post(`/assets/requests/${r.id}/decide`, { approve: false, note })) }}>Từ chối</button></>}
              {r.status === 'Approved' && <select aria-label={`Gắn thẻ cho ${r.number}`} value="" disabled={busy} onChange={e => e.target.value && act(`Đã gắn thẻ vào ${r.number}.`, () => post(`/assets/requests/${r.id}/link`, { assetId: e.target.value }))}>
                <option value="">Gắn thẻ đã lập…</option>
                {assets.filter(a => a.class === r.class && a.status !== 'Cancelled' && !reqs.some(x => x.assetId === a.id)).map(a => <option key={a.id} value={a.id}>{a.code} – {a.name}</option>)}</select>}
              {(r.status === 'Pending' || r.status === 'Approved') && <button disabled={busy} onClick={() => { const reason = ask('Lý do hủy đề nghị:'); if (reason) act(`Đã hủy ${r.number}.`, () => post(`/assets/requests/${r.id}/cancel`, { reason })) }}>Hủy</button>}
            </td></tr>)}
            {reqs.length === 0 && <tr><td colSpan={8} className="muted">Chưa có đề nghị mua sắm.</td></tr>}</tbody></table>
      </section>

      <section className="panel">
        <h3>Bàn giao, sử dụng <span className="muted">· tài sản đang dùng và bộ phận / người đang giữ; mỗi lần bàn giao mới đóng lần trước và lưu lịch sử</span></h3>
        <table className="grid"><thead><tr><th>Tài sản</th><th>Loại</th><th>Bộ phận</th><th>Người giữ</th><th>Vị trí</th><th>Từ ngày</th><th /></tr></thead>
          <tbody>{cur.map(c => <tr key={c.assetId}><td>{c.code} – {c.name}</td><td>{CLASS_VI[c.class]}</td><td>{c.department ?? <span className="muted">chưa bàn giao</span>}</td><td>{c.custodian ?? ''}</td><td>{c.location ?? ''}</td><td>{formatDate(c.since)}</td>
            <td><button disabled={busy} onClick={() => setAssign({ assetId: c.assetId, code: c.code, department: c.department ?? '', custodian: c.custodian ?? '', location: c.location ?? '' })}>{c.department ? 'Điều chuyển' : 'Bàn giao'}</button></td></tr>)}
            {cur.length === 0 && <tr><td colSpan={7} className="muted">Chưa có tài sản đang sử dụng.</td></tr>}</tbody></table>
        {assign && <div className="form-row">
          <strong>{assign.code}</strong>
          <label>Bộ phận<input value={assign.department} onChange={e => setAssign({ ...assign, department: e.target.value })} /></label>
          <label>Người giữ<input value={assign.custodian} onChange={e => setAssign({ ...assign, custodian: e.target.value })} /></label>
          <label>Vị trí<input value={assign.location} onChange={e => setAssign({ ...assign, location: e.target.value })} /></label>
          <button className="primary" disabled={busy || !assign.department.trim()} onClick={() => act(`Đã bàn giao ${assign.code}.`, async () => {
            await post(`/assets/${assign.assetId}/assign`, { department: assign.department, custodian: assign.custodian || null, location: assign.location || null, date: today(), note: null }); setAssign(null) })}>Lưu</button>
          <button onClick={() => setAssign(null)}>Đóng</button></div>}
      </section>

      <MaintenancePanels assets={assets} onOpenCount={setOpenWo} />

      <section className="panel">
        <h3>Kiểm kê tài sản <span className="muted">· kiểm soát phát hiện; không ghi Nhật ký. Tài sản mất / hỏng xử lý tiếp bằng ghi giảm sau khi điều tra.</span></h3>
        <div className="toolbar">
          <label>Ngày kiểm kê<input type="date" value={countDate} onChange={e => setCountDate(e.target.value)} /></label>
          <button className="primary" disabled={busy || !!draftCount} title={draftCount ? 'Đang có phiếu kiểm kê chưa chốt' : undefined} onClick={() => act('Đã lập phiếu kiểm kê.', async () => { const c = await post<Count>('/assets/counts', { date: countDate, note: null }); setOpenCount(c.id); setDraft({}) })}>Lập phiếu kiểm kê</button>
          {counts.map(c => <button key={c.id} className={open?.id === c.id ? 'primary' : ''} onClick={() => { setOpenCount(c.id); setDraft({}) }}>{c.number} · {c.status === 'Draft' ? 'đang kiểm' : c.status === 'Completed' ? 'đã chốt' : 'đã hủy'}</button>)}
        </div>
        {open && <>
          <p>Phiếu <b>{open.number}</b> ngày {formatDate(open.countDate)}: đã kiểm {open.counted}/{open.total}; không thấy <b>{open.missing}</b> (giá trị còn lại {formatMoney(open.missingNetBookValue)}); hỏng <b>{open.damaged}</b>; khác bộ phận theo sổ <b>{open.moved}</b>.</p>
          <table className="grid"><thead><tr><th>Tài sản</th><th>Bộ phận theo sổ</th><th>Kết quả</th><th>Bộ phận thực tế</th><th>Ghi chú</th><th className="num">Giá trị còn lại</th></tr></thead>
            <tbody>{open.lines.map(l => { const d = lineOf(l); const edit = open.status === 'Draft'; return <tr key={l.assetId}><td>{l.code} – {l.name}</td><td>{l.bookDepartment ?? ''}</td>
              <td>{edit ? <select aria-label={`Kết quả ${l.code}`} value={d.result} onChange={e => setDraft({ ...draft, [l.assetId]: { ...d, result: e.target.value as CountLine['result'] } })}>{(Object.keys(RESULT_VI) as CountLine['result'][]).map(k => <option key={k} value={k}>{RESULT_VI[k]}</option>)}</select> : RESULT_VI[l.result]}</td>
              <td>{edit ? <input aria-label={`Bộ phận thực tế ${l.code}`} value={d.actual} onChange={e => setDraft({ ...draft, [l.assetId]: { ...d, actual: e.target.value } })} /> : l.actualDepartment ?? ''}</td>
              <td>{edit ? <input aria-label={`Ghi chú ${l.code}`} value={d.note} onChange={e => setDraft({ ...draft, [l.assetId]: { ...d, note: e.target.value } })} /> : l.note ?? ''}</td><td className="num">{formatMoney(l.netBookValue)}</td></tr> })}</tbody></table>
          {open.status === 'Draft' && <div className="toolbar">
            <button disabled={busy || Object.keys(draft).length === 0} onClick={() => act('Đã lưu kết quả kiểm kê.', async () => { await put(`/assets/counts/${open.id}/lines`, Object.entries(draft).map(([assetId, d]) => ({ assetId, result: d.result, actualDepartment: d.actual || null, note: d.note || null }))); setDraft({}) })}>Lưu kết quả</button>
            <button className="primary" disabled={busy || Object.keys(draft).length > 0 || open.counted < open.total} title="Lưu kết quả và kiểm hết mọi tài sản trước khi chốt" onClick={() => act(`Đã chốt ${open.number}.`, () => post(`/assets/counts/${open.id}/complete`))}>Chốt kiểm kê</button>
            <button disabled={busy} onClick={() => { const reason = ask('Lý do hủy phiếu:'); if (reason) act(`Đã hủy ${open.number}.`, () => post(`/assets/counts/${open.id}/cancel`, { reason })) }}>Hủy phiếu</button></div>}
        </>}
      </section>
    </>
  )
}
