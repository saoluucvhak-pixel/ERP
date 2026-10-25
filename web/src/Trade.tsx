import { useCallback, useEffect, useState } from 'react'
import { get, post } from './api'
import { AccountSelect, ErrorBox, Modal, PartnerSelect, useAction, useCatalog, FlowStrip, navStore } from './common'
import { useUnsavedWhenChanged } from './unsaved'
import { useOpenTarget, focusRowProps } from './opentarget'
import { addMoney, formatDate, formatMoney, optionalRate, parseMoney, parseRate, today } from './money'

interface Uninvoiced { stockDocumentId: string; stockDocumentNumber: string; postingDate: string; lineNo: number; itemCode: string; quantity: string; invoicedQuantity: string; bdmt: string | null }
interface StockDoc { id: string; number: string; type: string; status: string; postingDate: string; lines: { lineNo: number; itemId: string; quantity: string }[] }
interface Invoice { id: string; rowVersion: number; number: string; kind: string; purpose: string; invoiceSeries: string; invoiceNo: string; invoiceDate: string; postingDate: string; partnerId: string; currency: string | null; status: string; total: string; vat: string; tradeDiscount?: string; discountKind?: string | null; lines?: { lineNo: number; unitPrice: string }[] }
interface SvcCand { orderId: string; orderNumber: string; orderLineNo: number; itemCode: string; itemName: string; unit: string; remaining: string; unitPrice: string; vatRate: string | null; description: string | null }
interface Candidate { key: string; docId: string; lineNo: number; label: string; bdmt: string | null; remaining: string; svc?: SvcCand }
interface LineDraft { candidate: string; basis: 'Quantity' | 'Bdmt'; price: string; vat: string; qty: string; accountId: string; description: string; discPct: string; discAmt: string }

const STATUS_VI: Record<string, string> = { Draft: 'Nháp', Posted: 'Đã ghi sổ', Reversed: 'Đã đảo', Cancelled: 'Đã hủy' }
const PURPOSE_VI: Record<string, string> = { Return: 'trả hàng', Discount: 'giảm giá', Increase: 'điều chỉnh tăng' }
const empty = (): LineDraft => ({ candidate: '', basis: 'Bdmt', price: '', vat: '', qty: '', accountId: '', description: '', discPct: '', discAmt: '' })

/** Hóa đơn mua / bán. Dăm gỗ: đơn giá theo BDMT (lấy từ kết quả đo / chứng thư của dòng phiếu kho); giá vốn vẫn tính theo MT. */
export function TradeView() {
  const { accounts, partners } = useCatalog()
  const [kind] = useState<'Purchase' | 'Sales'>(() => navStore.kind ?? 'Sales')   // chiều do phân hệ đang mở quyết định
  const [candidates, setCandidates] = useState<Candidate[]>([])
  const [list, setList] = useState<Invoice[]>([])
  const [total, setTotal] = useState(0)
  const [adjusted, setAdjusted] = useState<Record<string, { amount: string; vat: string }>>({})   // hóa đơn điều chỉnh đã ghi sổ điều chỉnh số liệu của hóa đơn gốc
  const [head, setHead] = useState({ partnerId: '', series: '', no: '', invoiceDate: today(), postingDate: today(), dueDate: '', currency: '', rate: '', description: '', discPct: '', discDays: '' })
  const [lines, setLines] = useState<LineDraft[]>([empty()])
  useUnsavedWhenChanged([head, lines])
  const [info, setInfo] = useState('')
  const [adjusting, setAdjusting] = useState<Invoice | null>(null)
  const [einv, setEinv] = useState(false)        // đã cấu hình nhà cung cấp hóa đơn điện tử: hiện nút Phát hành / Thay thế
  const [awaitIssue, setAwaitIssue] = useState(false)
  const [replacing, setReplacing] = useState<{ inv: Invoice; reason: string; prices: Record<number, string> } | null>(null)
  const openId = useOpenTarget('invoice')
  const [pinned, setPinned] = useState<Invoice | null>(null)
  useEffect(() => { if (openId) get<Invoice>(`/invoices/${openId}`).then(setPinned).catch(() => { /* không còn / không có quyền */ }) }, [openId])
  const { busy, error, setError, run } = useAction()

  const load = useCallback(() => run(async () => {
    const pg = await get<{ items: Invoice[]; total: number }>(`/invoices?kind=${kind}&page=1&pageSize=100`)
    setList(pg.items); setTotal(pg.total)
    const posted = pg.items.filter(x => x.status === 'Posted' && x.purpose === 'Normal').map(x => x.id)
    setAdjusted(posted.length ? await get<Record<string, { amount: string; vat: string }>>(`/invoices/adjusted?ids=${posted.join(',')}`) : {})
    const un = await get<Uninvoiced[]>(`/reports/uninvoiced?kind=${kind}&asOf=${today()}`)
    const c: Candidate[] = un.map(u => ({
      key: `${u.stockDocumentId}:${u.lineNo}`, docId: u.stockDocumentId, lineNo: u.lineNo, bdmt: u.bdmt,
      remaining: u.quantity, label: `${u.stockDocumentNumber}/${u.lineNo} · ${u.postingDate} · ${u.itemCode} ${formatMoney(u.quantity)}${u.bdmt ? ` · ${formatMoney(u.bdmt)} BDMT` : ' · chưa có BDMT'}`,
    }))
    if (kind === 'Purchase') {
      const drafts = (await get<StockDoc[]>('/stock-documents')).filter(d => d.type === 'Receipt' && d.status === 'Draft')
      for (const d of drafts) for (const l of d.lines)
        c.push({ key: `${d.id}:${l.lineNo}`, docId: d.id, lineNo: l.lineNo, bdmt: null, remaining: l.quantity, label: `${d.number}/${l.lineNo} · ${d.postingDate} · phiếu nhập NHÁP ${formatMoney(l.quantity)} (ghi sổ cùng hóa đơn)` })
    }
    // Dịch vụ đã nghiệm thu (đơn → biên bản nghiệm thu xác nhận) còn lập được hóa đơn.
    const sv = await get<SvcCand[]>(`/service-acceptances/invoice-candidates?kind=${kind}`)
    for (const s of sv) c.push({ key: `svc:${s.orderId}:${s.orderLineNo}`, docId: '', lineNo: 0, bdmt: null, remaining: s.remaining, svc: s,
      label: `Dịch vụ · ${s.orderNumber}/${s.orderLineNo} · ${s.itemCode} ${s.itemName} · nghiệm thu còn ${formatMoney(s.remaining)} ${s.unit}` })
    setCandidates(c)
  }), [run, kind])
  const loadMore = async () => {
    const pg = await get<{ items: Invoice[]; total: number }>(`/invoices?kind=${kind}&page=${Math.floor(list.length / 100) + 1}&pageSize=100`)
    setList(l => [...l, ...pg.items]); setTotal(pg.total)
  }
  useEffect(() => { load() }, [load])

  useEffect(() => { if (kind === 'Sales') get<{ configured: boolean }>('/einvoice/status').then(s => setEinv(s.configured)).catch(() => setEinv(false)) }, [kind])
  const upd = (i: number, p: Partial<LineDraft>) => setLines(ls => ls.map((l, j) => (j === i ? { ...l, ...p } : l)))

  const save = () => run(async () => {
    const rate = head.currency ? parseRate(head.rate) : null
    if (head.currency && rate == null) throw new Error('Tỷ giá không hợp lệ (tối đa 6 số lẻ).')
    const discPct = head.discPct.trim() ? parseRate(head.discPct) : null
    if (head.discPct.trim() && discPct == null) throw new Error('Tỷ lệ chiết khấu thanh toán không hợp lệ.')
    const body = {
      kind, partnerId: head.partnerId, awaitIssue: kind === 'Sales' && einv && awaitIssue, invoiceSeries: awaitIssue ? '' : head.series, invoiceNo: awaitIssue ? '' : head.no, invoiceDate: head.invoiceDate, postingDate: head.postingDate,
      dueDate: head.dueDate || null, currency: head.currency || null, exchangeRate: rate, description: head.description,
      paymentDiscountPercent: discPct, paymentDiscountDays: head.discDays.trim() ? Number(head.discDays) : null,
      lines: lines.map((l, i) => {
        const price = parseMoney(l.price)
        if (!price) throw new Error(`Dòng ${i + 1}: đơn giá không hợp lệ.`)
        const c = candidates.find(x => x.key === l.candidate)
        // Chiết khấu thương mại ghi trên hóa đơn: % hoặc số tiền (không cả hai). Để trống thì máy chủ lấy tỷ lệ của dòng đơn hàng gắn với dòng này.
        if (l.discPct.trim() && l.discAmt.trim()) throw new Error(`Dòng ${i + 1}: chiết khấu nhập theo % hoặc theo số tiền, không nhập cả hai.`)
        const dPct = l.discPct.trim() ? parseRate(l.discPct) : null, dAmt = l.discAmt.trim() ? parseMoney(l.discAmt) : null
        if ((l.discPct.trim() && dPct == null) || (l.discAmt.trim() && dAmt == null)) throw new Error(`Dòng ${i + 1}: chiết khấu không hợp lệ.`)
        const disc = { discountPercent: dPct, discountAmount: dAmt }
        if (c?.svc) return { unitPrice: price, basis: 'Quantity', quantity: parseMoney(l.qty), accountId: null, vatRate: optionalRate(l.vat), description: l.description || null, orderId: c.svc.orderId, orderLineNo: c.svc.orderLineNo, ...disc }
        return c
          ? { unitPrice: price, stockDocumentId: c.docId, stockLineNo: c.lineNo, basis: l.basis, vatRate: optionalRate(l.vat), description: l.description || null, ...disc }
          : { unitPrice: price, basis: 'Quantity', quantity: parseMoney(l.qty), accountId: l.accountId || null, vatRate: optionalRate(l.vat), description: l.description || null, ...disc }
      }),
    }
    try {
      const inv = await post<Invoice>('/invoices', body)
      setInfo(`Đã lập ${inv.number} (Nháp): tiền hàng ${formatMoney(inv.total)}${inv.tradeDiscount && inv.tradeDiscount !== '0' ? ` (sau chiết khấu thương mại ${formatMoney(inv.tradeDiscount)})` : ''}, thuế ${formatMoney(inv.vat)}. Người khác có quyền ghi sổ sẽ ghi sổ.`)
      setLines([empty()])
      await load()
    } catch (e) { setError(e) }
  })
  const act = (fn: () => Promise<unknown>, msg: string, needs?: string) => run(async () => { await fn(); setInfo(msg); await load() }, { exclusive: true, needs })

  return (
    <>
      <div className="toolbar">
        <h2>{kind === 'Sales' ? 'Hóa đơn bán' : 'Hóa đơn mua'}</h2>
      </div>
      <FlowStrip steps={[
        { label: 'Hóa đơn nháp (từ phiếu kho / phiếu giao hàng)', journal: false, count: `${list.filter(i => i.status === 'Draft').length} hóa đơn nháp` },
        { label: 'Ghi sổ hóa đơn (Nhật ký: doanh thu / hàng tồn, thuế, công nợ)', journal: true, count: `${list.filter(i => i.status === 'Posted').length} đã ghi sổ` },
        { label: 'Theo dõi công nợ, thanh toán', journal: true, screen: 'arap', count: 'bù trừ với phiếu thu / chi' },
        { label: 'Chiết khấu thương mại theo doanh số (cuối kỳ)', journal: true, screen: 'discounts', count: 'hóa đơn điều chỉnh giảm theo thỏa thuận' },
        { label: 'Chiết khấu thanh toán (trả / thu trong hạn)', journal: true, screen: 'paydiscount', count: 'theo điều khoản trên hóa đơn' },
        { label: 'Điều chỉnh: trả hàng, giảm giá (duyệt)', journal: true, screen: 'salesapproval', count: 'hóa đơn điều chỉnh liên kết hóa đơn gốc' },
      ]} />
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      <div className="panel">
        <div className="form-row">
          <label>{kind === 'Sales' ? 'Khách hàng' : 'Nhà cung cấp'}<PartnerSelect partners={partners} value={head.partnerId} onChange={v => setHead(h => ({ ...h, partnerId: v }))} /></label>
          {kind === 'Sales' && einv && <label title="Ký hiệu và số hóa đơn do nhà cung cấp hóa đơn điện tử cấp khi bấm Phát hành; hóa đơn tự ghi sổ sau khi cơ quan thuế chấp nhận."><input type="checkbox" checked={awaitIssue} onChange={e => setAwaitIssue(e.target.checked)} /> Lấy số từ nhà cung cấp HĐĐT</label>}
          <label>Ký hiệu HĐ<input value={head.series} disabled={awaitIssue} onChange={e => setHead(h => ({ ...h, series: e.target.value }))} /></label>
          <label>Số HĐ<input value={head.no} disabled={awaitIssue} onChange={e => setHead(h => ({ ...h, no: e.target.value }))} /></label>
          <label>Ngày hóa đơn<input type="date" value={head.invoiceDate} onChange={e => setHead(h => ({ ...h, invoiceDate: e.target.value }))} /></label>
          <label>Ngày hạch toán<input type="date" value={head.postingDate} onChange={e => setHead(h => ({ ...h, postingDate: e.target.value }))} /></label>
          <label>Hạn thanh toán<input type="date" value={head.dueDate} onChange={e => setHead(h => ({ ...h, dueDate: e.target.value }))} /></label>
          <label title="Chiết khấu thanh toán: giảm P% tiền hàng nếu trả đủ trong N ngày kể từ ngày hóa đơn. Để trống thì lấy theo điều khoản của đối tượng.">CK thanh toán %<input className="num" size={5} value={head.discPct} onChange={e => setHead(h => ({ ...h, discPct: e.target.value }))} placeholder="vd 2" /></label>
          <label>trong (ngày)<input className="num" size={4} inputMode="numeric" value={head.discDays} onChange={e => setHead(h => ({ ...h, discDays: e.target.value.replace(/\D/g, '') }))} placeholder="vd 10" /></label>
          <label>Nguyên tệ<select value={head.currency} onChange={e => setHead(h => ({ ...h, currency: e.target.value }))}><option value="">VND</option><option>USD</option><option>EUR</option><option>CNY</option><option>JPY</option></select></label>
          {head.currency && <label>Tỷ giá<input className="num" value={head.rate} onChange={e => setHead(h => ({ ...h, rate: e.target.value }))} /></label>}
          <label style={{ flex: 1, minWidth: 200 }}>Diễn giải<input value={head.description} onChange={e => setHead(h => ({ ...h, description: e.target.value }))} /></label>
        </div>
        <table className="grid">
          <thead><tr><th>#</th><th>Dòng phiếu kho ({kind === 'Sales' ? 'xuất chưa lập HĐ' : 'nhập chưa có HĐ'}) / dịch vụ</th><th>Tính giá theo</th><th className="num">SL (dịch vụ)</th><th>TK (dịch vụ)</th><th className="num">Đơn giá</th><th className="num" title="Chiết khấu thương mại ghi trên hóa đơn: nhập % HOẶC số tiền. Doanh thu / thuế / công nợ tính trên tiền hàng sau chiết khấu.">CK %</th><th className="num">CK (số tiền)</th><th className="num">Thuế suất %</th><th>Diễn giải</th><th /></tr></thead>
          <tbody>{lines.map((l, i) => (
            <tr key={i}><td>{i + 1}</td>
              <td><select aria-label="Dòng phiếu kho" value={l.candidate} onChange={e => { const c = candidates.find(x => x.key === e.target.value); upd(i, c?.svc ? { candidate: e.target.value, qty: c.svc.remaining, price: c.svc.unitPrice, vat: c.svc.vatRate ?? '', description: c.svc.description ?? '' } : { candidate: e.target.value }) }} style={{ maxWidth: 420 }}>
                <option value="">— dịch vụ / không gắn phiếu kho —</option>{candidates.map(c => <option key={c.key} value={c.key}>{c.label}</option>)}</select></td>
              <td><select aria-label="Tính giá theo" value={l.candidate && !candidates.find(x => x.key === l.candidate)?.svc ? l.basis : 'Quantity'} disabled={!l.candidate || candidates.find(x => x.key === l.candidate)?.svc !== undefined} onChange={e => upd(i, { basis: e.target.value as LineDraft['basis'] })}>
                <option value="Bdmt">BDMT</option><option value="Quantity">Số lượng (MT/ĐVT)</option></select></td>
              <td><input className="num" aria-label="Số lượng" size={6} disabled={!!l.candidate && !candidates.find(x => x.key === l.candidate)?.svc} value={l.qty} onChange={e => upd(i, { qty: e.target.value })} /></td>
              <td>{(!l.candidate) && <AccountSelect accounts={accounts} value={l.accountId} onChange={v => upd(i, { accountId: v })} ariaLabel="TK" />}</td>
              <td><input className="num" aria-label="Đơn giá" size={10} value={l.price} onChange={e => upd(i, { price: e.target.value })} /></td>
              <td><input className="num" aria-label="Chiết khấu %" size={4} value={l.discPct} placeholder="theo đơn" disabled={!!l.discAmt.trim()} onChange={e => upd(i, { discPct: e.target.value })} /></td>
              <td><input className="num" aria-label="Chiết khấu số tiền" size={8} value={l.discAmt} disabled={!!l.discPct.trim()} onChange={e => upd(i, { discAmt: e.target.value })} /></td>
              <td><input className="num" aria-label="Thuế suất" size={4} value={l.vat} placeholder="KCT" onChange={e => upd(i, { vat: e.target.value })} /></td>
              <td><input aria-label="Diễn giải dòng" value={l.description} onChange={e => upd(i, { description: e.target.value })} /></td>
              <td><button aria-label="Xóa dòng" onClick={() => setLines(ls => ls.filter((_, j) => j !== i))}>✕</button></td></tr>))}
          </tbody>
        </table>
        <div className="toolbar" style={{ marginTop: 8 }}>
          <button onClick={() => setLines(ls => [...ls, empty()])}>+ Thêm dòng</button>
          <button className="primary" onClick={save} disabled={busy}>Lưu hóa đơn nháp</button>
          <span className="muted">Thành tiền = BDMT (hoặc số lượng) × đơn giá; BDMT lấy từ kết quả đo/chứng thư hiệu lực của dòng phiếu kho.</span>
        </div>
      </div>
      {replacing && <Modal title={`Hóa đơn thay thế cho ${replacing.inv.invoiceSeries}/${replacing.inv.invoiceNo}`} onClose={() => setReplacing(null)} busy={busy}
        footer={<><button onClick={() => setReplacing(null)}>Đóng</button><button className="primary" disabled={busy || !replacing.reason.trim()} onClick={() => run(async () => {
          const changes = Object.entries(replacing.prices).filter(([, v]) => v.trim()).map(([lineNo, v]) => { const p = parseMoney(v); if (p == null) throw new Error(`Dòng ${lineNo}: đơn giá không hợp lệ.`); return { lineNo: Number(lineNo), unitPrice: p } })
          const r = await post<{ number: string }>(`/einvoice/invoices/${replacing.inv.id}/replacement`, { reason: replacing.reason, changes })
          setReplacing(null); setInfo(`Đã lập hóa đơn thay thế ${r.number} (nháp). Bấm “Phát hành HĐĐT”: khi cơ quan thuế chấp nhận, hóa đơn cũ được đảo và hóa đơn mới ghi sổ cùng lúc.`); await load()
        })}>Lập hóa đơn thay thế</button></>}>
        <div className="form-row"><label style={{ flex: 1, minWidth: 260 }}>Lý do thay thế<input value={replacing.reason} onChange={e => setReplacing({ ...replacing, reason: e.target.value })} placeholder="Sai đơn giá…" /></label></div>
        <p className="muted">Để trống = giữ nguyên dòng của hóa đơn gốc. Nhập đơn giá mới ở dòng cần sửa. Hóa đơn gốc vẫn có hiệu lực cho tới khi hóa đơn thay thế được cơ quan thuế chấp nhận.</p>
        <table className="grid"><thead><tr><th>Dòng</th><th className="num">Đơn giá cũ</th><th className="num">Đơn giá mới</th></tr></thead>
          <tbody>{(replacing.inv.lines ?? []).map(l => <tr key={l.lineNo}><td>{l.lineNo}</td><td className="num">{formatMoney(l.unitPrice)}</td>
            <td><input className="num" aria-label={`Đơn giá mới dòng ${l.lineNo}`} value={replacing.prices[l.lineNo] ?? ''} onChange={e => setReplacing({ ...replacing, prices: { ...replacing.prices, [l.lineNo]: e.target.value } })} /></td></tr>)}</tbody></table>
      </Modal>}
      {adjusting && <Adjust invoice={adjusting} onDone={msg => { setAdjusting(null); if (msg) setInfo(msg); load() }} />}
      <div className="scroll">
        <table className="grid">
          <thead><tr><th>Số</th><th>Ký hiệu / Số HĐ</th><th>Ngày HĐ</th><th>Ngày HT</th><th>Đối tượng</th><th className="num">Tiền hàng (VND)</th><th className="num">Thuế</th><th>Trạng thái</th><th /></tr></thead>
          <tbody>{(pinned && !list.some(x => x.id === pinned.id) ? [pinned, ...list] : list).map(i => (
            <tr key={i.id} {...focusRowProps(i.id, openId)}><td>{i.number}</td><td>{i.invoiceSeries}/{i.invoiceNo}{i.currency ? ` (${i.currency})` : ''}{i.purpose !== 'Normal' ? ` · ${i.purpose === 'Discount' && i.discountKind === 'Trade' ? 'chiết khấu thương mại' : PURPOSE_VI[i.purpose]}` : ''}</td><td>{formatDate(i.invoiceDate)}</td><td>{formatDate(i.postingDate)}</td>
              <td>{partners.find(p => p.id === i.partnerId)?.code}</td><td className="num">{formatMoney(i.total)}{adjusted[i.id] && <div className="muted" title="Sau các hóa đơn điều chỉnh đã ghi sổ; hóa đơn gốc không đổi">sau điều chỉnh {formatMoney(addMoney([i.total, adjusted[i.id].amount]))}</div>}</td><td className="num">{formatMoney(i.vat)}{adjusted[i.id] && <div className="muted">{formatMoney(addMoney([i.vat, adjusted[i.id].vat]))}</div>}</td>
              <td><span className="badge">{STATUS_VI[i.status] ?? i.status}</span></td>
              <td style={{ display: 'flex', gap: 4 }}>
                {i.status === 'Draft' && kind === 'Sales' && einv && <button className="primary" disabled={busy} onClick={() => run(async () => {
                  const d = await post<{ status: string; series: string | null; number: string | null; postError: string | null; lastError: string | null }>(`/einvoice/invoices/${i.id}/issue?rowVersion=${i.rowVersion}`)
                  setInfo(d.status === 'Issued' ? `Đã phát hành ${d.series}/${d.number}${d.postError ? ` nhưng chưa ghi sổ: ${d.postError}` : ' và ghi sổ.'}` : d.status === 'Rejected' ? `Bị từ chối: ${d.lastError}` : 'Đang chờ nhà cung cấp — theo dõi ở màn Hóa đơn điện tử.')
                  await load()
                })}>Phát hành HĐĐT</button>}
                {i.status === 'Draft' && !i.invoiceSeries.startsWith('CHO-PHAT-HANH') && <button className="primary" disabled={busy} onClick={() => act(async () => {
                  // Purchase invoices against receipts already posted at another value: show the split and require confirmation.
                  const diffs = await get<{ lineNo: number; receiptNumber: string; difference: string; inventoryPart: string; cogsPart: string; quantityInStock: string; receiptQuantity: string }[]>(`/invoices/${i.id}/price-differences`)
                  if (diffs.length > 0) {
                    const text = diffs.map(d => `Dòng ${d.lineNo} – phiếu ${d.receiptNumber}: chênh ${formatMoney(d.difference)} → tồn kho ${formatMoney(d.inventoryPart)} (còn ${formatMoney(d.quantityInStock)}/${formatMoney(d.receiptQuantity)}), giá vốn ${formatMoney(d.cogsPart)}`).join('\n')
                    if (!confirm(`CẢNH BÁO: giá hóa đơn khác giá trị đã nhập kho.\n\n${text}\n\nGhi sổ và điều chỉnh như trên?`)) throw new Error('Đã hủy ghi sổ: chưa xác nhận chênh lệch giá.')
                  }
                  await post(`/invoices/${i.id}/post?rowVersion=${i.rowVersion}&acknowledgePriceDifferences=${diffs.length > 0}`)
                }, `Đã ghi sổ ${i.number}.`, 'voucher.post')}>Ghi sổ</button>}
                {i.status === 'Draft' && <button className="danger" onClick={() => { const reason = prompt('Lý do hủy:'); if (reason) act(() => post(`/invoices/${i.id}/cancel?rowVersion=${i.rowVersion}`, { reason }), `Đã hủy ${i.number}.`) }}>Hủy</button>}
                {i.status === 'Posted' && i.purpose === 'Normal' && <button onClick={() => setAdjusting(i)}>Điều chỉnh</button>}
                {i.status === 'Posted' && i.purpose === 'Normal' && kind === 'Sales' && einv && <button onClick={() => setReplacing({ inv: i, reason: '', prices: {} })}>Thay thế</button>}
                {i.status === 'Posted' && <button className="danger" onClick={() => {
                  const reason = prompt('Lý do đảo hóa đơn:'); if (!reason) return
                  const d = prompt('Ngày đảo (yyyy-mm-dd):', i.postingDate); if (d) act(() => post(`/invoices/${i.id}/reverse?rowVersion=${i.rowVersion}`, { reversalDate: d, reason }), `Đã đảo ${i.number}.`, 'voucher.reverse')
                }}>Đảo</button>}
              </td></tr>))}
            {list.length === 0 && <tr><td colSpan={9} className="muted">Chưa có hóa đơn.</td></tr>}
            {list.length < total && <tr><td colSpan={9} className="muted">Đang hiển thị {list.length}/{total}. <button onClick={() => run(loadMore)}>Tải thêm</button></td></tr>}
          </tbody>
        </table>
      </div>
    </>
  )
}

/**
 * Điều chỉnh hóa đơn đã ghi sổ. Bán: trả hàng (phiếu nhập trả lại → đo độ khô → HĐ trả hàng), giảm giá, điều chỉnh tăng.
 * Mua: trả hàng (phiếu xuất trả nhà cung cấp theo giá vốn → HĐ trả hàng của NCC), giảm giá / tăng giá hàng mua.
 */
function Adjust({ invoice, onDone }: { invoice: Invoice; onDone: (msg?: string) => void }) {
  const purchase = invoice.kind === 'Purchase'
  const [purpose, setPurpose] = useState<'Return' | 'Discount' | 'Increase'>('Return')
  const [dkind, setDkind] = useState<'Trade' | 'Allowance'>('Trade')   // chiết khấu thương mại (sau bán, đạt doanh số…) hay giảm giá hàng (sai quy cách, kém chất lượng)
  const [lineNo, setLineNo] = useState('1')
  const [qty, setQty] = useState('')
  const [amount, setAmount] = useState('')
  const [date, setDate] = useState(today())
  const [series, setSeries] = useState(invoice.invoiceSeries)
  const [no, setNo] = useState('')
  const [receipts, setReceipts] = useState<StockDoc[]>([])
  const [receiptId, setReceiptId] = useState('')
  const [msg, setMsg] = useState('')
  const { busy, error, setError, run } = useAction()
  const loadReceipts = useCallback(() => get<StockDoc[]>('/stock-documents').then(ds => setReceipts(ds.filter(d => purchase
    ? d.type === 'Issue' && (d.status === 'Draft' || d.status === 'Posted')
    : d.type === 'Receipt' && d.status === 'Draft'))), [purchase])
  useEffect(() => { loadReceipts() }, [loadReceipts])

  const makeReceipt = () => run(async () => {
    const q = parseMoney(qty); if (!q) { setError(new Error('Số lượng trả lại không hợp lệ.')); return }
    const r = await post<{ id: string; number: string; value?: string }>(`/invoices/${invoice.id}/${purchase ? 'return-issue' : 'return-receipt'}`, { originalLineNo: Number.parseInt(lineNo, 10), quantity: q, date, warehouseId: null, description: null })
    setMsg(purchase
      ? `Đã lập phiếu xuất trả ${r.number} (nháp). Giá vốn tính theo phương pháp của kho khi ghi sổ; kho bình quân cuối tháng: ghi sổ phiếu và tính giá tháng trước khi ghi sổ hóa đơn trả hàng.`
      : `Đã lập ${r.number} (nháp), giá trị theo giá vốn gốc ${formatMoney(r.value)}. Nếu là dăm gỗ: ghi nhận độ khô ở Kho › MT/BDMT trước khi lập hóa đơn trả hàng.`)
    await loadReceipts(); setReceiptId(r.id)
  })
  const makeInvoice = () => run(async () => {
    const line = purpose === 'Return'
      ? { originalLineNo: Number.parseInt(lineNo, 10), returnReceiptId: receiptId, returnReceiptLineNo: 1, amount: null, description: null }
      : { originalLineNo: Number.parseInt(lineNo, 10), returnReceiptId: null, returnReceiptLineNo: null, amount: parseMoney(amount), description: null }
    const inv = await post<{ number: string; total: string; vat: string }>('/invoices/adjustments', {
      originalInvoiceId: invoice.id, purpose, discountKind: purpose === 'Discount' ? dkind : null, invoiceSeries: series, invoiceNo: no, invoiceDate: date, postingDate: date,
      description: `${purpose === 'Discount' && dkind === 'Trade' ? 'chiết khấu thương mại' : PURPOSE_VI[purpose]} theo HĐ ${invoice.invoiceSeries}/${invoice.invoiceNo}`, lines: [line],
    })
    onDone(`Đã lập ${inv.number} (nháp): ${formatMoney(inv.total)} + thuế ${formatMoney(inv.vat)}. Người khác có quyền ghi sổ sẽ ghi sổ.`)
  })

  return (
    <div className="panel">
      <div className="toolbar"><strong>Điều chỉnh HĐ {invoice.invoiceSeries}/{invoice.invoiceNo}</strong><button onClick={() => onDone()}>Đóng</button></div>
      <ErrorBox error={error} />
      {msg && !error && <div className="msg ok">{msg}</div>}
      <div className="form-row">
        <label>Loại<select value={purpose} onChange={e => setPurpose(e.target.value as typeof purpose)}><option value="Return">{purchase ? 'Hàng mua trả lại' : 'Hàng bán bị trả lại'}</option><option value="Discount">{purchase ? 'Giảm giá / chiết khấu hàng mua' : 'Giảm giá / chiết khấu hàng bán'}</option><option value="Increase">Điều chỉnh tăng</option></select></label>
        {purpose === 'Discount' && <label>Bản chất<select value={dkind} onChange={e => setDkind(e.target.value as typeof dkind)}><option value="Trade">Chiết khấu thương mại (đạt doanh số, mua nhiều)</option><option value="Allowance">Giảm giá hàng (sai quy cách, kém chất lượng)</option></select></label>}
        <label>Dòng HĐ gốc<input className="num" size={3} value={lineNo} onChange={e => setLineNo(e.target.value)} /></label>
        <label>Ngày<input type="date" value={date} onChange={e => setDate(e.target.value)} /></label>
        {purpose === 'Return' ? <>
          <label>SL trả lại (MT)<input className="num" value={qty} onChange={e => setQty(e.target.value)} /></label>
          <button style={{ alignSelf: 'flex-end' }} disabled={busy} onClick={makeReceipt}>{purchase ? '1. Lập phiếu xuất trả NCC' : '1. Lập phiếu nhập trả lại'}</button>
          <label>{purchase ? 'Phiếu xuất trả' : 'Phiếu nhập trả lại'}<select value={receiptId} onChange={e => setReceiptId(e.target.value)}><option value="">— chọn —</option>{receipts.map(r => <option key={r.id} value={r.id}>{r.number} · {formatDate(r.postingDate)}</option>)}</select></label>
        </> : <label>Số tiền ({invoice.currency ?? 'VND'})<input className="num" value={amount} onChange={e => setAmount(e.target.value)} /></label>}
        <label>Ký hiệu HĐ<input value={series} onChange={e => setSeries(e.target.value)} /></label>
        <label>Số HĐ điều chỉnh<input value={no} onChange={e => setNo(e.target.value)} /></label>
        <button className="primary" style={{ alignSelf: 'flex-end' }} disabled={busy || !no || (purpose === 'Return' && !receiptId)} onClick={makeInvoice}>{purpose === 'Return' ? '2. ' : ''}Lập HĐ điều chỉnh</button>
      </div>
    </div>
  )
}