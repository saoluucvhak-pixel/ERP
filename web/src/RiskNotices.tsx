import { useCallback, useEffect, useState } from 'react'
import { get, post } from './api'
import { ErrorBox, Modal, useAction } from './common'
import { formatDate, today } from './money'

interface Line { series: string; invoiceNo: string; issueDate: string | null; issue: string; matchedInHak: boolean }
interface Risk { id: string; number: string; authorityNoticeNo: string; noticeDate: string; receivedDate: string; responseDeadline: string | null; reason: string; status: 'Received' | 'Responded' | 'Closed'
  responseNote: string | null; responseNoticeNumber: string | null; overdue: boolean; daysLeft: number | null; rowVersion: number; lines: Line[] }
interface Notice { id: string; number: string; reason: string }

const STATUS_VI = { Received: 'Chờ phản hồi', Responded: 'Đã phản hồi', Closed: 'Đã đóng' }

/** Thông báo 01/TB-RSĐT của cơ quan thuế (nhận vào): ghi nhận, theo dõi hạn, phản hồi (giải trình và/hoặc gắn thông báo sai sót 04/SS), đóng. Không đổi hóa đơn / sổ. */
export function RiskNotices({ configured }: { configured: boolean }) {
  void configured
  const [rows, setRows] = useState<Risk[]>([])
  const [notices, setNotices] = useState<Notice[]>([])
  const [info, setInfo] = useState('')
  const [form, setForm] = useState<{ no: string; noticeDate: string; receivedDate: string; deadline: string; reason: string; lines: { series: string; invoiceNo: string; issueDate: string; issue: string }[] } | null>(null)
  const [resp, setResp] = useState<{ r: Risk; note: string; noticeId: string } | null>(null)
  const { busy, error, run } = useAction()
  const load = useCallback(() => run(async () => {
    setRows(await get<Risk[]>('/einvoice/risk-notices'))
    setNotices(await get<Notice[]>('/einvoice/notices'))
  }), [run])
  useEffect(() => { load() }, [load])
  return (
    <section>
      <div className="toolbar"><h3>Thông báo của cơ quan thuế về hóa đơn có rủi ro (01/TB-RSĐT)</h3>
        <button className="primary" onClick={() => setForm({ no: '', noticeDate: today(), receivedDate: today(), deadline: '', reason: '', lines: [{ series: '', invoiceNo: '', issueDate: '', issue: '' }] })}>Ghi nhận thông báo…</button></div>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      <div className="scroll"><table className="grid">
        <thead><tr><th>Số / ngày</th><th>Số của cơ quan thuế</th><th>Hóa đơn nêu trong thông báo</th><th>Hạn phản hồi</th><th>Trạng thái</th><th /></tr></thead>
        <tbody>{rows.map(r => <tr key={r.id}>
          <td>{r.number}<div className="muted">nhận {formatDate(r.receivedDate)}</div></td><td>{r.authorityNoticeNo}<div className="muted">ngày {formatDate(r.noticeDate)}</div></td>
          <td style={{ whiteSpace: 'normal' }}>{r.lines.map((l, i) => <div key={i}>{l.series}/{l.invoiceNo}{l.matchedInHak ? ' ✓' : ''}: <span className="muted">{l.issue}</span></div>)}<div className="muted">{r.reason}</div></td>
          <td>{r.responseDeadline ? formatDate(r.responseDeadline) : '—'}{r.overdue && <div style={{ color: 'var(--danger)' }}>quá hạn</div>}{!r.overdue && r.daysLeft != null && <div className="muted">còn {r.daysLeft} ngày</div>}</td>
          <td>{STATUS_VI[r.status]}{r.responseNote && <div className="muted">{r.responseNote}</div>}{r.responseNoticeNumber && <div className="muted">kèm {r.responseNoticeNumber}</div>}</td>
          <td style={{ display: 'flex', gap: 4 }}>
            {r.status === 'Received' && <button className="primary" disabled={busy} onClick={() => setResp({ r, note: '', noticeId: '' })}>Phản hồi…</button>}
            {r.status === 'Responded' && <button disabled={busy} onClick={() => run(async () => { await post(`/einvoice/risk-notices/${r.id}/close`, { rowVersion: r.rowVersion }); setInfo(`Đã đóng ${r.number}.`); await load() })}>Đóng</button>}</td></tr>)}
          {rows.length === 0 && <tr><td colSpan={6} className="muted">Chưa có thông báo của cơ quan thuế. Ghi nhận khi nhận được văn bản; HAK theo dõi hạn và việc phản hồi, không tự đổi hóa đơn.</td></tr>}</tbody></table></div>
      <p className="muted">✓ = hóa đơn phát hành từ HAK (tự ghép theo ký hiệu + số). Hạn phản hồi nhập theo nội dung thông báo [CẦN XÁC MINH thời hạn theo Thông tư 91/2026]. Sai sót phải sửa thì lập hóa đơn điều chỉnh / thay thế hoặc thông báo 04/SS rồi gắn vào phản hồi.</p>

      {form && <Modal title="Ghi nhận thông báo 01/TB-RSĐT" onClose={() => setForm(null)} busy={busy}
        footer={<><button onClick={() => setForm(null)}>Đóng</button><button className="primary" disabled={busy || !form.no.trim() || !form.reason.trim()} onClick={() => run(async () => {
          const r = await post<Risk>('/einvoice/risk-notices', { authorityNoticeNo: form.no, noticeDate: form.noticeDate, receivedDate: form.receivedDate, responseDeadline: form.deadline || null, reason: form.reason,
            lines: form.lines.filter(l => l.series.trim() || l.invoiceNo.trim()).map(l => ({ series: l.series, invoiceNo: l.invoiceNo, issueDate: l.issueDate || null, issue: l.issue })) })
          setForm(null); setInfo(`Đã ghi nhận ${r.number}.`); await load()
        })}>Lưu</button></>}>
        <div className="form-row"><label>Số thông báo của cơ quan thuế<input value={form.no} onChange={e => setForm({ ...form, no: e.target.value })} /></label>
          <label>Ngày thông báo<input type="date" value={form.noticeDate} onChange={e => setForm({ ...form, noticeDate: e.target.value })} /></label>
          <label>Ngày nhận<input type="date" value={form.receivedDate} onChange={e => setForm({ ...form, receivedDate: e.target.value })} /></label>
          <label>Hạn phản hồi<input type="date" value={form.deadline} onChange={e => setForm({ ...form, deadline: e.target.value })} /></label></div>
        <label>Nội dung thông báo<textarea value={form.reason} onChange={e => setForm({ ...form, reason: e.target.value })} rows={2} /></label>
        <table className="grid"><thead><tr><th>Ký hiệu</th><th>Số HĐ</th><th>Ngày lập</th><th>Rủi ro / sai sót</th><th /></tr></thead>
          <tbody>{form.lines.map((l, i) => <tr key={i}>
            <td><input value={l.series} onChange={e => setForm({ ...form, lines: form.lines.map((x, j) => j === i ? { ...x, series: e.target.value } : x) })} size={8} /></td>
            <td><input value={l.invoiceNo} onChange={e => setForm({ ...form, lines: form.lines.map((x, j) => j === i ? { ...x, invoiceNo: e.target.value } : x) })} size={10} /></td>
            <td><input type="date" value={l.issueDate} onChange={e => setForm({ ...form, lines: form.lines.map((x, j) => j === i ? { ...x, issueDate: e.target.value } : x) })} /></td>
            <td><input value={l.issue} onChange={e => setForm({ ...form, lines: form.lines.map((x, j) => j === i ? { ...x, issue: e.target.value } : x) })} /></td>
            <td><button aria-label="Xóa dòng" disabled={form.lines.length === 1} onClick={() => setForm({ ...form, lines: form.lines.filter((_, j) => j !== i) })}>✕</button></td></tr>)}</tbody></table>
        <button onClick={() => setForm({ ...form, lines: [...form.lines, { series: '', invoiceNo: '', issueDate: '', issue: '' }] })}>+ Thêm hóa đơn</button>
      </Modal>}

      {resp && <Modal title={`Phản hồi ${resp.r.number}`} onClose={() => setResp(null)} busy={busy}
        footer={<><button onClick={() => setResp(null)}>Đóng</button><button className="primary" disabled={busy || (!resp.note.trim() && !resp.noticeId)} onClick={() => run(async () => {
          await post(`/einvoice/risk-notices/${resp.r.id}/respond`, { rowVersion: resp.r.rowVersion, note: resp.note || null, responseNoticeId: resp.noticeId || null })
          setResp(null); setInfo('Đã ghi nhận phản hồi.'); await load()
        })}>Ghi nhận phản hồi</button></>}>
        <label>Nội dung phản hồi / giải trình<textarea value={resp.note} onChange={e => setResp({ ...resp, note: e.target.value })} rows={3} /></label>
        <label>Gắn thông báo sai sót 04/SS đã lập (tùy chọn)<select value={resp.noticeId} onChange={e => setResp({ ...resp, noticeId: e.target.value })}><option value="">— không —</option>
          {notices.map(n => <option key={n.id} value={n.id}>{n.number} — {n.reason}</option>)}</select></label>
      </Modal>}
    </section>
  )
}
