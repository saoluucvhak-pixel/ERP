import { useCallback, useEffect, useRef, useState } from 'react'
import { get, session } from './api'
import { ErrorBox, useAction } from './common'
import { formatDateTime } from './money'

export interface PhotoMeta { id: string; fileName: string; contentType: string; sizeBytes: number; caption: string | null; uploadedAt: string }

/** Thu nhỏ ảnh chụp từ camera (cạnh dài tối đa 1600 px, JPEG 0,8) trước khi gửi: tiết kiệm đường truyền LAN / Wi-Fi và dung lượng lưu trữ. */
export async function shrinkImage(file: File, maxSide = 1600, quality = 0.8): Promise<Blob> {
  if (!/^image\/(jpeg|png|webp)$/.test(file.type)) return file          // định dạng khác (ví dụ HEIC) để máy chủ từ chối với thông báo rõ ràng
  try {
    const bmp = await createImageBitmap(file)
    const scale = Math.min(1, maxSide / Math.max(bmp.width, bmp.height))
    if (scale === 1 && file.size < 1_500_000) { bmp.close(); return file }
    const canvas = document.createElement('canvas')
    canvas.width = Math.round(bmp.width * scale); canvas.height = Math.round(bmp.height * scale)
    canvas.getContext('2d')!.drawImage(bmp, 0, 0, canvas.width, canvas.height)
    bmp.close()
    const blob = await new Promise<Blob | null>(res => canvas.toBlob(res, 'image/jpeg', quality))
    return blob && blob.size < file.size ? blob : file
  } catch { return file }
}

/** Gửi một ảnh lên phiếu (thân yêu cầu là chính ảnh; máy chủ kiểm tra chữ ký tệp, tối đa 8 MB). */
export async function uploadPhoto(entityType: 'maintenance_request' | 'asset_repair', entityId: string, file: File, caption?: string) {
  const blob = await shrinkImage(file)
  const q = new URLSearchParams({ fileName: file.name || 'anh.jpg' }); if (caption) q.set('caption', caption)
  const headers: Record<string, string> = { 'Content-Type': blob.type || 'application/octet-stream', 'X-Device': 'web' }
  if (session.token) headers.Authorization = `Bearer ${session.token}`
  if (session.companyId) headers['X-Company-Id'] = session.companyId
  let res: Response
  try { res = await fetch(`/api/attachments/${entityType}/${entityId}?${q}`, { method: 'POST', headers, body: blob }) }
  catch { throw new Error('Không kết nối được máy chủ HAK để gửi ảnh.') }
  if (!res.ok) { const d = await res.json().catch(() => null); throw new Error(d?.message ?? `Gửi ảnh không được (mã ${res.status}).`) }
}

function useBlobUrl(id: string) {
  const [url, setUrl] = useState<string | null>(null)
  useEffect(() => {
    let alive = true; let made: string | null = null
    const headers: Record<string, string> = { 'X-Device': 'web' }
    if (session.token) headers.Authorization = `Bearer ${session.token}`
    if (session.companyId) headers['X-Company-Id'] = session.companyId
    fetch(`/api/attachments/file/${id}`, { headers }).then(r => (r.ok ? r.blob() : Promise.reject())).then(b => { if (alive) { made = URL.createObjectURL(b); setUrl(made) } }).catch(() => undefined)
    return () => { alive = false; if (made) URL.revokeObjectURL(made) }
  }, [id])
  return url
}

function Thumb({ p, onOpen }: { p: PhotoMeta; onOpen: (url: string) => void }) {
  const url = useBlobUrl(p.id)
  return url ? <button type="button" className="photo-thumb" onClick={() => onOpen(url)} title={`${p.fileName} · ${formatDateTime(p.uploadedAt)}`}><img src={url} alt={p.caption ?? p.fileName} /></button> : <span className="photo-thumb muted">…</span>
}

/** Ảnh đính kèm của một phiếu: xem ảnh thu nhỏ, bấm để xem lớn, chụp / chọn thêm ảnh (ảnh là chứng cứ nên không xóa). */
export function Photos({ entityType, entityId, canAdd = true, onChange }: { entityType: 'maintenance_request' | 'asset_repair'; entityId: string; canAdd?: boolean; onChange?: () => void }) {
  const [list, setList] = useState<PhotoMeta[]>([])
  const [big, setBig] = useState<string | null>(null)
  const { busy, error, run } = useAction()
  const input = useRef<HTMLInputElement>(null)
  const load = useCallback(() => run(async () => setList(await get<PhotoMeta[]>(`/attachments/${entityType}/${entityId}`))), [run, entityType, entityId])
  useEffect(() => { load() }, [load])
  return (
    <div className="photos">
      <ErrorBox error={error} />
      <div className="photo-row">
        {list.map(p => <Thumb key={p.id} p={p} onOpen={setBig} />)}
        {list.length === 0 && <span className="muted">Chưa có ảnh.</span>}
        {canAdd && <>
          <button type="button" disabled={busy} onClick={() => input.current?.click()}>📷 Thêm ảnh</button>
          <input ref={input} type="file" accept="image/*" capture="environment" multiple hidden onChange={e => {
            const files = [...(e.target.files ?? [])]; e.target.value = ''
            if (files.length) run(async () => { for (const f of files) await uploadPhoto(entityType, entityId, f); await load(); onChange?.() })
          }} />
        </>}
      </div>
      {big && <div className="photo-big" role="dialog" aria-label="Xem ảnh" onClick={() => setBig(null)}><img src={big} alt="Ảnh phóng to" /></div>}
    </div>
  )
}

/** Nút "📷 n ảnh" trong danh sách: bấm để mở / đóng khung ảnh của phiếu (không tải ảnh của cả danh sách cùng lúc). */
export function PhotosToggle({ entityType, entityId, count, onChange }: { entityType: 'maintenance_request' | 'asset_repair'; entityId: string; count: number; onChange?: () => void }) {
  const [open, setOpen] = useState(false)
  return <div>
    <button type="button" onClick={() => setOpen(o => !o)} aria-expanded={open}>📷 {count > 0 ? `${count} ảnh` : 'Thêm ảnh'}</button>
    {open && <Photos entityType={entityType} entityId={entityId} onChange={onChange} />}
  </div>
}
