import { useCallback, useEffect, useState } from 'react'
import { get, post } from './api'
import { AccountSelect, ErrorBox, ExportButton, navStore, useAction, useCatalog } from './common'
import { formatMoney, today } from './money'

interface QueueRow { invoiceId: string; number: string; invoiceSeries: string; invoiceNo: string; invoiceDate: string; dueDate: string | null; partnerId: string; partnerCode: string; partnerName: string
  total: string; open: string; hasReceiptLink: boolean; fromOrder: boolean; acceptanceId: string | null; stage: string }
interface ReorderRow { itemId: string; code: string; name: string; unit: string; onHand: string; minStock: string; suggestedQty: string }
interface InboxRow { acceptanceId: string; invoiceNumber: string; invoiceSeries: string; invoiceNo: string; partnerName: string; amount: string; open: string; dueDate: string | null; stage: string
  paymentOrderNumber: string | null; acceptedAt: string; note: string | null; closeReason: string | null }

const STAGE_VI: Record<string, string> = {
  NOT_ACCEPTED: 'Chưa chấp nhận', AWAITING_ORDER: 'Chờ tạo lệnh', ORDER_CREATED: 'Lệnh chờ duyệt', ORDER_APPROVED: 'Lệnh đã duyệt',
  SENT_TO_BANK: 'Đã gửi ngân hàng', BANK_EXECUTED: 'NH đã chi, chờ ghi sổ', PAID: 'Đã thanh toán', REJECTED: 'Bị từ chối', CANCELLED: 'Đã rút',
}

/** Khâu của chu trình theo tệp phân tích ERP (mục V.2): "ghi Nhật ký" = khâu đó phát sinh bút toán. */
const STEPS: { label: string; journal: boolean; screen?: string; note?: string }[] = [
  { label: 'Yêu cầu mua hàng', journal: false },
  { label: 'Đơn mua', journal: false, screen: 'orders' },
  { label: 'Nhận hàng, nhập kho', journal: true, screen: 'stock' },
  { label: 'Hóa đơn mua', journal: true, screen: 'trade' },
  { label: 'Chấp nhận thanh toán', journal: false },
  { label: 'Lệnh thanh toán', journal: false, screen: 'bank' },
  { label: 'Ngân hàng chi', journal: true, screen: 'bank' },
]

/** Chu trình mua hàng → thanh toán: hàng chờ theo ngày đáo hạn, chấp nhận thanh toán (không ghi Nhật ký), hộp thư của chu trình thanh toán. */
export function ProcurementView() {
  const { accounts } = useCatalog()
  const [queue, setQueue] = useState<QueueRow[]>([])
  const [inbox, setInbox] = useState<InboxRow[]>([])
  const [reorder, setReorder] = useState<ReorderRow[]>([])
  const [bank, setBank] = useState('')
  const [info, setInfo] = useState('')
  const { busy, error, run } = useAction()
  const asOf = today()

  const load = useCallback(() => run(async () => {
    setQueue(await get<QueueRow[]>(`/cycles/procurement/payable-queue?asOf=${asOf}`))
    setInbox(await get<InboxRow[]>('/cycles/payment/inbox'))
    setReorder(await get<ReorderRow[]>('/cycles/procurement/reorder-list'))
  }), [run, asOf])
  useEffect(() => { load() }, [load])

  const act = (label: string, fn: () => Promise<unknown>) => run(async () => { await fn(); setInfo(label); await load() })
  const ask = (q: string) => { const v = prompt(q); return v && v.trim() ? v.trim() : null }
  const count = (s: string) => queue.filter(r => r.stage === s).length
  const stepCount: Record<number, number | null> = { 0: reorder.length, 4: count('NOT_ACCEPTED'), 5: count('AWAITING_ORDER') + count('ORDER_CREATED'), 6: count('ORDER_APPROVED') + count('SENT_TO_BANK') + count('BANK_EXECUTED') }

  return (
    <>
      <div className="toolbar"><h2>Chu trình mua hàng → thanh toán</h2><button onClick={() => load()} disabled={busy}>Tải lại</button>
        <ExportButton fileName={`Cho_thanh_toan_${asOf}`} sheet="Chờ thanh toán" title="Hóa đơn mua chờ thanh toán theo ngày đáo hạn" subtitle={`Đến ngày ${asOf.split('-').reverse().join('/')}`} disabled={queue.length === 0}
          columns={[{ header: 'Đáo hạn' }, { header: 'Hóa đơn', width: 20 }, { header: 'Nhà cung cấp', width: 34 }, { header: 'Còn nợ', numeric: true }, { header: 'Giai đoạn', width: 24 }]}
          rows={queue.map(r => [r.dueDate ?? '', `${r.invoiceSeries}/${r.invoiceNo}`, `${r.partnerCode} – ${r.partnerName}`, r.open, STAGE_VI[r.stage] ?? r.stage])} />
      </div>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}

      <div className="panel" style={{ display: 'flex', gap: 6, flexWrap: 'wrap', alignItems: 'stretch' }}>
        {STEPS.map((s, i) => (
          <div key={s.label} style={{ flex: '1 1 130px', border: '1px solid var(--line)', borderRadius: 6, padding: '6px 8px', cursor: s.screen ? 'pointer' : undefined, opacity: s.note ? 0.6 : 1 }}
            onClick={() => s.screen && navStore.go?.(s.screen)}>
            <div style={{ fontWeight: 600 }}>{i + 1}. {s.label}</div>
            <div className="muted">{s.note ?? (s.journal ? 'ghi Nhật ký' : 'không ghi Nhật ký')}</div>
            {stepCount[i] != null && <div>{stepCount[i]} chứng từ</div>}
          </div>))}
      </div>

      <div className="panel">
        <strong>Danh mục cần đặt hàng</strong>
        <span className="muted"> · vật tư có tồn hiện tại thấp hơn tồn tối thiểu (khai báo ở Thiết lập → Vật tư); số lượng đề xuất = số lượng đặt tối ưu</span>
        <table className="grid">
          <thead><tr><th>Vật tư</th><th>ĐVT</th><th className="num">Tồn hiện tại</th><th className="num">Tồn tối thiểu</th><th className="num">Đề xuất đặt</th></tr></thead>
          <tbody>{reorder.map(r => <tr key={r.itemId}><td>{r.code} – {r.name}</td><td>{r.unit}</td><td className="num" style={{ color: 'var(--danger)' }}>{formatMoney(r.onHand)}</td><td className="num">{formatMoney(r.minStock)}</td><td className="num">{formatMoney(r.suggestedQty)}</td></tr>)}
            {reorder.length === 0 && <tr><td colSpan={5} className="muted">Không có vật tư nào dưới tồn tối thiểu (hoặc chưa khai báo tồn tối thiểu).</td></tr>}</tbody>
        </table>
      </div>

      <div className="panel">
        <strong>Hàng chờ thanh toán theo ngày đáo hạn</strong>
        <span className="muted"> · hóa đơn mua VND đã ghi sổ còn nợ; ngoại tệ thanh toán ở màn Ngoại tệ</span>
        <table className="grid">
          <thead><tr><th>Đáo hạn</th><th>Hóa đơn</th><th>Nhà cung cấp</th><th className="num">Còn nợ</th><th>Đối chiếu</th><th>Giai đoạn</th><th /></tr></thead>
          <tbody>{queue.map(r => {
            const overdue = r.dueDate != null && r.dueDate < asOf
            return (
              <tr key={r.invoiceId}>
                <td style={{ color: overdue ? 'var(--danger)' : undefined }}>{r.dueDate ?? '—'}{overdue ? ' (quá hạn)' : ''}</td>
                <td>{r.invoiceSeries}/{r.invoiceNo}<div className="muted">{r.number}</div></td><td>{r.partnerCode} – {r.partnerName}</td>
                <td className="num">{formatMoney(r.open)}{r.open !== r.total && <div className="muted">/ {formatMoney(r.total)}</div>}</td>
                <td>{r.hasReceiptLink ? '✓ phiếu nhập' : <span className="muted">không gắn phiếu nhập</span>} · {r.fromOrder ? '✓ đơn mua' : <span className="muted">không theo đơn</span>}</td>
                <td>{STAGE_VI[r.stage] ?? r.stage}</td>
                <td style={{ display: 'flex', gap: 4 }}>
                  {r.stage === 'NOT_ACCEPTED' && <button className="primary" disabled={busy} onClick={() => {
                    const note = prompt('Ghi chú chấp nhận (ví dụ: đã đối chiếu đơn – phiếu nhập – hóa đơn), có thể để trống:') ?? null
                    if (note !== null) act(`Đã chấp nhận thanh toán ${r.invoiceSeries}/${r.invoiceNo}; chu trình thanh toán đã nhận bàn giao.`, () => post(`/cycles/procurement/invoices/${r.invoiceId}/accept-payment`, { note }))
                  }}>Chấp nhận thanh toán</button>}
                  {r.acceptanceId && (r.stage === 'AWAITING_ORDER' || r.stage === 'ORDER_CREATED') && <button disabled={busy} onClick={() => {
                    const reason = ask('Lý do rút chấp nhận:')
                    if (reason) act('Đã rút chấp nhận.', () => post(`/cycles/procurement/acceptances/${r.acceptanceId}/cancel`, { reason }))
                  }}>Rút chấp nhận</button>}
                </td>
              </tr>)
          })}
            {queue.length === 0 && <tr><td colSpan={7} className="muted">Không có hóa đơn mua nào còn nợ.</td></tr>}</tbody>
        </table>
      </div>

      <div className="panel">
        <div className="toolbar">
          <strong>Hộp thư chu trình thanh toán</strong>
          <label>Tài khoản ngân hàng chi<AccountSelect accounts={accounts} value={bank} onChange={setBank} filter={a => a.cashKind === 'Bank'} ariaLabel="Tài khoản ngân hàng chi" /></label>
          <span className="muted">Công ty đã ánh xạ vai trò BANK_PAYMENT_DEFAULT thì lệnh được tạo tự động ngay khi chấp nhận. Duyệt, gửi ngân hàng, ghi nhận NH đã chi: màn Ngân hàng.</span>
        </div>
        <table className="grid">
          <thead><tr><th>Đáo hạn</th><th>Hóa đơn</th><th>Nhà cung cấp</th><th className="num">Số tiền</th><th>Giai đoạn</th><th>Lệnh</th><th /></tr></thead>
          <tbody>{inbox.map(r => (
            <tr key={r.acceptanceId}>
              <td>{r.dueDate ?? '—'}</td><td>{r.invoiceSeries}/{r.invoiceNo}{r.note && <div className="muted">{r.note}</div>}</td><td>{r.partnerName}</td>
              <td className="num">{formatMoney(r.amount)}</td><td>{STAGE_VI[r.stage] ?? r.stage}</td><td>{r.paymentOrderNumber ?? ''}</td>
              <td style={{ display: 'flex', gap: 4 }}>
                {r.stage === 'AWAITING_ORDER' && !r.paymentOrderNumber && <>
                  <button className="primary" disabled={busy || !bank} title={bank ? '' : 'Chọn tài khoản ngân hàng chi'} onClick={() => act('Đã tạo lệnh thanh toán Nháp.', () => post(`/cycles/payment/acceptances/${r.acceptanceId}/create-order`, { bankAccountId: bank }))}>Tạo lệnh</button>
                  <button className="danger" disabled={busy} onClick={() => {
                    const reason = ask('Lý do từ chối (trả về Cung ứng):')
                    if (reason) act('Đã từ chối, trả về Cung ứng.', () => post(`/cycles/payment/acceptances/${r.acceptanceId}/reject`, { reason }))
                  }}>Từ chối</button></>}
              </td>
            </tr>))}
            {inbox.length === 0 && <tr><td colSpan={7} className="muted">Hộp thư trống.</td></tr>}</tbody>
        </table>
      </div>
    </>
  )
}
