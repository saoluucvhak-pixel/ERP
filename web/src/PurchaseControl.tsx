import { useCallback, useEffect, useState } from 'react'
import { get, post } from './api'
import { ErrorBox, useAction } from './common'
import { formatDate, formatMoney, today } from './money'

interface Policy { id: string; scope: string; partnerId: string | null; partnerName: string | null; version: number; pricePercent: string; priceAbsolute: string; quantityPercent: string; effectiveFrom: string; isActive: boolean; note: string | null }
interface Supplier { id: string; code: string; name: string; isSupplier?: boolean; isActive: boolean }
interface MatchFlag { code: string; blocking: boolean; message: string }
interface MatchLine { lineNo: number; itemCode: string | null; invoicedQty: string | null; receivedQty: string | null; acceptedQty: string | null; orderedQty: string | null; unitPrice: string; orderPrice: string | null; flags: MatchFlag[] }
interface Match { invoiceId: string; number: string; lines: MatchLine[]; blockingCount: number; matched: boolean; policyVersion: number | null }
interface Approval { id: string; status: string; reason: string; policyVersion: number | null; approvedAt: string; stillValid: boolean; closeReason: string | null }
interface InvoiceRow { invoiceId: string; number: string; invoiceSeries: string; invoiceNo: string; partnerName: string; open: string; mismatchCount: number }
interface RecvLine { lineNo: number; itemCode: string; unit: string; ordered: string; arrived: string; accepted: string; rejected: string; pendingQc: string; postedToStock: string; invoiced: string; notYetArrived: string }
interface Receiving { number: string; status: string; lines: RecvLine[] }
interface OrderRow { id: string; number: string; partnerId: string; status: string }

const SCOPE_VI: Record<string, string> = { Company: 'Cả công ty', Partner: 'Theo nhà cung cấp' }

/** Kiểm soát đối chiếu mua hàng: chính sách dung sai có phiên bản, duyệt sai lệch hóa đơn (quyền riêng) và theo dõi nhận hàng của đơn mua. */
export function PurchaseControlPanel() {
  const { busy, error, run } = useAction()
  const [policies, setPolicies] = useState<Policy[]>([])
  const [suppliers, setSuppliers] = useState<Supplier[]>([])
  const [invoices, setInvoices] = useState<InvoiceRow[]>([])
  const [orders, setOrders] = useState<OrderRow[]>([])
  const [form, setForm] = useState({ scope: 'Company', partnerId: '', pricePercent: '0', priceAbsolute: '0', quantityPercent: '0', effectiveFrom: today(), note: '' })
  const [match, setMatch] = useState<{ m: Match; a: Approval | null } | null>(null)
  const [recv, setRecv] = useState<Receiving | null>(null)
  const [info, setInfo] = useState('')

  const load = useCallback(() => run(async () => {
    setPolicies(await get<Policy[]>('/cycles/procurement/match-policies'))
    setSuppliers((await get<Supplier[]>('/partners')).filter(p => p.isSupplier && p.isActive))
    setInvoices((await get<InvoiceRow[]>(`/cycles/procurement/payable-queue?asOf=${today()}`)).filter(r => r.mismatchCount > 0))
    setOrders((await get<{ items: OrderRow[] }>('/orders?kind=Purchase&page=1&pageSize=100')).items.filter(o => o.status !== 'Cancelled'))
  }), [run])
  useEffect(() => { load() }, [load])

  const dec = (s: string) => /^\d+([.,]\d{1,4})?$/.test(s.trim()) ? s.trim().replace(',', '.') : null
  const openMatch = (invoiceId: string) => run(async () => {
    const m = await get<Match>(`/cycles/procurement/invoices/${invoiceId}/match`)
    const a = await get<Approval | null>(`/cycles/procurement/invoices/${invoiceId}/variance`).catch(() => null)
    setMatch({ m, a }); setInfo('')
  })
  const ask = (q: string) => { const v = prompt(q); return v && v.trim() ? v.trim() : null }
  const savePolicy = () => run(async () => {
    const price = dec(form.pricePercent), abs = dec(form.priceAbsolute), qty = dec(form.quantityPercent)
    if (price == null || abs == null || qty == null) throw new Error('Dung sai phải là số không âm (tối đa 4 chữ số thập phân).')
    if (form.scope === 'Partner' && !form.partnerId) throw new Error('Chọn nhà cung cấp cho chính sách theo nhà cung cấp.')
    await post('/cycles/procurement/match-policies', { scope: form.scope, partnerId: form.scope === 'Partner' ? form.partnerId : null, pricePercent: price, priceAbsolute: abs, quantityPercent: qty, effectiveFrom: form.effectiveFrom, note: form.note.trim() || null })
    setInfo('Đã lưu chính sách mới (phiên bản mới, bản cũ giữ nguyên để truy vết).'); await load()
  })

  return (
    <>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}

      <div className="panel">
        <strong>Dung sai đối chiếu hóa đơn mua</strong>
        <span className="muted"> · hóa đơn vượt dung sai (giá hoặc số lượng) bị chặn chấp nhận thanh toán, trừ khi người có quyền duyệt sai lệch; mỗi lần đổi tạo phiên bản mới</span>
        <table className="grid">
          <thead><tr><th>Phạm vi</th><th>Nhà cung cấp</th><th className="num">Phiên bản</th><th className="num">Giá lệch % </th><th className="num">Giá lệch tuyệt đối</th><th className="num">Số lượng lệch %</th><th>Hiệu lực từ</th><th>Ghi chú</th></tr></thead>
          <tbody>{policies.map(p => (
            <tr key={p.id} style={p.isActive ? undefined : { opacity: .55 }}><td>{SCOPE_VI[p.scope] ?? p.scope}</td><td>{p.partnerName ?? ''}</td><td className="num">{p.version}{p.isActive ? '' : ' (cũ)'}</td>
              <td className="num">{formatMoney(p.pricePercent)}</td><td className="num">{formatMoney(p.priceAbsolute)}</td><td className="num">{formatMoney(p.quantityPercent)}</td><td>{formatDate(p.effectiveFrom)}</td><td>{p.note}</td></tr>))}
            {policies.length === 0 && <tr><td colSpan={8} className="muted">Chưa khai báo: hệ thống dùng dung sai 0 (không cho lệch).</td></tr>}</tbody>
        </table>
        <div className="form-row">
          <label>Phạm vi<select value={form.scope} onChange={e => setForm(f => ({ ...f, scope: e.target.value }))}><option value="Company">Cả công ty</option><option value="Partner">Theo nhà cung cấp</option></select></label>
          {form.scope === 'Partner' && <label>Nhà cung cấp<select value={form.partnerId} onChange={e => setForm(f => ({ ...f, partnerId: e.target.value }))}><option value="">—</option>{suppliers.map(s => <option key={s.id} value={s.id}>{s.code} – {s.name}</option>)}</select></label>}
          <label>Giá lệch %<input style={{ width: 80 }} value={form.pricePercent} onChange={e => setForm(f => ({ ...f, pricePercent: e.target.value }))} /></label>
          <label>Giá lệch tuyệt đối<input style={{ width: 110 }} value={form.priceAbsolute} onChange={e => setForm(f => ({ ...f, priceAbsolute: e.target.value }))} /></label>
          <label>SL lệch %<input style={{ width: 80 }} value={form.quantityPercent} onChange={e => setForm(f => ({ ...f, quantityPercent: e.target.value }))} /></label>
          <label>Hiệu lực từ<input type="date" value={form.effectiveFrom} onChange={e => setForm(f => ({ ...f, effectiveFrom: e.target.value }))} /></label>
          <label style={{ flex: 1 }}>Ghi chú<input value={form.note} onChange={e => setForm(f => ({ ...f, note: e.target.value }))} /></label>
          <button className="primary" disabled={busy} onClick={savePolicy}>Lưu chính sách</button>
        </div>
      </div>

      <div className="panel">
        <strong>Hóa đơn mua đang lệch đối chiếu</strong>
        <span className="muted"> · xem chi tiết lệch theo dòng; người có quyền “duyệt sai lệch” ghi lý do để cho phép chấp nhận thanh toán. Nếu số liệu nguồn đổi sau khi duyệt, phê duyệt hết hiệu lực.</span>
        <table className="grid">
          <thead><tr><th>Hóa đơn</th><th>Nhà cung cấp</th><th className="num">Còn nợ</th><th className="num">Số lệch</th><th /></tr></thead>
          <tbody>{invoices.map(r => (
            <tr key={r.invoiceId}><td>{r.invoiceSeries}/{r.invoiceNo}<div className="muted">{r.number}</div></td><td>{r.partnerName}</td><td className="num">{formatMoney(r.open)}</td><td className="num">{r.mismatchCount}</td>
              <td><button disabled={busy} onClick={() => openMatch(r.invoiceId)}>Đối chiếu</button></td></tr>))}
            {invoices.length === 0 && <tr><td colSpan={5} className="muted">Không có hóa đơn nào đang lệch.</td></tr>}</tbody>
        </table>
        {match && <div className="panel" style={{ marginTop: 8 }}>
          <div className="toolbar"><strong>Đối chiếu {match.m.number}</strong>
            <span className="muted">{match.m.matched ? 'Khớp' : `${match.m.blockingCount} chỗ chặn`}{match.m.policyVersion != null ? ` · chính sách v${match.m.policyVersion}` : ''}</span>
            <button onClick={() => setMatch(null)}>Đóng</button></div>
          <table className="grid">
            <thead><tr><th>Dòng</th><th>Vật tư</th><th className="num">Hóa đơn</th><th className="num">Đã nhận</th><th className="num">Đạt</th><th className="num">Đặt</th><th className="num">Giá HĐ</th><th className="num">Giá đơn</th><th>Cảnh báo</th></tr></thead>
            <tbody>{match.m.lines.map(l => (
              <tr key={l.lineNo}><td>{l.lineNo}</td><td>{l.itemCode}</td><td className="num">{l.invoicedQty != null ? formatMoney(l.invoicedQty) : ''}</td><td className="num">{l.receivedQty != null ? formatMoney(l.receivedQty) : ''}</td>
                <td className="num">{l.acceptedQty != null ? formatMoney(l.acceptedQty) : ''}</td><td className="num">{l.orderedQty != null ? formatMoney(l.orderedQty) : ''}</td><td className="num">{formatMoney(l.unitPrice)}</td><td className="num">{l.orderPrice != null ? formatMoney(l.orderPrice) : ''}</td>
                <td>{l.flags.map(f => <div key={f.code} style={{ color: f.blocking ? 'var(--danger)' : undefined }}>{f.message}</div>)}</td></tr>))}</tbody>
          </table>
          {match.a
            ? <div className={match.a.stillValid ? 'msg ok' : 'msg'}>Phê duyệt sai lệch: {match.a.status}{match.a.stillValid ? ' (còn hiệu lực)' : ' (đã hết hiệu lực)'} · {match.a.reason} · {new Date(match.a.approvedAt).toLocaleString('vi-VN')}
                {match.a.status === 'Active' && <> <button className="danger" disabled={busy} onClick={() => { const r = ask('Lý do thu hồi phê duyệt:'); if (r) run(async () => { await post(`/cycles/procurement/variance/${match.a!.id}/revoke`, { reason: r }); setInfo('Đã thu hồi phê duyệt sai lệch.'); await openMatch(match.m.invoiceId) }) }}>Thu hồi</button></>}</div>
            : match.m.blockingCount > 0 && <button className="primary" disabled={busy} onClick={() => { const r = ask('Lý do duyệt sai lệch (bắt buộc):'); if (r) run(async () => { await post(`/cycles/procurement/invoices/${match.m.invoiceId}/approve-variance`, { reason: r }); setInfo('Đã duyệt sai lệch.'); await openMatch(match.m.invoiceId) }) }}>Duyệt sai lệch</button>}
        </div>}
      </div>

      <div className="panel">
        <strong>Theo dõi nhận hàng của đơn mua</strong>
        <span className="muted"> · lượng đặt, thực đến, đạt, không đạt, chờ kiểm tra chất lượng, đã nhập kho và đã lập hóa đơn của từng dòng</span>
        <div className="form-row">
          <select aria-label="Đơn mua" defaultValue="" onChange={e => { const id = e.target.value; if (id) run(async () => setRecv(await get<Receiving>(`/cycles/procurement/orders/${id}/receiving`))); else setRecv(null) }}>
            <option value="">— Chọn đơn mua —</option>{orders.map(o => <option key={o.id} value={o.id}>{o.number}</option>)}
          </select>
        </div>
        {recv && <table className="grid">
          <thead><tr><th>Dòng</th><th>Vật tư</th><th className="num">Đặt</th><th className="num">Thực đến</th><th className="num">Đạt</th><th className="num">Không đạt</th><th className="num">Chờ QC</th><th className="num">Đã nhập kho</th><th className="num">Đã lập HĐ</th><th className="num">Chưa về</th></tr></thead>
          <tbody>{recv.lines.map(l => (
            <tr key={l.lineNo}><td>{l.lineNo}</td><td>{l.itemCode} ({l.unit})</td><td className="num">{formatMoney(l.ordered)}</td><td className="num">{formatMoney(l.arrived)}</td><td className="num">{formatMoney(l.accepted)}</td>
              <td className="num" style={l.rejected !== '0' ? { color: 'var(--danger)' } : undefined}>{formatMoney(l.rejected)}</td><td className="num">{formatMoney(l.pendingQc)}</td><td className="num">{formatMoney(l.postedToStock)}</td><td className="num">{formatMoney(l.invoiced)}</td><td className="num">{formatMoney(l.notYetArrived)}</td></tr>))}</tbody>
        </table>}
      </div>
    </>
  )
}
