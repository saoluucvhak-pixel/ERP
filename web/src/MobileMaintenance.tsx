import { useCallback, useEffect, useRef, useState } from 'react'
import { api, get, post } from './api'
import { ErrorBox, useAction } from './common'
import { formatDate, formatDateTime, today } from './money'
import { Photos, uploadPhoto } from './Photos'
import { assetCodeFromScan } from './qr'

interface Asset { id: string; code: string; name: string; class: 'FixedAsset' | 'Tool'; status: string }
interface Assign { assetId: string; department: string | null; custodian: string | null; location: string | null }
interface Req { id: string; number: string; assetId: string; priority: string; description: string; reportedAt: string; status: string; workOrderNumber: string | null }
interface Repair { id: string; number: string; assetId: string; status: string; description: string; assignedTech: string | null; reportedDate: string }

const PRIORITY_VI: Record<string, string> = { Low: 'Thấp', Medium: 'Trung bình', High: 'Cao', Urgent: 'Khẩn cấp' }
const REQ_VI: Record<string, string> = { New: 'Mới', Surveying: 'Đang khảo sát', Proposed: 'Đã đề xuất phương án', Approved: 'Đã duyệt', InProgress: 'Đang sửa', Done: 'Đã xong', Rejected: 'Từ chối', Cancelled: 'Đã hủy' }
const REPAIR_VI: Record<string, string> = { Draft: 'Nháp', PendingApproval: 'Chờ duyệt', Approved: 'Đã duyệt', InProgress: 'Đang thi công', Testing: 'Nghiệm thu thử', Completed: 'Hoàn thành', Closed: 'Đã đóng', Rejected: 'Từ chối', Cancelled: 'Đã hủy' }

export const mobileCode = () => { const m = /^#\/m\/a\/([^/?#]+)/.exec(window.location.hash); try { return m ? decodeURIComponent(m[1]) : null } catch { return m?.[1] ?? null } }
export const isMobileRoute = () => window.location.hash.startsWith('#/m')

/**
 * Trang bảo trì trên điện thoại (mở bằng cách quét mã QR dán trên máy, địa chỉ dạng /#/m/a/{mã tài sản}): xem máy, báo sự cố kèm ảnh chụp, xem phiếu yêu cầu và
 * phiếu thi công của máy này, thêm ảnh hiện trường. Dùng camera điện thoại để quét; quét ngay trong trang chỉ có khi trình duyệt cho phép (HTTPS hoặc localhost).
 */
export function MobileMaintenance({ company, onFull, onLogout }: { company: string; onFull: () => void; onLogout: () => void }) {
  const [code, setCode] = useState<string | null>(mobileCode())
  useEffect(() => { const h = () => setCode(mobileCode()); window.addEventListener('hashchange', h); return () => window.removeEventListener('hashchange', h) }, [])
  return (
    <div className="mobile-app">
      <header><b>Bảo trì</b><span className="muted">{company}</span>
        <button onClick={onFull}>Bản đầy đủ</button><button onClick={onLogout}>Thoát</button></header>
      {code ? <AssetPage code={code} /> : <Home />}
    </div>
  )
}

function Home() {
  const [manual, setManual] = useState('')
  const [scanning, setScanning] = useState(false)
  const [msg, setMsg] = useState('')
  const video = useRef<HTMLVideoElement>(null)
  const canScan = typeof window !== 'undefined' && window.isSecureContext && 'BarcodeDetector' in window && !!navigator.mediaDevices?.getUserMedia
  const go = (text: string) => { const c = assetCodeFromScan(text); if (!c) { setMsg('Không nhận ra mã tài sản.'); return false } window.location.hash = `#/m/a/${encodeURIComponent(c)}`; return true }
  useEffect(() => {
    if (!scanning) return
    let stop = false; let stream: MediaStream | null = null
    ;(async () => {
      try {
        stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' } })
        if (video.current) { video.current.srcObject = stream; await video.current.play() }
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const det = new (window as any).BarcodeDetector({ formats: ['qr_code'] })
        while (!stop && video.current) {
          const found = await det.detect(video.current).catch(() => [])
          if (found[0]?.rawValue && go(found[0].rawValue)) break
          await new Promise(r => setTimeout(r, 300))
        }
      } catch { setMsg('Không mở được camera; dùng camera điện thoại quét mã QR hoặc nhập mã bên dưới.') }
      setScanning(false)
    })()
    return () => { stop = true; stream?.getTracks().forEach(t => t.stop()) }
  }, [scanning])
  return (
    <>
    <section className="panel">
      <h2>Quét mã QR trên máy</h2>
      {canScan ? <>
        {scanning ? <video ref={video} playsInline muted style={{ width: '100%', borderRadius: 8 }} /> : <button className="primary" onClick={() => { setMsg(''); setScanning(true) }}>📷 Mở camera quét</button>}
        {scanning && <button onClick={() => setScanning(false)}>Dừng</button>}
      </> : <p className="muted">Mở ứng dụng Camera của điện thoại, hướng vào mã QR dán trên máy — điện thoại sẽ mở đúng trang của máy đó. (Quét ngay trong trang cần kết nối HTTPS.)</p>}
      {msg && <div className="msg error">{msg}</div>}
      <label>Hoặc nhập mã tài sản<input value={manual} onChange={e => setManual(e.target.value)} onKeyDown={e => { if (e.key === 'Enter' && manual.trim()) go(manual) }} placeholder="Ví dụ TSCD-001" autoCapitalize="characters" /></label>
      <button disabled={!manual.trim()} onClick={() => go(manual)}>Mở máy</button>
    </section>
    <MobileTimesheet />
    </>
  )
}

interface SvcOrder { id: string; number: string; kind: string; status: string; description: string; lines: { lineNo: number; itemId: string; quantity: string }[] }
interface SvcItem { id: string; code: string; name: string; unit: string; isService?: boolean }
interface TimeRow { id: string; orderNumber: string; orderLineNo: number; itemCode: string; workDate: string; worker: string; hours: string; locked: boolean }

/** Chấm giờ dịch vụ nhanh trên điện thoại: chọn dòng dịch vụ của đơn bán đã xác nhận, ghi người thực hiện và số giờ; xem và xóa các dòng chưa chốt. */
function MobileTimesheet() {
  const [lines, setLines] = useState<{ key: string; orderId: string; lineNo: number; label: string }[]>([])
  const [rows, setRows] = useState<TimeRow[]>([])
  const [f, setF] = useState({ key: '', date: today(), worker: localStorage.getItem('hak.worker') ?? '', hours: '', note: '' })
  const [info, setInfo] = useState('')
  const { busy, error, run, setError } = useAction()
  const load = useCallback(() => run(async () => {
    const items = await get<SvcItem[]>('/items')
    const orders = (await get<SvcOrder[]>('/orders?kind=Sales')).filter(o => o.status === 'Confirmed')
    setLines(orders.flatMap(o => o.lines.filter(l => items.find(i => i.id === l.itemId)?.isService).map(l => { const it = items.find(i => i.id === l.itemId)!; return { key: `${o.id}:${l.lineNo}`, orderId: o.id, lineNo: l.lineNo, label: `${o.number} · ${it.name} (${it.unit})` } })))
    setRows((await get<TimeRow[]>('/service-progress/time?openOnly=true')).slice(0, 15))
  }), [run])
  useEffect(() => { load() }, [load])
  const sel = lines.find(l => l.key === f.key)
  if (lines.length === 0) return null
  return (
    <section className="panel">
      <h2>Chấm giờ dịch vụ</h2>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      <label>Dịch vụ<select aria-label="Dịch vụ chấm giờ" value={f.key} onChange={e => setF({ ...f, key: e.target.value })}><option value="">— chọn —</option>{lines.map(l => <option key={l.key} value={l.key}>{l.label}</option>)}</select></label>
      <label>Ngày<input type="date" value={f.date} onChange={e => setF({ ...f, date: e.target.value })} /></label>
      <label>Người thực hiện<input value={f.worker} onChange={e => setF({ ...f, worker: e.target.value })} /></label>
      <label>Số giờ<input inputMode="decimal" value={f.hours} onChange={e => setF({ ...f, hours: e.target.value.replace(/[^0-9.]/g, '') })} placeholder="Ví dụ 4 hoặc 1.5" /></label>
      <label>Ghi chú<input value={f.note} onChange={e => setF({ ...f, note: e.target.value })} /></label>
      <button className="primary" disabled={busy || !sel || !f.worker.trim() || !f.hours} onClick={() => {
        const h = Number(f.hours)
        if (!h || h <= 0 || h > 24) { setError(new Error('Số giờ phải lớn hơn 0 và không quá 24.')); return }
        run(async () => {
          await post('/service-progress/time', { orderId: sel!.orderId, orderLineNo: sel!.lineNo, workDate: f.date, worker: f.worker, hours: h, note: f.note || null })
          try { localStorage.setItem('hak.worker', f.worker) } catch { /* bỏ qua */ }
          setF({ ...f, hours: '', note: '' }); setInfo('Đã ghi giờ.'); await load()
        })
      }}>Ghi giờ</button>
      {rows.length > 0 && <><h3>Giờ chưa chốt gần đây</h3>
        <ul>{rows.map(r => <li key={r.id}>{formatDate(r.workDate)} · {r.itemCode} · {r.worker} · <b>{r.hours} giờ</b> <button aria-label="Xóa dòng giờ" disabled={busy} onClick={() => run(async () => { await api('DELETE', `/service-progress/time/${r.id}`); await load() })}>✕</button></li>)}</ul></>}
    </section>
  )
}

function AssetPage({ code }: { code: string }) {
  const [asset, setAsset] = useState<Asset | null | undefined>(undefined)
  const [assign, setAssign] = useState<Assign | null>(null)
  const [reqs, setReqs] = useState<Req[]>([])
  const [repairs, setRepairs] = useState<Repair[]>([])
  const [form, setForm] = useState({ priority: 'Medium', description: '', files: [] as File[] })
  const [info, setInfo] = useState('')
  const { busy, error, run, setError } = useAction()
  const pick = useRef<HTMLInputElement>(null)
  const load = useCallback(() => run(async () => {
    const all = await get<Asset[]>(`/assets?asOf=${today()}`)
    const a = all.find(x => x.code.toLowerCase() === code.toLowerCase() && x.status !== 'Disposed') ?? all.find(x => x.code.toLowerCase() === code.toLowerCase()) ?? null
    setAsset(a)
    if (!a) return
    setAssign((await get<Assign[]>('/assets/assignments')).find(x => x.assetId === a.id) ?? null)
    setReqs(await get<Req[]>(`/assets/maintenance/requests?assetId=${a.id}`))
    setRepairs((await get<Repair[]>(`/assets/repairs?assetId=${a.id}`)).filter(r => !['Closed', 'Cancelled', 'Rejected'].includes(r.status)))
  }), [run, code])
  useEffect(() => { load() }, [load])

  const send = () => run(async () => {
    if (!asset) return
    if (!form.description.trim()) { setError(new Error('Mô tả hiện tượng hư hỏng.')); return }
    const r = await post<{ id: string; number: string }>('/assets/maintenance/requests', { assetId: asset.id, priority: form.priority, description: form.description })
    let failed = 0
    for (const f of form.files) { try { await uploadPhoto('maintenance_request', r.id, f) } catch { failed++ } }
    setForm({ priority: 'Medium', description: '', files: [] })
    setInfo(`Đã gửi báo sự cố ${r.number}${form.files.length ? ` kèm ${form.files.length - failed}/${form.files.length} ảnh` : ''}.${failed ? ' Ảnh chưa gửi được có thể thêm lại ở phiếu bên dưới.' : ''}`)
    await load()
  })

  if (asset === undefined) return <section className="panel"><ErrorBox error={error} /><span className="muted">Đang tải…</span></section>
  if (asset === null) return <section className="panel"><ErrorBox error={error} /><div className="msg error">Không tìm thấy tài sản mã “{code}”.</div><a href="#/m">← Quét / nhập mã khác</a></section>
  return (
    <>
      <section className="panel">
        <a href="#/m">← Quét máy khác</a>
        <h2>{asset.name}</h2>
        <div className="muted">{asset.code} · {asset.class === 'FixedAsset' ? 'TSCĐ' : 'CCDC'}{asset.status !== 'Active' ? ` · ${asset.status}` : ''}</div>
        {assign?.department && <div>Bộ phận: <b>{assign.department}</b>{assign.custodian ? ` · người giữ ${assign.custodian}` : ''}{assign.location ? ` · ${assign.location}` : ''}</div>}
      </section>
      <section className="panel">
        <h3>Báo sự cố</h3>
        <ErrorBox error={error} />
        {info && !error && <div className="msg ok">{info}</div>}
        <label>Mức độ<select value={form.priority} onChange={e => setForm({ ...form, priority: e.target.value })}>{Object.entries(PRIORITY_VI).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</select></label>
        <label>Hiện tượng<textarea rows={3} value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} placeholder="Máy kêu to, rò dầu, không khởi động…" /></label>
        <div className="photo-row">
          <button type="button" onClick={() => pick.current?.click()}>📷 Chụp / chọn ảnh ({form.files.length})</button>
          <input ref={pick} type="file" accept="image/*" capture="environment" multiple hidden onChange={e => { const fs = [...(e.target.files ?? [])]; e.target.value = ''; setForm(f => ({ ...f, files: [...f.files, ...fs].slice(0, 12) })) }} />
          {form.files.length > 0 && <button type="button" onClick={() => setForm({ ...form, files: [] })}>Bỏ ảnh</button>}
        </div>
        <button className="primary" disabled={busy || !form.description.trim()} onClick={send}>{busy ? 'Đang gửi…' : 'Gửi báo sự cố'}</button>
      </section>
      {repairs.length > 0 && <section className="panel"><h3>Phiếu thi công đang xử lý</h3>
        {repairs.map(r => <div key={r.id} className="m-card"><b>{r.number}</b> · {REPAIR_VI[r.status] ?? r.status}{r.assignedTech ? ` · ${r.assignedTech}` : ''}<div>{r.description}</div>
          <Photos entityType="asset_repair" entityId={r.id} /></div>)}</section>}
      <section className="panel"><h3>Yêu cầu sửa chữa gần đây</h3>
        {reqs.length === 0 && <span className="muted">Chưa có.</span>}
        {reqs.slice(0, 10).map(r => <div key={r.id} className="m-card"><b>{r.number}</b> · {REQ_VI[r.status] ?? r.status} · {PRIORITY_VI[r.priority] ?? r.priority}{r.workOrderNumber ? ` · phiếu ${r.workOrderNumber}` : ''}
          <div className="muted">{formatDateTime(r.reportedAt)}</div><div>{r.description}</div><Photos entityType="maintenance_request" entityId={r.id} /></div>)}
      </section>
    </>
  )
}
