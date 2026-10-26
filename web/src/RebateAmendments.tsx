import { useEffect, useState } from 'react'
import { get, post } from './api'
import { ErrorBox, Modal, useAction } from './common'
import { VoidRunDialog } from './VoidRunDialog'
import { addMoney, formatDate, formatMoney, today } from './money'

interface Agreement { id: string; code: string; toDate: string }
interface History { id: string; parentRunId: string | null; sequence: number; amount: string; reason: string | null; voided: boolean; createdAt: string }
interface Preview { rowVersion: number; previousRunId: string; sourceHash: string; eligibleSales: string; previousAmount: string; targetAmount: string; difference: string
  lines: { originalInvoiceId: string; series: string; invoiceNo: string; lineNo: number; currency: string | null; previous: string; target: string; difference: string; functionalDifference: string; vatRate: string | null }[] }

export function RebateAmendments({ agreement, onClose, onChanged, onProgress, onStatement }: {
  agreement: Agreement; onClose: () => void; onChanged: () => Promise<unknown>; onProgress: (id: string) => void; onStatement: (id: string) => void
}) {
  const [history, setHistory] = useState<History[]>([])
  const [preview, setPreview] = useState<Preview | null>(null)
  const [reason, setReason] = useState('')
  const [series, setSeries] = useState('')
  const [no, setNo] = useState('')
  const [date, setDate] = useState(agreement.toDate > today() ? agreement.toDate : today())
  const [postingDate, setPostingDate] = useState(date)
  const [info, setInfo] = useState('')
  const [voiding, setVoiding] = useState<History | null>(null)
  const { busy, error, run } = useAction()
  const base = `/discounts/agreements/${agreement.id}`
  useEffect(() => { run(async () => setHistory(await get<History[]>(`${base}/history`))) }, [base, run])
  return <Modal title={`Bổ sung / thu hồi CKTM — ${agreement.code}`} onClose={onClose} busy={busy} footer={<button disabled={busy} onClick={onClose}>Đóng</button>}>
    {voiding && <VoidRunDialog agreementId={agreement.id} agreementCode={agreement.code} runId={voiding.id} label={voiding.parentRunId ? `lần bổ sung/thu hồi ${voiding.sequence}` : 'quyết toán gốc'} amount={voiding.amount}
      onClose={() => setVoiding(null)} onConfirm={async why => {
        await post(`${base}/runs/${voiding.id}/void`, { reason: why }); setPreview(null)
        setHistory(await get<History[]>(`${base}/history`)); await onChanged()
      }} />}
    <ErrorBox error={error} />
    {info && <p role="status">{info}</p>}
    <p>Quyền lợi lũy kế đã lập (gồm nháp, không tính lần đã hủy): <b>{formatMoney(addMoney(history.filter(h => !h.voided).map(h => h.amount)))}</b>. Xem tiến độ từng lần để phân biệt số đã ghi sổ.</p>
    <p>Giữ nguyên các lần đã chốt. Lần mới chỉ xử lý chênh lệch theo điều khoản đã duyệt; người có quyền lập hóa đơn tạo nháp, người có quyền ghi sổ kiểm tra và ghi sổ theo quy trình phân nhiệm. Hoàn tất hóa đơn của lần trước rồi mới tính tiếp.</p>
    <table className="grid"><thead><tr><th>Lần</th><th>Ngày lập</th><th className="num">Bổ sung (+) / thu hồi (−)</th><th>Lý do</th><th>Thao tác</th></tr></thead><tbody>
      {history.map(h => <tr key={h.id}><td>{h.parentRunId ? h.sequence : 'Gốc'}{h.voided ? ' · đã hủy' : ''}</td><td>{formatDate(h.createdAt.slice(0, 10))}</td><td className="num">{formatMoney(h.amount)}</td><td>{h.reason ?? 'Quyết toán ban đầu'}</td><td>
        <button disabled={busy} onClick={() => onProgress(h.id)}>Tiến độ lần này</button>
        <button disabled={busy} onClick={() => onStatement(h.id)}>Bảng kê lần này</button>
        {!h.voided && <button disabled={busy} onClick={() => setVoiding(h)}>Hủy lần này</button>}
      </td></tr>)}
    </tbody></table>
    <button disabled={busy} onClick={() => run(async () => { setPreview(null); setPreview(await get<Preview>(`${base}/amendment-preview`)); setInfo('') })}>Tính lại chênh lệch</button>
    {preview && <>
      <p>Doanh số hiện tại: <b>{formatMoney(preview.eligibleSales)}</b>. Quyền lợi trước: {formatMoney(preview.previousAmount)}; quyền lợi hiện tại: {formatMoney(preview.targetAmount)}; chênh lệch tiền hạch toán: <b>{formatMoney(preview.difference)}</b>.</p>
      <table className="grid"><thead><tr><th>Hóa đơn / dòng</th><th>Tiền</th><th className="num">Đã chốt</th><th className="num">Quyền lợi mới</th><th className="num">Chênh lệch</th><th className="num">Tiền hạch toán</th><th>VAT %</th></tr></thead><tbody>
        {preview.lines.map(l => <tr key={`${l.originalInvoiceId}-${l.lineNo}`}><td>{l.series}/{l.invoiceNo} · {l.lineNo}</td><td>{l.currency ?? 'Tiền hạch toán'}</td><td className="num">{formatMoney(l.previous)}</td><td className="num">{formatMoney(l.target)}</td><td className="num">{formatMoney(l.difference)}</td><td className="num">{formatMoney(l.functionalDifference)}</td><td>{l.vatRate ?? 'Không chịu thuế'}</td></tr>)}
      </tbody></table>
      {preview.lines.length === 0 ? <p>Không có chênh lệch theo từng dòng; không tạo chứng từ.</p> : <>
        <p>Số dương tạo hóa đơn giảm CKTM bổ sung; số âm tạo hóa đơn tăng để thu hồi CKTM. Tổng bằng 0 vẫn phải xử lý nếu phân bổ theo nguồn/thuế suất thay đổi. Hóa đơn tạo ở trạng thái nháp, chưa tự ghi sổ hoặc xác nhận hoàn tất thuế.</p>
        <div className="form-row">
          <label>Lý do<input value={reason} onChange={e => setReason(e.target.value)} /></label>
          <label>Ký hiệu<input value={series} onChange={e => setSeries(e.target.value)} /></label>
          <label>Số hóa đơn đầu tiên<input value={no} onChange={e => setNo(e.target.value)} /></label>
          <label>Ngày hóa đơn<input type="date" value={date} onChange={e => setDate(e.target.value)} /></label>
          <label>Ngày hạch toán<input type="date" value={postingDate} onChange={e => setPostingDate(e.target.value)} /></label>
        </div>
        <p className="muted">Hóa đơn NCC phải dùng số/ký hiệu thực nhận; nhiều số không liên tiếp cần chia hồ sơ trước khi xác nhận.</p>
        <button className="primary" disabled={busy || !reason.trim() || !series.trim() || !no.trim()} onClick={() => run(async () => {
          const result = await post<{ id: string; amount: string }>(`${base}/amend`, { rowVersion: preview.rowVersion, previousRunId: preview.previousRunId, sourceHash: preview.sourceHash, reason, invoiceSeries: series, firstInvoiceNo: no, invoiceDate: date, postingDate })
          setPreview(null); setInfo(`Đã lập lần điều chỉnh ${formatMoney(result.amount)}. Mở Hóa đơn để kiểm tra, phát hành và ghi sổ; xem Tiến độ lần này để theo dõi hồ sơ.`)
          setHistory(await get<History[]>(`${base}/history`)); await onChanged()
        })}>Lập hóa đơn nháp theo chênh lệch</button>
      </>}
    </>}
  </Modal>
}
