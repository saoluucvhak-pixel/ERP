import { useCallback, useEffect, useState } from 'react'
import { get, post } from './api'
import { ErrorBox, FlowStrip, Modal, useAction } from './common'
import { formatDate, formatMoney } from './money'

interface Doc { id: string; invoiceId: string | null; invoiceNumber: string | null; status: string; relation: string; nature: string; series: string | null; number: string | null; issueDate: string
  taxAuthorityCode: string | null; reason: string | null; lastError: string | null; postError: string | null; attempts: number; outboxState: string | null; nextAttemptAt: string | null; rowVersion: number
  partnerName: string | null; total: string | null; relatedNumber: string | null; provider: string }
interface Notice { id: string; number: string; kind: string; status: string; reason: string; response: string | null; rowVersion: number; createdAt: string; lines: { documentId: string; series: string | null; number: string | null; errorDescription: string }[] }

const STATUS_VI: Record<string, string> = { Issuing: 'Đang chờ nhà cung cấp', Issued: 'Đã phát hành', Rejected: 'Bị từ chối', Replaced: 'Đã bị thay thế', Cancelled: 'Đã hủy' }
const RELATION_VI: Record<string, string> = { Original: 'Hóa đơn gốc', Replacement: 'Thay thế', Adjustment: 'Điều chỉnh' }
const NATURE_VI: Record<string, string> = { Increase: 'tăng', Decrease: 'giảm', Information: 'thông tin', None: '' }
const KIND_VI: Record<string, string> = { Explain: 'Giải trình', Cancel: 'Hủy', Adjust: 'Điều chỉnh', Replace: 'Thay thế' }
const NOTICE_STATUS_VI: Record<string, string> = { Draft: 'Nháp', Sent: 'Đã gửi', Accepted: 'Cơ quan thuế tiếp nhận', Rejected: 'Cơ quan thuế không tiếp nhận' }

/**
 * Hóa đơn điện tử phát hành từ ERP (docs/12): hàng đợi và trạng thái từng hóa đơn (gốc, thay thế, điều chỉnh), phát hành lại an toàn khi mạng lỗi / bị từ chối,
 * điều chỉnh thông tin (không bút toán) và thông báo sai sót Mẫu 04/SS-HĐĐT. Lập hóa đơn và bấm Phát hành ở màn Hóa đơn.
 */
export function EInvoiceView() {
  const [tab, setTab] = useState<'docs' | 'notices'>('docs')
  const [status, setStatus] = useState<{ provider: string; configured: boolean } | null>(null)
  const [docs, setDocs] = useState<Doc[]>([])
  const [notices, setNotices] = useState<Notice[]>([])
  const [attention, setAttention] = useState(false)
  const [info, setInfo] = useState('')
  const [adj, setAdj] = useState<{ doc: Doc; reason: string; field: string; oldV: string; newV: string } | null>(null)
  const [nf, setNf] = useState<{ kind: string; reason: string; pick: Record<string, string> } | null>(null)
  const { busy, error, run } = useAction()
  const load = useCallback(() => run(async () => {
    setStatus(await get('/einvoice/status'))
    setDocs(await get<Doc[]>(`/einvoice/documents${attention ? '?attention=true' : ''}`))
    setNotices(await get<Notice[]>('/einvoice/notices'))
  }), [run, attention])
  // oxlint-disable-next-line react-hooks/exhaustive-deps -- tải lại khi mở màn / đổi bộ lọc
  useEffect(() => { load() }, [attention])
  const needs = (d: Doc) => d.status === 'Issuing' || d.status === 'Rejected' || !!d.postError
  const issued = docs.filter(d => d.status === 'Issued')

  const retry = (d: Doc) => run(async () => { const v = await post<Doc>(`/einvoice/documents/${d.id}/retry`); setInfo(`Hóa đơn ${v.invoiceNumber ?? ''}: ${STATUS_VI[v.status] ?? v.status}${v.postError ? ` — chưa ghi sổ: ${v.postError}` : ''}`); await load() })
  const processAll = () => run(async () => { const r = await post<{ dispatched: number; issued: number; rejected: number; pending: number }>('/einvoice/process-pending'); setInfo(`Đã gửi ${r.dispatched} lệnh: ${r.issued} phát hành, ${r.rejected} bị từ chối, ${r.pending} còn chờ.`); await load() })
  const sendNotice = (n: Notice) => run(async () => { const v = await post<Notice>(`/einvoice/notices/${n.id}/send`, { rowVersion: n.rowVersion }); setInfo(`Thông báo ${v.number}: ${NOTICE_STATUS_VI[v.status] ?? v.status}.`); await load() })

  return (
    <>
      <div className="toolbar"><h2>Hóa đơn điện tử</h2>
        <button className={tab === 'docs' ? 'primary' : ''} onClick={() => setTab('docs')}>Hàng đợi và hóa đơn đã phát hành</button>
        <button className={tab === 'notices' ? 'primary' : ''} onClick={() => setTab('notices')}>Thông báo sai sót (04/SS-HĐĐT)</button>
        <button disabled={busy || !status?.configured} onClick={processAll}>Gửi các lệnh đang chờ</button></div>
      <FlowStrip steps={[
        { label: 'Hóa đơn nháp → Phát hành (ký số, gửi cơ quan thuế)', journal: false, screen: 'trade', count: `${docs.filter(d => d.status === 'Issuing').length} đang chờ · ${docs.filter(d => d.status === 'Rejected').length} bị từ chối` },
        { label: 'Cơ quan thuế chấp nhận → số, ký hiệu, mã được gán → ghi sổ tự động', journal: true, count: `${issued.length} đã phát hành · ${docs.filter(d => d.postError).length} chưa ghi sổ được` },
        { label: 'Sai sót: điều chỉnh / thay thế (có bút toán) hoặc thông báo 04/SS (không bút toán)', journal: false, count: `${notices.filter(n => n.status === 'Draft').length} thông báo nháp` },
        { label: 'Đối soát HĐĐT ↔ sổ ↔ tờ khai', journal: false, screen: 'vat' },
      ]} />
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      {status && !status.configured && <div className="msg error" role="alert">Chưa cấu hình nhà cung cấp hóa đơn điện tử (Hak:EInvoice:Provider) nên chưa phát hành được. Có thể xem dữ liệu đã có.</div>}
      {status?.configured && status.provider === 'SIMULATED' && <div className="msg">Đang dùng nhà cung cấp GIẢ LẬP (chỉ để thử): hóa đơn tạo ra không có giá trị pháp lý.</div>}

      {tab === 'docs' && <>
        <div className="toolbar"><label><input type="checkbox" checked={attention} onChange={e => setAttention(e.target.checked)} /> Chỉ hiện hóa đơn cần xử lý</label>
          {issued.length > 0 && <button disabled={busy} onClick={() => setNf({ kind: 'Explain', reason: '', pick: {} })}>Lập thông báo sai sót…</button>}</div>
        <div className="scroll"><table className="grid">
          <thead><tr><th>Số / ký hiệu</th><th>Hóa đơn ERP</th><th>Loại</th><th>Trạng thái</th><th>Khách hàng</th><th className="num">Tổng</th><th>Ghi chú</th><th /></tr></thead>
          <tbody>{docs.map(d => <tr key={d.id} style={{ opacity: d.status === 'Replaced' || d.status === 'Cancelled' ? 0.55 : undefined }}>
            <td>{d.number ? `${d.series}/${d.number}` : <span className="muted">chưa có số</span>}<div className="muted">{formatDate(d.issueDate)}{d.taxAuthorityCode ? ` · mã CQT ${d.taxAuthorityCode.slice(0, 10)}…` : ''}</div></td>
            <td>{d.invoiceNumber ?? <span className="muted">không có bút toán</span>}</td>
            <td>{RELATION_VI[d.relation]}{d.nature !== 'None' ? ` (${NATURE_VI[d.nature]})` : ''}{d.relatedNumber ? <div className="muted">cho {d.relatedNumber}</div> : null}</td>
            <td><span className={`badge ${d.status === 'Issued' ? 'Posted' : d.status === 'Rejected' ? 'Locked' : ''}`}>{STATUS_VI[d.status] ?? d.status}</span>{d.status === 'Issuing' && d.attempts > 0 && <div className="muted">đã thử {d.attempts} lần</div>}</td>
            <td>{d.partnerName}</td><td className="num">{d.total ? formatMoney(d.total) : ''}</td>
            <td style={{ whiteSpace: 'normal', maxWidth: 360 }}>{d.postError ? <span style={{ color: 'var(--danger)' }}>Đã phát hành nhưng chưa ghi sổ: {d.postError}</span> : d.lastError ? <span style={{ color: 'var(--danger)' }}>{d.lastError}</span> : d.reason}</td>
            <td style={{ display: 'flex', gap: 4, flexWrap: 'wrap' }}>
              {needs(d) && <button className="primary" disabled={busy || !status?.configured} onClick={() => retry(d)}>{d.status === 'Rejected' ? 'Phát hành lại' : d.postError ? 'Ghi sổ lại' : 'Gửi lại'}</button>}
              {d.status === 'Issued' && d.relation !== 'Adjustment' && <button disabled={busy || !status?.configured} onClick={() => setAdj({ doc: d, reason: '', field: '', oldV: '', newV: '' })}>Điều chỉnh thông tin</button>}</td></tr>)}
            {docs.length === 0 && <tr><td colSpan={8} className="muted">{attention ? 'Không có hóa đơn nào cần xử lý.' : 'Chưa có hóa đơn phát hành từ HAK. Lập hóa đơn bán (chọn “lấy số từ nhà cung cấp”) rồi bấm Phát hành ở màn Hóa đơn.'}</td></tr>}</tbody>
        </table></div>
        <p className="muted">Hóa đơn đã phát hành không sửa được: sai tiền / thuế / hàng hóa thì lập hóa đơn điều chỉnh hoặc thay thế (màn Hóa đơn); sai thông tin không đổi tiền thì “Điều chỉnh thông tin” (không bút toán) hoặc thông báo sai sót. Các trường hợp được phép do kế toán trưởng xác minh theo quy định hiện hành [CẦN XÁC MINH].</p>
      </>}

      {tab === 'notices' && <div className="scroll"><table className="grid">
        <thead><tr><th>Số</th><th>Loại</th><th>Hóa đơn</th><th>Lý do</th><th>Trạng thái</th><th /></tr></thead>
        <tbody>{notices.map(n => <tr key={n.id}><td>{n.number}<div className="muted">{formatDate(n.createdAt.slice(0, 10))}</div></td><td>{KIND_VI[n.kind]}</td>
          <td style={{ whiteSpace: 'normal' }}>{n.lines.map(l => <div key={l.documentId}>{l.series}/{l.number}: <span className="muted">{l.errorDescription}</span></div>)}</td>
          <td style={{ whiteSpace: 'normal' }}>{n.reason}</td><td>{NOTICE_STATUS_VI[n.status]}{n.response && <div className="muted">{n.response}</div>}</td>
          <td>{n.status === 'Draft' && <button className="primary" disabled={busy || !status?.configured} onClick={() => sendNotice(n)}>Gửi cơ quan thuế</button>}</td></tr>)}
          {notices.length === 0 && <tr><td colSpan={6} className="muted">Chưa có thông báo sai sót. Thông báo không phát sinh bút toán; loại “Hủy” được tiếp nhận sẽ đánh dấu hóa đơn đã hủy và đối soát sẽ nhắc đảo bút toán.</td></tr>}</tbody>
      </table></div>}

      {adj && <Modal title={`Điều chỉnh thông tin — ${adj.doc.series}/${adj.doc.number}`} onClose={() => setAdj(null)} busy={busy}
        footer={<><button onClick={() => setAdj(null)}>Đóng</button><button className="primary" disabled={busy || !adj.reason.trim() || !adj.field.trim() || !adj.newV.trim()} onClick={() => run(async () => {
          const v = await post<Doc>(`/einvoice/documents/${adj.doc.id}/adjust-information`, { reason: adj.reason, changes: [{ field: adj.field, oldValue: adj.oldV, newValue: adj.newV }] })
          setAdj(null); setInfo(`Đã phát hành hóa đơn điều chỉnh thông tin ${v.series}/${v.number} (không phát sinh bút toán).`); await load()
        })}>Phát hành</button></>}>
        <div className="form-row">
          <label style={{ flex: 1, minWidth: 220 }}>Lý do<input value={adj.reason} onChange={e => setAdj({ ...adj, reason: e.target.value })} placeholder="Sai địa chỉ người mua…" /></label>
          <label>Thông tin sai<input value={adj.field} onChange={e => setAdj({ ...adj, field: e.target.value })} placeholder="Địa chỉ" /></label>
          <label>Ghi sai<input value={adj.oldV} onChange={e => setAdj({ ...adj, oldV: e.target.value })} /></label>
          <label>Đúng là<input value={adj.newV} onChange={e => setAdj({ ...adj, newV: e.target.value })} /></label>
        </div>
        <p className="muted">Chỉ dùng cho sai sót không làm đổi tiền hàng, thuế, người mua (mã số thuế) [CẦN XÁC MINH danh mục]. Sai tiền, thuế: lập hóa đơn điều chỉnh có tiền hoặc thay thế.</p>
      </Modal>}

      {nf && <Modal title="Lập thông báo sai sót (04/SS-HĐĐT)" onClose={() => setNf(null)} busy={busy}
        footer={<><button onClick={() => setNf(null)}>Đóng</button><button className="primary" disabled={busy || !nf.reason.trim() || Object.keys(nf.pick).length === 0} onClick={() => run(async () => {
          const n = await post<Notice>('/einvoice/notices', { kind: nf.kind, reason: nf.reason, lines: Object.entries(nf.pick).map(([documentId, errorDescription]) => ({ documentId, errorDescription })) })
          setNf(null); setTab('notices'); setInfo(`Đã lập thông báo ${n.number} (nháp). Bấm “Gửi cơ quan thuế” khi sẵn sàng.`); await load()
        })}>Lưu nháp</button></>}>
        <div className="form-row">
          <label>Loại xử lý<select value={nf.kind} onChange={e => setNf({ ...nf, kind: e.target.value })}>{Object.entries(KIND_VI).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</select></label>
          <label style={{ flex: 1, minWidth: 220 }}>Lý do chung<input value={nf.reason} onChange={e => setNf({ ...nf, reason: e.target.value })} /></label>
        </div>
        <table className="grid"><thead><tr><th /><th>Hóa đơn</th><th>Mô tả sai sót</th></tr></thead>
          <tbody>{issued.map(d => <tr key={d.id}><td><input type="checkbox" aria-label={`Chọn ${d.series}/${d.number}`} checked={d.id in nf.pick} onChange={e => { const p = { ...nf.pick }; if (e.target.checked) p[d.id] = ''; else delete p[d.id]; setNf({ ...nf, pick: p }) }} /></td>
            <td>{d.series}/{d.number} · {d.partnerName}</td>
            <td>{d.id in nf.pick && <input aria-label="Mô tả sai sót" value={nf.pick[d.id]} onChange={e => setNf({ ...nf, pick: { ...nf.pick, [d.id]: e.target.value } })} />}</td></tr>)}</tbody></table>
      </Modal>}
    </>
  )
}
