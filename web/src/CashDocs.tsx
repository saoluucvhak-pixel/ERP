import { useEffect, useRef, useState } from 'react'
import { get, post, put, type OpenItem, type Voucher } from './api'
import { AccountSelect, ErrorBox, navStore, PartnerSelect, STATUS_VI, TYPE_VI, useAction, useCatalog, FlowStrip } from './common'
import { useUnsavedWhenChanged } from './unsaved'
import { addMoney, daysBefore, formatDate, formatMoney, parseMoney, today } from './money'

const EVENTS: Record<string, { value: string; label: string; needsPartner: boolean; needsCounter: boolean }[]> = {
  CR: [{ value: 'CUSTOMER_RECEIPT', label: 'Thu tiền khách hàng', needsPartner: true, needsCounter: false }],
  BR: [{ value: 'CUSTOMER_RECEIPT', label: 'Khách hàng chuyển khoản', needsPartner: true, needsCounter: false }],
  CP: [{ value: 'EXPENSE', label: 'Chi phí', needsPartner: false, needsCounter: true },
       { value: 'SUPPLIER_PAYMENT', label: 'Trả nhà cung cấp', needsPartner: true, needsCounter: false }],
  BP: [{ value: 'SUPPLIER_PAYMENT', label: 'Trả nhà cung cấp', needsPartner: true, needsCounter: false },
       { value: 'INTERNAL_TRANSFER', label: 'Chuyển tiền nội bộ', needsPartner: false, needsCounter: true }],
}

interface DocLine { amount: string; counterAccountId: string; settle: string; description: string; dueDate?: string }

/** Phiếu thu/chi, báo Nợ/Có: định khoản do Posting Rule Engine sinh ra theo nghiệp vụ được chọn. */
/** Sửa phiếu Nháp: dựng lại form từ chứng từ đã lưu (dòng đối ứng = dòng khác TK quỹ/ngân hàng chính); lưu bằng PUT /documents. */
function linesOf(v: Voucher): DocLine[] {
  const rest = v.lines.filter(l => l.accountId !== v.mainAccountId)
  return (rest.length ? rest : v.lines).map(l => ({ amount: formatMoney(l.amount), counterAccountId: l.accountId, settle: l.settleJournalLineId ?? '', description: l.description ?? '', dueDate: l.dueDate ?? undefined }))
}

export function CashDocsView({ voucher = null, onDone }: { voucher?: Voucher | null; onDone?: () => void }) {
  const { accounts, partners } = useCatalog()
  // Arrived from the revenue cycle's receivable queue: prefill a customer receipt that settles that invoice (consumed once).
  const [prefill] = useState(() => { const r = voucher ? undefined : navStore.receipt; navStore.receipt = undefined; return r })
  // Ý định từ "Thêm nhanh" (Thu / Chi): App xóa sau khi dựng xong, nên đọc trong lúc render là an toàn kể cả khi dựng hai lần.
  const [intent] = useState(() => (voucher ? undefined : navStore.intent))
  const [type, setType] = useState(voucher?.voucherType ?? intent?.voucherType ?? 'CR')
  const [event, setEvent] = useState(voucher?.businessEvent ?? intent?.businessEvent ?? 'CUSTOMER_RECEIPT')
  const [date, setDate] = useState(voucher?.postingDate ?? today())
  const [description, setDescription] = useState(voucher?.description ?? '')
  const [partnerId, setPartnerId] = useState(voucher?.partnerId ?? prefill?.partnerId ?? '')
  const [cpName, setCpName] = useState(voucher?.counterpartyName ?? '')
  const [cpUnit, setCpUnit] = useState(voucher?.counterpartyUnit ?? '')
  const [mainAccountId, setMainAccountId] = useState(voucher?.mainAccountId ?? '')
  const [lines, setLines] = useState<DocLine[]>(voucher ? linesOf(voucher) : [{ amount: prefill ? formatMoney(prefill.amount) : '', counterAccountId: '', settle: prefill?.settleJournalLineId ?? '', description: '' }])
  const [openItems, setOpenItems] = useState<OpenItem[]>([])
  useUnsavedWhenChanged([type, event, date, description, partnerId, cpName, cpUnit, mainAccountId, lines], !!voucher)   // phiếu đang sửa do màn gọi quản lý
  const [created, setCreated] = useState<Voucher | null>(null)
  const [balance, setBalance] = useState<string | null>(null)
  const [recent, setRecent] = useState<Voucher[]>([])
  const { busy, error, setError, run } = useAction()
  const ev = EVENTS[type].find(e => e.value === event) ?? EVENTS[type][0]
  const cashKind = type === 'CR' || type === 'CP' ? 'Cash' : 'Bank'
  const arapRole = event === 'CUSTOMER_RECEIPT' ? 'Receivable' : event === 'SUPPLIER_PAYMENT' ? 'Payable' : null

  // Đổi loại phiếu thì về nghiệp vụ mặc định của loại đó; lần dựng đầu giữ nghiệp vụ đã chọn (từ phiếu sửa hoặc Thêm nhanh).
  const prevType = useRef(type)
  useEffect(() => { if (!voucher) { if (prevType.current !== type) setEvent(EVENTS[type][0].value); setMainAccountId('') } prevType.current = type }, [type, voucher])
  useEffect(() => {
    const acc = accounts.find(a => a.openItemNature === arapRole)
    if (!acc || !partnerId) { setOpenItems([]); return }
    const q = new URLSearchParams({ accountId: acc.id, partnerId, asOf: date })
    get<OpenItem[]>(`/settlement/open-items?${q}`)
      // Foreign-currency invoices are settled at their book rate through "Ngoại tệ → Thu/chi theo hóa đơn".
      .then(items => setOpenItems(items.filter(i => i.side === (arapRole === 'Receivable' ? 'Debit' : 'Credit') && !i.foreignCurrency)))
      .catch(() => setOpenItems([]))
  }, [accounts, arapRole, partnerId, date])

  // Fund balance up to the document date (posted entries only) and the latest documents touching that fund.
  useEffect(() => {
    if (!mainAccountId) { setBalance(null); setRecent([]); return }
    const q = new URLSearchParams({ accountId: mainAccountId, from: `${date.slice(0, 4)}-01-01`, to: date })
    get<{ closingBalance: string }>(`/reports/account-ledger?${q}`).then(l => setBalance(l.closingBalance)).catch(() => setBalance(null))
    const sinceDate = daysBefore(date, 60)
    if (!sinceDate) { setRecent([]); return }   // ô ngày bị xóa trống: chờ nhập lại, không để cả màn hình trắng
    const r = new URLSearchParams({ accountId: mainAccountId, from: sinceDate, take: '200' })
    get<{ items: Voucher[] }>(`/vouchers?${r}`)
      .then(d => setRecent(d.items.filter(v => ['CR', 'CP', 'BR', 'BP'].includes(v.voucherType)).slice(-10).reverse())).catch(() => setRecent([]))
  }, [mainAccountId, date, created])
  const outflow = type === 'CP' || type === 'BP'
  const total = addMoney(lines.map(l => parseMoney(l.amount) ?? '0'))
  const overdraw = outflow && balance != null && total !== '0' && addMoney([balance, '-' + total]).startsWith('-')

  const save = () => run(async () => {
    const amounts = lines.map(l => parseMoney(l.amount))
    if (amounts.some(a => a == null)) { setError(new Error('Có dòng số tiền không hợp lệ.')); return }
    const body = {
      documentType: type, businessEvent: event, voucherDate: voucher?.voucherDate ?? date, postingDate: date, description,
      partnerId: partnerId || null, mainAccountId: mainAccountId || null, counterpartyName: cpName.trim() || null, counterpartyUnit: cpUnit.trim() || null,
      lines: lines.map((l, i) => ({ amount: amounts[i], counterAccountId: ev.needsCounter ? l.counterAccountId || null : null, settleJournalLineId: l.settle || null, description: l.description || null, dueDate: l.dueDate ?? null })),
    }
    if (voucher) { await put(`/documents/${voucher.id}?rowVersion=${voucher.rowVersion}`, body); onDone?.(); return }
    const v = await post<Voucher>('/documents', body)
    setCreated(v)
    setLines([{ amount: '', counterAccountId: '', settle: '', description: '' }])
  })

  const upd = (i: number, p: Partial<DocLine>) => setLines(ls => ls.map((l, j) => (j === i ? { ...l, ...p } : l)))

  return (
    <>
      <div className="toolbar"><h2>{voucher ? `Sửa ${TYPE_VI[voucher.voucherType]} ${voucher.number}` : 'Thu / Chi tiền'}</h2>{onDone && <button onClick={onDone}>Đóng</button>}<button className="primary" onClick={save} disabled={busy}>Lưu nháp</button></div>
      <FlowStrip steps={[
        { label: 'Lập phiếu thu / chi (nháp)', journal: false, count: 'theo quy tắc định khoản' },
        { label: 'Gửi duyệt → duyệt (người khác)', journal: false, screen: 'vouchers', count: 'màn Chứng từ' },
        { label: 'Ghi sổ (Nhật ký: tiền / đối ứng)', journal: true, screen: 'vouchers', count: 'bù trừ công nợ nếu có' },
        { label: 'Sổ quỹ, đối chiếu', journal: false, screen: 'ledger', count: 'kiểm kê quỹ định kỳ' },
      ]} />
      <ErrorBox error={error} />
      {created && <div className="msg ok">Đã tạo {TYPE_VI[created.voucherType]} {created.number} (Nháp, {formatMoney(created.totalDebit)}). Gửi duyệt và ghi sổ ở màn hình Chứng từ.</div>}
      {mainAccountId && balance != null && <div className={overdraw ? 'msg error' : 'msg ok'}>
        Số dư đã ghi sổ đến {formatDate(date)}: <b>{formatMoney(balance)}</b>
        {outflow && total !== '0' && <> · Phiếu này chi <b>{formatMoney(total)}</b> → còn {formatMoney(addMoney([balance, '-' + total]))}</>}
        {overdraw && ' — vượt số dư, hệ thống sẽ chặn khi ghi sổ nếu quỹ không cho phép âm.'}
      </div>}
      <div className="panel">
        <div className="form-row">
          <label>Loại chứng từ
            <select value={type} onChange={e => setType(e.target.value)} disabled={!!voucher}>
              {Object.keys(EVENTS).map(t => <option key={t} value={t}>{TYPE_VI[t]}</option>)}
            </select>
          </label>
          <label>Nghiệp vụ
            <select value={event} onChange={e => setEvent(e.target.value)}>
              {EVENTS[type].map(e => <option key={e.value} value={e.value}>{e.label}</option>)}
            </select>
          </label>
          <label>Ngày<input type="date" value={date} onChange={e => setDate(e.target.value)} /></label>
          <label>{cashKind === 'Cash' ? 'Quỹ' : 'Tài khoản ngân hàng'}
            <AccountSelect accounts={accounts} value={mainAccountId} onChange={setMainAccountId} filter={a => a.cashKind === cashKind} />
          </label>
          {ev.needsPartner && <label>Đối tượng<PartnerSelect partners={partners} value={partnerId} onChange={setPartnerId} /></label>}
          <label>Họ tên người {outflow ? 'nhận' : 'nộp'} tiền<input value={cpName} maxLength={200} onChange={e => setCpName(e.target.value)} placeholder={partners.find(p => p.id === partnerId)?.name ?? 'nếu khác đối tượng'} /></label>
          <label>Đơn vị / địa chỉ<input value={cpUnit} maxLength={300} onChange={e => setCpUnit(e.target.value)} placeholder={partners.find(p => p.id === partnerId)?.address ?? ''} /></label>
          <label style={{ flex: 1, minWidth: 220 }}>Diễn giải<input value={description} onChange={e => setDescription(e.target.value)} /></label>
        </div>
        <table className="grid">
          <thead><tr><th>#</th><th className="num">Số tiền</th>{ev.needsCounter && <th>TK đối ứng</th>}{ev.needsPartner && <th>Thanh toán cho chứng từ</th>}<th>Diễn giải</th><th /></tr></thead>
          <tbody>
            {lines.map((l, i) => (
              <tr key={i}>
                <td>{i + 1}</td>
                <td><input className="num" aria-label="Số tiền" value={l.amount} onChange={e => upd(i, { amount: e.target.value })}
                  onBlur={() => { const p = parseMoney(l.amount); if (p) upd(i, { amount: formatMoney(p) }) }} /></td>
                {ev.needsCounter && <td><AccountSelect accounts={accounts} value={l.counterAccountId} onChange={id => upd(i, { counterAccountId: id })}
                  filter={a => (event === 'INTERNAL_TRANSFER' ? a.cashKind !== 'None' : a.cashKind === 'None')} ariaLabel="TK đối ứng" /></td>}
                {ev.needsPartner && <td>
                  <select value={l.settle} aria-label="Chứng từ công nợ" onChange={e => {
                    const item = openItems.find(o => o.journalLineId === e.target.value)
                    upd(i, { settle: e.target.value, amount: item && !l.amount ? formatMoney(item.open) : l.amount })
                  }}>
                    <option value="">(ứng trước / chưa phân bổ)</option>
                    {openItems.map(o => <option key={o.journalLineId} value={o.journalLineId}>{o.voucherNumber} · {formatDate(o.postingDate)} · còn {formatMoney(o.open)}</option>)}
                  </select></td>}
                <td><input aria-label="Diễn giải dòng" value={l.description} onChange={e => upd(i, { description: e.target.value })} /></td>
                <td><button aria-label="Xóa dòng" onClick={() => setLines(ls => ls.filter((_, j) => j !== i))}>✕</button></td>
              </tr>
            ))}
          </tbody>
        </table>
        <button style={{ marginTop: 8 }} onClick={() => setLines(ls => [...ls, { amount: '', counterAccountId: '', settle: '', description: '' }])}>+ Thêm dòng</button>
        {recent.length > 0 && <><h3>Phiếu gần đây của {cashKind === 'Cash' ? 'quỹ' : 'tài khoản'} này</h3>
          <table className="grid"><thead><tr><th>Ngày</th><th>Số</th><th>Diễn giải</th><th className="num">Số tiền</th><th>Trạng thái</th></tr></thead><tbody>
            {recent.map(v => <tr key={v.id}><td>{formatDate(v.postingDate)}</td><td>{v.number}</td><td>{v.description}</td><td className="num">{formatMoney(v.totalDebit)}</td><td>{STATUS_VI[v.status]}</td></tr>)}
          </tbody></table></>}
        <p className="muted">Định khoản được sinh từ quy tắc đang hiệu lực cho nghiệp vụ đã chọn; số tiền thanh toán không được vượt số còn phải thu/trả của chứng từ.</p>
      </div>
    </>
  )
}
