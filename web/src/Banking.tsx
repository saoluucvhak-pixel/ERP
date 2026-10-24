import { useCallback, useEffect, useState } from 'react'
import { get, post } from './api'
import { AccountSelect, ErrorBox, ExportButton, FlowStrip, PartnerSelect, PrintSheet, printSheet, useAction, useCatalog, useCompanyName } from './common'
import { addMoney, amountInWords, csvCell, formatDate, formatMoney, parseMoney, today } from './money'
import type { PartnerBank } from './BankAccounts'

interface PaymentOrder { id: string; rowVersion: number; number: string; bankAccountId: string; beneficiaryAccountNo: string | null; beneficiaryBank: string | null; partnerId: string; amount: string; requestedDate: string; description: string; status: string; executedDate: string | null; bankReference: string | null; voucherId: string | null }

const PO_STATUS: Record<string, string> = {
  Created: 'Đã lập', Approved: 'Đã duyệt', SentToBank: 'Đã gửi NH', ExecutedByBank: 'NH đã thực hiện', Posted: 'Đã hạch toán', Reconciled: 'Đã đối chiếu sao kê', Cancelled: 'Đã hủy',
}

export function BankingView() {
  const [tab, setTab] = useState<'orders' | 'recon'>('orders')
  return (
    <>
      <div className="toolbar">
        <h2>Ngân hàng</h2>
        <button className={tab === 'orders' ? 'primary' : ''} onClick={() => setTab('orders')}>Lệnh thanh toán</button>
        <button className={tab === 'recon' ? 'primary' : ''} onClick={() => setTab('recon')}>Sao kê &amp; đối chiếu</button>
      </div>
      {tab === 'orders' ? <PaymentOrders /> : <Reconciliation />}
    </>
  )
}

function PaymentOrders() {
  const { accounts, partners } = useCatalog()
  const [list, setList] = useState<PaymentOrder[]>([])
  const [form, setForm] = useState({ bankAccountId: '', partnerId: '', amount: '', requestedDate: today(), description: '', beneficiaryAccountNo: '', beneficiaryBank: '' })
  const [info, setInfo] = useState('')
  const [pbanks, setPbanks] = useState<PartnerBank[]>([])
  const [cbanks, setCbanks] = useState<{ accountId: string; bankName: string; accountNo: string; accountName: string }[]>([])
  const [picked, setPicked] = useState<Set<string>>(new Set())
  const [printing, setPrinting] = useState<PaymentOrder[] | null>(null)
  const { busy, error, setError, run } = useAction()
  const load = useCallback(() => run(async () => {
    setList(await get<PaymentOrder[]>('/payment-orders'))
    setPbanks(await get<PartnerBank[]>('/bank-accounts/partners').catch(() => []))
    setCbanks(await get<{ accountId: string; bankName: string; accountNo: string; accountName: string }[]>('/bank-accounts/company').catch(() => []))
  }), [run])
  useEffect(() => { load() }, [load])
  const act = (fn: () => Promise<unknown>, msg: string) => run(async () => { await fn(); setInfo(msg); await load() })
  const partnerName = (id: string) => partners.find(p => p.id === id)?.code ?? ''
  /** Tên chủ tài khoản thụ hưởng: theo tài khoản trùng số trong danh mục của đối tượng, nếu không có thì tên đối tượng. */
  const beneficiary = (p: PaymentOrder) => pbanks.find(b => b.partnerId === p.partnerId && b.accountNo === p.beneficiaryAccountNo)?.accountName ?? partners.find(x => x.id === p.partnerId)?.name ?? ''
  const payer = (p: PaymentOrder) => cbanks.find(c => c.accountId === p.bankAccountId)
  const chosen = list.filter(p => picked.has(p.id))
  const csv = () => {
    const q = csvCell
    const body = ['STT;Số UNC;Tên người thụ hưởng;Số tài khoản;Ngân hàng;Số tiền;Nội dung'].concat(chosen.map((p, i) => [i + 1, p.number, q(beneficiary(p)), `'${p.beneficiaryAccountNo ?? ''}`, q(p.beneficiaryBank ?? ''), p.amount.replace('.', ','), q(p.description)].join(';')))
    const url = URL.createObjectURL(new Blob(['\ufeff' + body.join('\r\n')], { type: 'text/csv;charset=utf-8' }))
    const a = document.createElement('a'); a.href = url; a.download = `Chuyen_tien_${today()}.csv`; a.click(); URL.revokeObjectURL(url)
  }

  const create = () => {
    const amount = parseMoney(form.amount)
    if (!amount) { setError(new Error('Số tiền không hợp lệ.')); return }
    act(() => post('/payment-orders', { ...form, amount, settleJournalLineId: null, businessEvent: 'SUPPLIER_PAYMENT' }), 'Đã lập lệnh thanh toán.')
  }

  return (
    <>
      <FlowStrip steps={[
        { label: 'Lệnh thanh toán Nháp (từ chấp nhận thanh toán, hàng chờ trả lương hoặc lập tay)', journal: false, count: `${list.filter(o => o.status === 'Created').length} lệnh chờ duyệt` },
        { label: 'Duyệt lệnh (người khác người lập)', journal: false, count: `${list.filter(o => o.status === 'Approved').length} lệnh chờ gửi ngân hàng` },
        { label: 'Gửi ngân hàng', journal: false, count: `${list.filter(o => o.status === 'SentToBank').length} lệnh chờ ngân hàng chi` },
        { label: 'Ngân hàng đã chi → lập chứng từ chi', journal: true, screen: 'vouchers', count: `${list.filter(o => o.status === 'ExecutedByBank').length} lệnh chờ ghi sổ` },
        { label: 'Đối chiếu sao kê', journal: false, count: `${list.filter(o => o.status === 'Posted').length} lệnh chờ đối chiếu` },
      ]} />
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      <div className="panel">
        <div className="form-row">
          <label>Tài khoản chi<AccountSelect accounts={accounts} value={form.bankAccountId} onChange={v => setForm(f => ({ ...f, bankAccountId: v }))} filter={a => a.cashKind === 'Bank'} /></label>
          <label>Nhà cung cấp<PartnerSelect partners={partners} value={form.partnerId} onChange={v => setForm(f => ({ ...f, partnerId: v }))} /></label>
          <label>Số tiền<input className="num" value={form.amount} onChange={e => setForm(f => ({ ...f, amount: e.target.value }))} onBlur={() => { const p = parseMoney(form.amount); if (p) setForm(f => ({ ...f, amount: formatMoney(p) })) }} /></label>
          <label>Ngày đề nghị<input type="date" value={form.requestedDate} onChange={e => setForm(f => ({ ...f, requestedDate: e.target.value }))} /></label>
          <label>Số TK thụ hưởng<input value={form.beneficiaryAccountNo} onChange={e => setForm(f => ({ ...f, beneficiaryAccountNo: e.target.value }))} /></label>
          <label>Ngân hàng thụ hưởng<input value={form.beneficiaryBank} onChange={e => setForm(f => ({ ...f, beneficiaryBank: e.target.value }))} /></label>
          <label style={{ flex: 1, minWidth: 220 }}>Nội dung<input value={form.description} onChange={e => setForm(f => ({ ...f, description: e.target.value }))} /></label>
          <button className="primary" onClick={create} disabled={busy} style={{ alignSelf: 'flex-end' }}>Lập lệnh</button>
        </div>
      </div>
      <div className="toolbar">
        <span className="muted">Đã chọn {chosen.length} lệnh · {formatMoney(addMoney(chosen.map(p => p.amount)))}</span>
        <ExportButton fileName={`Chuyen_tien_${today()}`} sheet="Chuyển tiền" disabled={chosen.length === 0}
          columns={[{ header: 'STT', width: 6 }, { header: 'Số UNC', width: 16 }, { header: 'Tên người thụ hưởng', width: 34 }, { header: 'Số tài khoản', width: 20 }, { header: 'Ngân hàng', width: 28 }, { header: 'Số tiền', numeric: true, width: 18 }, { header: 'Nội dung', width: 40 }]}
          rows={chosen.map((p, i) => [String(i + 1), p.number, beneficiary(p), p.beneficiaryAccountNo, p.beneficiaryBank, p.amount, p.description])} />
        <button disabled={chosen.length === 0} onClick={csv} title="CSV UTF-8, dấu ; ngăn cột — để nạp vào ngân hàng điện tử (mẫu từng ngân hàng khác nhau: đối chiếu trước khi nạp)">Xuất CSV</button>
        <button disabled={chosen.length === 0} onClick={() => setPrinting(chosen)}>In UNC đã chọn</button>
      </div>
      <div className="scroll">
        <table className="grid">
          <thead><tr><th /><th>Số</th><th>Ngày</th><th>NCC</th><th>Tài khoản thụ hưởng</th><th>Nội dung</th><th className="num">Số tiền</th><th>Trạng thái</th><th>Mã GD NH</th><th>Thao tác</th></tr></thead>
          <tbody>{list.map(p => (
            <tr key={p.id}>
              <td><input type="checkbox" aria-label={`Chọn ${p.number}`} disabled={p.status === 'Cancelled'} checked={picked.has(p.id)} onChange={e => setPicked(s => { const n = new Set(s); if (e.target.checked) n.add(p.id); else n.delete(p.id); return n })} /></td>
              <td>{p.number}</td><td>{formatDate(p.requestedDate)}</td><td>{partnerName(p.partnerId)}</td>
              <td>{p.beneficiaryAccountNo ? <>{p.beneficiaryAccountNo}<div className="muted">{p.beneficiaryBank}</div></> : <span className="muted" title="Khai báo ở Danh mục đối tượng › Ngân hàng">chưa có</span>}</td><td>{p.description}</td>
              <td className="num">{formatMoney(p.amount)}</td><td><span className="badge">{PO_STATUS[p.status] ?? p.status}</span></td><td>{p.bankReference}</td>
              <td style={{ display: 'flex', gap: 4 }}>
                {p.status === 'Created' && <button disabled={busy} onClick={() => act(() => post(`/payment-orders/${p.id}/approve?rowVersion=${p.rowVersion}`), `Đã duyệt ${p.number}.`)}>Duyệt</button>}
                {p.status === 'Approved' && <button disabled={busy} onClick={() => act(() => post(`/payment-orders/${p.id}/send`), `Đã ghi nhận gửi ngân hàng ${p.number}.`)}>Đã gửi NH</button>}
                {p.status === 'SentToBank' && <button onClick={() => {
                  const ref = prompt('Mã giao dịch ngân hàng:'); if (!ref) return
                  const date = prompt('Ngày ngân hàng thực hiện (yyyy-mm-dd):', today()); if (!date) return
                  act(() => post(`/payment-orders/${p.id}/execute`, { executedDate: date, bankReference: ref }),
                    `Đã ghi nhận ngân hàng thực hiện ${p.number}; đã tạo báo Nợ (Nháp) — duyệt và ghi sổ ở màn hình Chứng từ.`)
                }}>NH đã thực hiện</button>}
                {p.status === 'ExecutedByBank' && <button disabled={busy} onClick={() => act(() => post(`/payment-orders/${p.id}/recreate-voucher`), 'Đã lập lại chứng từ hạch toán.')}>Lập lại chứng từ</button>}
                {p.status !== 'Cancelled' && <button onClick={() => setPrinting([p])}>In UNC</button>}
                {['Created', 'Approved', 'SentToBank'].includes(p.status) && <button className="danger" onClick={() => {
                  const reason = prompt('Lý do hủy lệnh:'); if (reason) act(() => post(`/payment-orders/${p.id}/cancel`, { reason }), `Đã hủy ${p.number}.`)
                }}>Hủy</button>}
              </td>
            </tr>))}
            {list.length === 0 && <tr><td colSpan={10} className="muted">Chưa có lệnh thanh toán.</td></tr>}
          </tbody>
        </table>
      </div>
      {printing && <UncPrint orders={printing} beneficiary={beneficiary} payer={payer} partnerName={id => partners.find(p => p.id === id)?.name ?? ''} onDone={() => setPrinting(null)} />}
    </>
  )
}

/** Ủy nhiệm chi: mỗi lệnh một trang, đủ bên chi, bên thụ hưởng, số tiền bằng số và bằng chữ, nội dung, chữ ký. */
function UncPrint({ orders, beneficiary, payer, partnerName, onDone }: { orders: PaymentOrder[]; beneficiary: (p: PaymentOrder) => string; payer: (p: PaymentOrder) => { bankName: string; accountNo: string; accountName: string } | undefined; partnerName: (id: string) => string; onDone: () => void }) {
  const company = useCompanyName()
  useEffect(() => { const t = setTimeout(() => { printSheet(); onDone() }, 200); return () => clearTimeout(t) }, [onDone])
  return (
    <PrintSheet>
      {orders.map(p => {
        const pay = payer(p)
        return <div key={p.id} style={{ pageBreakAfter: 'always' }}>
          <div><b>{pay?.accountName || company}</b></div>
          <h1>ỦY NHIỆM CHI</h1>
          <div className="center">Số: <b>{p.number}</b> · Ngày {formatDate(p.requestedDate)}{p.status === 'Created' ? ' · (CHƯA DUYỆT)' : ''}</div>
          <table>
            <tbody>
              <tr><td><b>Đơn vị trả tiền</b></td><td>{pay?.accountName || company}</td></tr>
              <tr><td><b>Số tài khoản</b></td><td>{pay?.accountNo ?? '………………………………'}</td></tr>
              <tr><td><b>Tại ngân hàng</b></td><td>{pay?.bankName ?? '………………………………'}</td></tr>
              <tr><td><b>Đơn vị nhận tiền</b></td><td>{beneficiary(p) || partnerName(p.partnerId)}</td></tr>
              <tr><td><b>Số tài khoản</b></td><td>{p.beneficiaryAccountNo ?? '………………………………'}</td></tr>
              <tr><td><b>Tại ngân hàng</b></td><td>{p.beneficiaryBank ?? '………………………………'}</td></tr>
              <tr><td><b>Số tiền bằng số</b></td><td className="num"><b>{formatMoney(p.amount)}</b> đồng</td></tr>
              <tr><td><b>Số tiền bằng chữ</b></td><td><i>{amountInWords(p.amount)}</i></td></tr>
              <tr><td><b>Nội dung</b></td><td>{p.description}</td></tr>
            </tbody>
          </table>
          <div className="signs"><div><b>Người lập</b><br /><i>(Ký, họ tên)</i></div><div><b>Kế toán trưởng</b><br /><i>(Ký, họ tên)</i></div><div><b>Chủ tài khoản</b><br /><i>(Ký, họ tên, đóng dấu)</i></div></div>
          <div className="foot">Mẫu in nội bộ HAK Accounting — chưa đối chiếu với mẫu ủy nhiệm chi của ngân hàng [CẦN XÁC MINH]. In lúc {new Date().toLocaleString('vi-VN')}.</div>
        </div>
      })}
    </PrintSheet>
  )
}

interface Candidate { journalLineId: string; voucherNumber: string; postingDate: string; amount: string; score: number }
/** Dòng sao kê không có mã giao dịch được hệ thống gán mã "AUTO-…": hiển thị là chưa có mã. */
const refText = (r: string) => (r.startsWith('AUTO-') ? '(không có mã)' : r)

interface StatementOnly { id: string; transactionDate: string; amount: string; bankReference: string; description: string; status: string; candidates: Candidate[] }
interface BookOnly { journalLineId: string; voucherNumber: string; postingDate: string; signedAmount: string; description: string | null }
interface Report { bookBalance: string; statementBalance: string | null; statementOnly: StatementOnly[]; bookOnly: BookOnly[]; unexplainedDifference: string | null }
interface Match { id: string; origin: string; score: number; transactionDate: string; amount: string; bankReference: string; voucherNumber: string }

/** "yyyy-mm-dd;-12.000.000;REF;nội dung" → signed canonical amount; null when invalid. */
function parseSigned(s: string): string | null {
  const t = s.trim()
  const neg = t.startsWith('-')
  const p = parseMoney(neg ? t.slice(1) : t)
  return p == null ? null : (neg ? '-' + p : p)
}

function Reconciliation() {
  const { accounts } = useCatalog()
  const [bankAccountId, setBankAccountId] = useState('')
  const [asOf, setAsOf] = useState(today())
  const [csv, setCsv] = useState('')
  const [hdr, setHdr] = useState({ fromDate: today().slice(0, 8) + '01', toDate: today(), opening: '0', closing: '0', sourceName: 'sao-ke.csv' })
  const [report, setReport] = useState<Report | null>(null)
  const [matches, setMatches] = useState<Match[]>([])
  const [info, setInfo] = useState('')
  const [draft, setDraft] = useState<{ lineId: string; accountId: string; description: string; reason: string } | null>(null)
  const { busy, error, setError, run } = useAction()

  const load = () => run(async () => {
    setReport(await get<Report>(`/reports/bank-reconciliation?bankAccountId=${bankAccountId}&asOf=${asOf}`))
    setMatches(await get<Match[]>(`/bank-reconciliation/matches?bankAccountId=${bankAccountId}`))
  })

  const importCsv = () => run(async () => {
    const lines = csv.split(/\r?\n/).map(l => l.trim()).filter(Boolean).map((l, i) => {
      const [transactionDate, amount, bankReference, ...desc] = l.split(';')
      const a = parseSigned(amount ?? '')
      if (!/^\d{4}-\d{2}-\d{2}$/.test(transactionDate ?? '') || !a) throw new Error(`Dòng ${i + 1} sai định dạng: ngày;số tiền;mã GD (có thể bỏ trống);nội dung`)
      return { transactionDate, amount: a, bankReference: (bankReference ?? '').trim(), description: desc.join(';').trim() }
    })
    const opening = parseSigned(hdr.opening), closing = parseSigned(hdr.closing)
    if (!opening || !closing) { setError(new Error('Số dư đầu/cuối không hợp lệ.')); return }
    const sum = addMoney([opening, ...lines.map(l => l.amount)])
    if (sum !== closing) { setError(new Error(`Số dư đầu + phát sinh = ${formatMoney(sum)} khác số dư cuối ${formatMoney(closing)}.`)); return }
    const st = await post<{ id: string }>('/bank-statements', { bankAccountId, fromDate: hdr.fromDate, toDate: hdr.toDate, openingBalance: opening, closingBalance: closing, sourceName: hdr.sourceName, sourceContent: csv, lines })
    const r = await post<{ matched: number; pending: number; unmatched: number }>(`/bank-statements/${st.id}/auto-match`)
    setInfo(`Đã nhập ${lines.length} giao dịch. Tự khớp ${r.matched}, chờ xác nhận ${r.pending}, chưa khớp ${r.unmatched}.`)
    setCsv('')
    await load()
  })

  return (
    <>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      <div className="toolbar">
        <label>Tài khoản ngân hàng<AccountSelect accounts={accounts} value={bankAccountId} onChange={setBankAccountId} filter={a => a.cashKind === 'Bank'} /></label>
        <label>Đến ngày<input type="date" value={asOf} onChange={e => setAsOf(e.target.value)} /></label>
        <button className="primary" onClick={load} disabled={busy || !bankAccountId}>Xem đối chiếu</button>
      </div>
      <details className="panel">
        <summary>Nhập sao kê (CSV: <code>yyyy-mm-dd;số tiền (+ vào / − ra);mã GD (bỏ trống nếu ngân hàng không có);nội dung</code>)</summary>
        <div className="form-row" style={{ marginTop: 8 }}>
          <label>Từ ngày<input type="date" value={hdr.fromDate} onChange={e => setHdr(h => ({ ...h, fromDate: e.target.value }))} /></label>
          <label>Đến ngày<input type="date" value={hdr.toDate} onChange={e => setHdr(h => ({ ...h, toDate: e.target.value }))} /></label>
          <label>Số dư đầu<input className="num" value={hdr.opening} onChange={e => setHdr(h => ({ ...h, opening: e.target.value }))} /></label>
          <label>Số dư cuối<input className="num" value={hdr.closing} onChange={e => setHdr(h => ({ ...h, closing: e.target.value }))} /></label>
          <label>Tên file<input value={hdr.sourceName} onChange={e => setHdr(h => ({ ...h, sourceName: e.target.value }))} /></label>
        </div>
        <textarea aria-label="Nội dung sao kê" rows={6} style={{ width: '100%' }} value={csv} onChange={e => setCsv(e.target.value)} placeholder="2026-03-06;-12.000.000;FT26065ABC123;TT hoa don" />
        <button className="primary" onClick={importCsv} disabled={busy || !bankAccountId || !csv.trim()}>Nhập &amp; tự khớp</button>
      </details>
      {report && (
        <>
          <div className={`msg ${report.unexplainedDifference === '0' ? 'ok' : 'error'}`}>
            Số dư sổ: <strong>{formatMoney(report.bookBalance)}</strong> · Số dư sao kê: <strong>{report.statementBalance == null ? 'chưa có sao kê' : formatMoney(report.statementBalance)}</strong>
            {report.unexplainedDifference != null && <> · Chênh lệch chưa giải thích: <strong>{formatMoney(report.unexplainedDifference)}</strong></>}
          </div>
          <div className="panel">
            <strong>Giao dịch chỉ có trên sao kê ({report.statementOnly.length})</strong>
            <table className="grid">
              <thead><tr><th>Ngày</th><th>Mã GD</th><th>Nội dung</th><th className="num">Số tiền</th><th>Trạng thái / ứng viên</th></tr></thead>
              <tbody>{report.statementOnly.map(l => (
                <tr key={l.id}><td>{formatDate(l.transactionDate)}</td><td>{refText(l.bankReference)}</td><td>{l.description}</td><td className="num">{formatMoney(l.amount)}</td>
                  <td>{l.candidates.length === 0 ? <span className="muted">Chưa có bút toán tương ứng </span> : l.candidates.map(c => (
                    <button key={c.journalLineId} onClick={() => run(async () => { await post('/bank-reconciliation/confirm', { statementLineId: l.id, journalLineId: c.journalLineId }); await load() })}>
                      Khớp {c.voucherNumber} ({formatDate(c.postingDate)}, điểm {c.score})</button>))}
                    <button onClick={() => run(async () => {
                      const s = await get<{ accountId: string | null; reason: string }>(`/bank-statement-lines/${l.id}/suggestion`)
                      setDraft({ lineId: l.id, accountId: s.accountId ?? '', description: l.description, reason: s.reason })
                    })}>Tạo chứng từ</button>
                    {draft?.lineId === l.id && <div className="form-row" style={{ marginTop: 6 }}>
                      <span className="muted">{draft.reason}</span>
                      <label>TK đối ứng<AccountSelect accounts={accounts} value={draft.accountId} onChange={id => setDraft(d => d && { ...d, accountId: id })} filter={a => a.id !== bankAccountId && !a.requiresPartner} /></label>
                      <label>Diễn giải<input value={draft.description} onChange={e => setDraft(d => d && { ...d, description: e.target.value })} /></label>
                      <button className="primary" disabled={busy || !draft.accountId} onClick={() => run(async () => {
                        const v = await post<{ number: string }>(`/bank-statement-lines/${l.id}/voucher`, { counterAccountId: draft.accountId, description: draft.description || null })
                        setDraft(null); setInfo(`Đã tạo chứng từ nháp ${v.number}. Gửi duyệt, ghi sổ ở màn hình Chứng từ, rồi bấm Xem đối chiếu và Khớp.`)
                      })}>Tạo nháp</button>
                      <button onClick={() => setDraft(null)}>Hủy</button></div>}</td></tr>))}
              </tbody>
            </table>
          </div>
          <div className="panel">
            <strong>Bút toán chỉ có trên sổ ({report.bookOnly.length})</strong>
            <table className="grid">
              <thead><tr><th>Ngày</th><th>Số CT</th><th>Diễn giải</th><th className="num">Số tiền</th></tr></thead>
              <tbody>{report.bookOnly.map(b => <tr key={b.journalLineId}><td>{formatDate(b.postingDate)}</td><td>{b.voucherNumber}</td><td>{b.description}</td><td className="num">{formatMoney(b.signedAmount)}</td></tr>)}</tbody>
            </table>
          </div>
          <div className="panel">
            <strong>Đã đối chiếu ({matches.length})</strong>
            <table className="grid">
              <thead><tr><th>Ngày</th><th>Mã GD</th><th>Số CT</th><th className="num">Số tiền</th><th>Cách khớp</th><th /></tr></thead>
              <tbody>{matches.map(m => (
                <tr key={m.id}><td>{formatDate(m.transactionDate)}</td><td>{refText(m.bankReference)}</td><td>{m.voucherNumber}</td><td className="num">{formatMoney(m.amount)}</td>
                  <td>{m.origin === 'Auto' ? `Tự động (điểm ${m.score})` : 'Thủ công'}</td>
                  <td><button className="danger" onClick={() => { const reason = prompt('Lý do hủy đối chiếu:'); if (reason) run(async () => { await post(`/bank-reconciliation/${m.id}/unmatch`, { reason }); await load() }) }}>Hủy khớp</button></td></tr>))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </>
  )
}
