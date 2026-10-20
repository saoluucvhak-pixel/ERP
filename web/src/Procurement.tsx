import { useCallback, useEffect, useState } from 'react'
import { ApiError, get, post, put } from './api'
import { AccountSelect, ErrorBox, ExportButton, navStore, PrintSheet, printSheet, useAction, useCatalog, useCompanyName } from './common'
import { addMoney, formatDate, formatMoney, parseMoney, today } from './money'

interface QueueRow { invoiceId: string; number: string; invoiceSeries: string; invoiceNo: string; invoiceDate: string; dueDate: string | null; partnerId: string; partnerCode: string; partnerName: string
  total: string; open: string; hasReceiptLink: boolean; fromOrder: boolean; acceptanceId: string | null; stage: string; mismatchCount: number; matchNotes: string[] | null }
interface InspLine { stockLineNo: number; itemCode: string; itemName: string; unit: string; receivedQty: string; acceptedQty: string; rejectedQty: string; note: string | null }
interface Inspection { id: string; number: string; inspectionDate: string; inspector: string; conclusion: string | null; lines: InspLine[] }
interface ReceiptRow { stockDocumentId: string; stockNumber: string; documentDate: string; status: string; partnerName: string; description: string; totalQty: string; inspection: Inspection | null }
interface ReorderRow { itemId: string; code: string; name: string; unit: string; onHand: string; minStock: string; suggestedQty: string; lastPrice: string | null; lastPartnerId: string | null }
interface SupplierRow { id: string; code: string; name: string; isActive: boolean; isSupplier: boolean; supplierStatus: 'Approved' | 'Pending' | 'Blocked'; supplierStatusReason: string | null }
interface RatingRow { partnerId: string; receipts: number; purchaseAmount: string; acceptRate: string | null; onTimeRate: string | null; score: number | null; grade: string | null }
const SUPPLIER_VI = { Approved: 'Đã duyệt', Pending: 'Chờ duyệt', Blocked: 'Bị chặn' } as const
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
  { label: 'Nhận hàng, kiểm nghiệm, nhập kho', journal: true, screen: 'stock' },
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
  const [receipts, setReceipts] = useState<ReceiptRow[]>([])
  const [suppliers, setSuppliers] = useState<SupplierRow[]>([])
  const [rating, setRating] = useState<Record<string, RatingRow>>({})
  const [policy, setPolicy] = useState(false)
  const [pick, setPick] = useState<Record<string, { qty: string; price: string }>>({})
  const [orderSupplier, setOrderSupplier] = useState('')
  const [inspecting, setInspecting] = useState<{ r: ReceiptRow; inspector: string; conclusion: string; accepted: Record<number, string> } | null>(null)
  const [printBb, setPrintBb] = useState<{ r: ReceiptRow; bb: Inspection } | null>(null)
  const company = useCompanyName()
  const [bank, setBank] = useState('')
  const [info, setInfo] = useState('')
  const { busy, error, run } = useAction()
  const asOf = today()

  const load = useCallback(() => run(async () => {
    setQueue(await get<QueueRow[]>(`/cycles/procurement/payable-queue?asOf=${asOf}`))
    setInbox(await get<InboxRow[]>('/cycles/payment/inbox'))
    setReorder(await get<ReorderRow[]>('/cycles/procurement/reorder-list'))
    setReceipts(await get<ReceiptRow[]>('/cycles/procurement/receipts'))
    setSuppliers((await get<SupplierRow[]>('/partners')).filter(p => p.isSupplier && p.isActive))
    setPolicy((await get<{ require: boolean }>('/cycles/procurement/suppliers/policy')).require)
    const rows = await get<RatingRow[]>(`/cycles/procurement/suppliers/rating?fromDate=${asOf.slice(0, 4)}-01-01&toDate=${asOf}`)
    setRating(Object.fromEntries(rows.map(r => [r.partnerId, r])))
  }), [run, asOf])
  useEffect(() => { load() }, [load])

  const act = (label: string, fn: () => Promise<unknown>) => run(async () => { await fn(); setInfo(label); await load() })
  const ask = (q: string) => { const v = prompt(q); return v && v.trim() ? v.trim() : null }
  const count = (s: string) => queue.filter(r => r.stage === s).length
  const stepCount: Record<number, number | null> = { 0: reorder.length, 2: receipts.filter(r => !r.inspection).length, 4: count('NOT_ACCEPTED'), 5: count('AWAITING_ORDER') + count('ORDER_CREATED'), 6: count('ORDER_APPROVED') + count('SENT_TO_BANK') + count('BANK_EXECUTED') }

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
        <strong>Nhận hàng và biên bản kiểm nghiệm</strong>
        <span className="muted"> · biên bản ghi số lượng thực nhận đạt của từng dòng phiếu nhập (không ghi Nhật ký); hóa đơn vượt số lượng đạt hoặc lệch đơn giá đơn đặt hàng thì bị chặn chấp nhận thanh toán</span>
        <table className="grid">
          <thead><tr><th>Phiếu nhập</th><th>Ngày</th><th>Nhà cung cấp</th><th className="num">Số lượng nhận</th><th>Biên bản kiểm nghiệm</th><th /></tr></thead>
          <tbody>{receipts.slice(0, 30).map(r => (
            <tr key={r.stockDocumentId}><td>{r.stockNumber}<div className="muted">{r.status === 'Draft' ? 'Nháp' : 'Đã ghi sổ'}</div></td><td>{formatDate(r.documentDate)}</td><td>{r.partnerName}</td><td className="num">{formatMoney(r.totalQty)}</td>
              <td>{r.inspection ? <>{r.inspection.number} · {r.inspection.inspector}{r.inspection.lines.some(l => l.rejectedQty !== '0') && <div style={{ color: 'var(--danger)' }}>Không đạt: {r.inspection.lines.filter(l => l.rejectedQty !== '0').map(l => `${l.itemCode} ${formatMoney(l.rejectedQty)}`).join(', ')}</div>}</> : <span className="muted">chưa kiểm nghiệm</span>}</td>
              <td style={{ display: 'flex', gap: 4 }}>
                {!r.inspection && <button className="primary" disabled={busy} onClick={async () => setInspecting({ r, inspector: '', conclusion: '', accepted: {} })}>Lập biên bản</button>}
                {r.inspection && <button onClick={() => setPrintBb({ r, bb: r.inspection! })}>In biên bản</button>}
                {r.inspection && <button className="danger" disabled={busy} onClick={() => { const reason = ask('Lý do hủy biên bản (để lập lại):'); if (reason) act('Đã hủy biên bản kiểm nghiệm.', () => post(`/cycles/procurement/inspections/${r.inspection!.id}/cancel`, { reason })) }}>Hủy</button>}
              </td></tr>))}
            {receipts.length === 0 && <tr><td colSpan={6} className="muted">Chưa có phiếu nhập kho.</td></tr>}</tbody>
        </table>
        {inspecting && <div className="panel" style={{ marginTop: 8 }}>
          <strong>Biên bản kiểm nghiệm cho {inspecting.r.stockNumber}</strong>
          <div className="form-row">
            <label style={{ flex: 1 }}>Người / ban kiểm nghiệm<input value={inspecting.inspector} onChange={e => setInspecting(v => v && { ...v, inspector: e.target.value })} /></label>
            <label style={{ flex: 1 }}>Kết luận<input value={inspecting.conclusion} onChange={e => setInspecting(v => v && { ...v, conclusion: e.target.value })} /></label>
          </div>
          <InspectionLines key={inspecting.r.stockDocumentId} receiptId={inspecting.r.stockDocumentId} accepted={inspecting.accepted} onChange={acc => setInspecting(v => v && { ...v, accepted: acc })} />
          <div className="toolbar">
            <button className="primary" disabled={busy || !inspecting.inspector.trim()} onClick={() => run(async () => {
              const lines = Object.entries(inspecting.accepted).filter(([, q]) => q.trim() !== '').map(([no, q]) => ({ stockLineNo: Number(no), acceptedQty: parseMoney(q) }))
              if (lines.some(l => l.acceptedQty == null)) throw new Error('Số lượng đạt không hợp lệ.')
              const res = await post<{ number: string }>('/cycles/procurement/inspections', { stockDocumentId: inspecting.r.stockDocumentId, inspectionDate: today(), inspector: inspecting.inspector.trim(), conclusion: inspecting.conclusion.trim() || null, lines })
              setInfo(`Đã lập biên bản kiểm nghiệm ${res.number}.`); setInspecting(null); await load()
            })}>Lưu biên bản</button><button onClick={() => setInspecting(null)}>Đóng</button></div>
        </div>}
      </div>
      {printBb && <PrintBb data={printBb} company={company} onDone={() => setPrintBb(null)} />}

      <div className="panel">
        <strong>Danh mục cần đặt hàng</strong>
        <span className="muted"> · vật tư có tồn hiện tại thấp hơn tồn tối thiểu (khai báo ở Thiết lập → Vật tư); số lượng đề xuất = số lượng đặt tối ưu</span>
        <table className="grid">
          <thead><tr><th>Vật tư</th><th>ĐVT</th><th className="num">Tồn hiện tại</th><th className="num">Tồn tối thiểu</th><th className="num">Đề xuất đặt</th><th className="num">SL đặt</th><th className="num">Đơn giá (gợi ý theo đơn gần nhất)</th></tr></thead>
          <tbody>{reorder.map(r => {
            const p = pick[r.itemId]
            return <tr key={r.itemId}><td><input type="checkbox" aria-label={`Chọn ${r.code}`} checked={!!p} onChange={e => setPick(v => { const n = { ...v }; if (e.target.checked) { n[r.itemId] = { qty: r.suggestedQty, price: r.lastPrice ?? '' }; if (!orderSupplier && r.lastPartnerId) setOrderSupplier(r.lastPartnerId) } else delete n[r.itemId]; return n })} /> {r.code} – {r.name}</td><td>{r.unit}</td><td className="num" style={{ color: 'var(--danger)' }}>{formatMoney(r.onHand)}</td><td className="num">{formatMoney(r.minStock)}</td><td className="num">{formatMoney(r.suggestedQty)}</td>
              <td className="num">{p ? <input aria-label={`Số lượng ${r.code}`} style={{ width: 90, textAlign: 'right' }} value={p.qty} onChange={e => setPick(v => ({ ...v, [r.itemId]: { ...p, qty: e.target.value } }))} /> : null}</td>
              <td className="num">{p ? <input aria-label={`Đơn giá ${r.code}`} style={{ width: 110, textAlign: 'right' }} value={p.price} onChange={e => setPick(v => ({ ...v, [r.itemId]: { ...p, price: e.target.value } }))} /> : null}</td></tr>
          })}
            {reorder.length === 0 && <tr><td colSpan={7} className="muted">Không có vật tư nào dưới tồn tối thiểu (hoặc chưa khai báo tồn tối thiểu).</td></tr>}</tbody>
        </table>
        {Object.keys(pick).length > 0 && <div className="toolbar">
          <select aria-label="Nhà cung cấp đặt hàng" value={orderSupplier} onChange={e => setOrderSupplier(e.target.value)}>
            <option value="">— Chọn nhà cung cấp —</option>
            {suppliers.map(p => <option key={p.id} value={p.id} disabled={p.supplierStatus !== 'Approved'}>{p.code} – {p.name}{p.supplierStatus !== 'Approved' ? ` (${SUPPLIER_VI[p.supplierStatus]})` : ''}</option>)}
          </select>
          <button className="primary" disabled={busy || !orderSupplier} onClick={() => run(async () => {
            const lines = Object.entries(pick).map(([itemId, v]) => ({ itemId, quantity: parseMoney(v.qty), unitPrice: parseMoney(v.price || '0') }))
            if (lines.some(l => l.quantity == null || l.unitPrice == null)) throw new Error('Số lượng hoặc đơn giá không hợp lệ.')
            const o = await post<{ number: string }>('/cycles/procurement/reorder-list/create-order', { partnerId: orderSupplier, lines })
            setPick({}); setInfo(`Đã tạo đơn mua nháp ${o.number}; người khác xác nhận ở màn Đơn hàng.`); await load()
          })}>Tạo đơn mua nháp từ các dòng đã chọn</button></div>}
      </div>

      <div className="panel">
        <strong>Nhà cung cấp: duyệt và xếp hạng uy tín</strong>
        <span className="muted"> · điểm = 60% tỷ lệ hàng đạt kiểm nghiệm + 40% giao đúng hạn (từ đầu năm); A ≥ 90, B ≥ 75, còn lại C</span>
        <label className="muted" style={{ display: 'block', margin: '6px 0' }}>
          <input type="checkbox" checked={policy} disabled={busy} onChange={e => act('Đã cập nhật chính sách duyệt nhà cung cấp.', () => put('/cycles/procurement/suppliers/policy', { require: e.target.checked }))} /> Nhà cung cấp mới phải được duyệt trước khi đặt hàng
        </label>
        <table className="grid">
          <thead><tr><th>Nhà cung cấp</th><th>Trạng thái</th><th className="num">Phiếu nhập</th><th className="num">Giá trị nhập</th><th className="num">Đạt kiểm nghiệm</th><th className="num">Đúng hạn</th><th className="num">Điểm</th><th>Hạng</th><th></th></tr></thead>
          <tbody>{suppliers.map(p => { const r = rating[p.id]; return <tr key={p.id}>
            <td>{p.code} – {p.name}</td>
            <td title={p.supplierStatusReason ?? ''} style={{ color: p.supplierStatus === 'Approved' ? undefined : 'var(--danger)' }}>{SUPPLIER_VI[p.supplierStatus]}</td>
            <td className="num">{r?.receipts ?? 0}</td><td className="num">{formatMoney(r?.purchaseAmount ?? '0')}</td>
            <td className="num">{r?.acceptRate != null ? `${formatMoney(String(r.acceptRate))}%` : '—'}</td><td className="num">{r?.onTimeRate != null ? `${formatMoney(String(r.onTimeRate))}%` : '—'}</td>
            <td className="num">{r?.score ?? '—'}</td><td>{r?.grade ?? '—'}</td>
            <td>{p.supplierStatus !== 'Approved' && <button disabled={busy} onClick={() => act(`Đã duyệt ${p.code}.`, () => put(`/cycles/procurement/suppliers/${p.id}/status`, { status: 'Approved' }))}>Duyệt</button>}
              {p.supplierStatus !== 'Blocked' && <button disabled={busy} onClick={() => { const reason = ask('Lý do chặn nhà cung cấp:'); if (reason) act(`Đã chặn ${p.code}.`, () => put(`/cycles/procurement/suppliers/${p.id}/status`, { status: 'Blocked', reason })) }}>Chặn</button>}
              {p.supplierStatus === 'Approved' && <button disabled={busy} onClick={() => { const reason = ask('Lý do đưa về chờ duyệt:'); if (reason) act(`Đã đưa ${p.code} về chờ duyệt.`, () => put(`/cycles/procurement/suppliers/${p.id}/status`, { status: 'Pending', reason })) }}>Chờ duyệt</button>}</td></tr> })}
            {suppliers.length === 0 && <tr><td colSpan={9} className="muted">Chưa có nhà cung cấp.</td></tr>}</tbody>
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
                <td>{r.hasReceiptLink ? '✓ phiếu nhập' : <span className="muted">không gắn phiếu nhập</span>} · {r.fromOrder ? '✓ đơn mua' : <span className="muted">không theo đơn</span>}
                  <div title={(r.matchNotes ?? []).join('\n')} style={{ color: r.mismatchCount > 0 ? 'var(--danger)' : 'var(--ok)' }}>{r.mismatchCount > 0 ? `⚠ ${r.mismatchCount} chỗ lệch: ${(r.matchNotes ?? []).filter(n => n.includes('vượt') || n.includes('khác')).join(' ')}` : '✓ khớp khi đối chiếu'}</div></td>
                <td>{STAGE_VI[r.stage] ?? r.stage}</td>
                <td style={{ display: 'flex', gap: 4 }}>
                  {r.stage === 'NOT_ACCEPTED' && <button className="primary" disabled={busy} onClick={() => {
                    const note = prompt('Ghi chú chấp nhận (ví dụ: đã đối chiếu đơn – phiếu nhập – hóa đơn), có thể để trống:') ?? null
                    if (note === null) return
                    const url = `/cycles/procurement/invoices/${r.invoiceId}/accept-payment`
                    act(`Đã chấp nhận thanh toán ${r.invoiceSeries}/${r.invoiceNo}; chu trình thanh toán đã nhận bàn giao.`, async () => {
                      try { await post(url, { note }) } catch (e) {
                        // Lệch khi đối chiếu: người có trách nhiệm có thể vẫn chấp nhận nhưng phải ghi lý do (được ghi nhật ký).
                        if (e instanceof ApiError && e.code === 'PAYMENT_MATCH_FAILED') {
                          const reason = prompt(`${e.message}\n\nNhập lý do để vẫn chấp nhận thanh toán (bỏ trống = không chấp nhận):`)
                          if (!reason || !reason.trim()) throw new Error('Đã không chấp nhận: hóa đơn còn lệch khi đối chiếu.')
                          await post(url, { note: reason.trim(), acknowledgeMismatch: true })
                        } else throw e
                      }
                    })
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

/** Số lượng thực nhận đạt từng dòng phiếu nhập (mặc định đạt toàn bộ). Lấy dòng từ phiếu kho. */
function InspectionLines({ receiptId, accepted, onChange }: { receiptId: string; accepted: Record<number, string>; onChange: (a: Record<number, string>) => void }) {
  const [lines, setLines] = useState<{ lineNo: number; itemId: string; quantity: string }[]>([])
  const [items, setItems] = useState<{ id: string; code: string; name: string; unit: string }[]>([])
  useEffect(() => {
    get<{ id: string; lines: { lineNo: number; itemId: string; quantity: string }[] }[]>('/stock-documents').then(d => setLines(d.find(x => x.id === receiptId)?.lines ?? [])).catch(() => setLines([]))
    get<{ id: string; code: string; name: string; unit: string }[]>('/items').then(setItems).catch(() => setItems([]))
  }, [receiptId])
  return (
    <table className="grid"><thead><tr><th>Vật tư</th><th className="num">Số lượng nhận</th><th className="num">Số lượng đạt</th><th className="num">Không đạt</th></tr></thead>
      <tbody>{lines.map(l => {
        const it = items.find(i => i.id === l.itemId)
        const v = accepted[l.lineNo] ?? formatMoney(l.quantity)
        const q = parseMoney(v)
        return <tr key={l.lineNo}><td>{it ? `${it.code} – ${it.name} (${it.unit})` : l.itemId}</td><td className="num">{formatMoney(l.quantity)}</td>
          <td><input className="num" aria-label={`Số lượng đạt dòng ${l.lineNo}`} value={v} onChange={e => onChange({ ...accepted, [l.lineNo]: e.target.value })} /></td>
          <td className="num" style={{ color: 'var(--danger)' }}>{q != null && q !== l.quantity ? formatMoney(addMoney([l.quantity, '-' + q])) : ''}</td></tr>
      })}</tbody></table>
  )
}

function PrintBb({ data, company, onDone }: { data: { r: ReceiptRow; bb: Inspection }; company: string; onDone: () => void }) {
  useEffect(() => { const t = setTimeout(() => { printSheet(); onDone() }, 150); return () => clearTimeout(t) }, [onDone])
  const { r, bb } = data
  return (
    <PrintSheet>
      <div><b>{company}</b></div><h1>BIÊN BẢN KIỂM NGHIỆM</h1>
      <div className="center">Số: <b>{bb.number}</b> · Ngày {bb.inspectionDate.split('-').reverse().join('/')} · Phiếu nhập kho {r.stockNumber}</div>
      <p>Nhà cung cấp: <b>{r.partnerName}</b> · Người / ban kiểm nghiệm: {bb.inspector}</p>
      <table><thead><tr><th>STT</th><th>Vật tư</th><th>ĐVT</th><th>Số lượng nhận</th><th>Đạt</th><th>Không đạt</th><th>Ghi chú</th></tr></thead>
        <tbody>{bb.lines.map(l => <tr key={l.stockLineNo}><td className="center">{l.stockLineNo}</td><td>{l.itemName} ({l.itemCode})</td><td className="center">{l.unit}</td>
          <td className="num">{formatMoney(l.receivedQty)}</td><td className="num">{formatMoney(l.acceptedQty)}</td><td className="num">{l.rejectedQty === '0' ? '' : formatMoney(l.rejectedQty)}</td><td>{l.note}</td></tr>)}</tbody></table>
      <p>Kết luận: {bb.conclusion ?? '……………………………………………………………'}</p>
      <div className="signs"><div><b>Người nhận hàng</b><br /><i>(Ký, họ tên)</i></div><div><b>Thủ kho</b><br /><i>(Ký, họ tên)</i></div><div><b>Ban kiểm nghiệm</b><br /><i>(Ký, họ tên)</i></div></div>
      <div className="foot">Mẫu in nội bộ HAK Accounting — chưa đối chiếu mẫu theo chế độ kế toán [CẦN XÁC MINH].</div>
    </PrintSheet>
  )
}
