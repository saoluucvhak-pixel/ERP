import { useEffect, useState } from 'react'
import { get } from './api'
import { ErrorBox, Modal, useAction } from './common'
import { formatMoney } from './money'

interface Dependents { invoices: { id: string; number: string; series: string; invoiceNo: string; status: string; amount: string }[] }
const STATUS_VI: Record<string, string> = { Draft: 'Nháp', Posted: 'Đã ghi sổ', Reversed: 'Đã đảo', Cancelled: 'Đã hủy' }

/**
 * Hộp thoại hủy một lần quyết toán CKTM: hiện mã thỏa thuận, lần quyết toán, số tiền và các hóa đơn / chứng từ phụ thuộc trước khi xác nhận (thay cho prompt trình duyệt).
 * Chỉ báo thành công sau khi API xác nhận; lỗi giữ nguyên hộp thoại và lý do đã nhập để sửa tiếp.
 */
export function VoidRunDialog({ agreementId, agreementCode, runId, label, amount, onConfirm, onClose }: {
  agreementId: string; agreementCode: string; runId: string | null; label: string; amount: string | null
  onConfirm: (reason: string) => Promise<void>; onClose: () => void
}) {
  const [reason, setReason] = useState('')
  const [deps, setDeps] = useState<Dependents | null>(null)
  const { busy, error, run } = useAction()
  useEffect(() => {
    run(async () => setDeps(runId
      ? await get<Dependents>(`/discounts/agreements/${agreementId}/lifecycle?runId=${runId}`)
      : await get<Dependents>(`/discounts/agreements/${agreementId}/lifecycle`)))
  }, [agreementId, runId, run])
  const posted = deps?.invoices.filter(i => i.status === 'Posted') ?? []
  return <Modal title={`Hủy quyết toán — ${agreementCode}`} onClose={onClose} busy={busy}
    footer={<><button disabled={busy} onClick={onClose}>Không hủy</button>
      <button className="primary" disabled={busy || reason.trim().length < 3} onClick={() => run(async () => { await onConfirm(reason.trim()); onClose() })}>Hủy quyết toán</button></>}>
    <ErrorBox error={error} />
    <p>Thỏa thuận <b>{agreementCode}</b> · {label}{amount != null && <> · số tiền <b>{formatMoney(amount)}</b></>}.</p>
    {deps && <>
      <p>Hóa đơn / chứng từ phụ thuộc: {deps.invoices.length === 0 ? 'không có' : `${deps.invoices.length}`}. Chỉ hủy được các bản nháp; bản đã ghi sổ phải đảo trước (hệ thống từ chối nếu còn).</p>
      {deps.invoices.length > 0 && <table className="grid"><thead><tr><th>Hóa đơn</th><th>Trạng thái</th><th className="num">Tiền</th></tr></thead><tbody>
        {deps.invoices.map(i => <tr key={i.id}><td>{i.number} · {i.series}/{i.invoiceNo}</td><td>{STATUS_VI[i.status] ?? i.status}</td><td className="num">{formatMoney(i.amount)}</td></tr>)}</tbody></table>}
      {posted.length > 0 && <p className="msg error" role="alert">Có {posted.length} hóa đơn đã ghi sổ: đảo chúng trước khi hủy.</p>}
    </>}
    <label>Lý do hủy (bắt buộc)<input value={reason} onChange={e => setReason(e.target.value)} autoFocus /></label>
  </Modal>
}
