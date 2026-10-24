import { useEffect, useState } from 'react'
import { get, post, session, type OpenItem, type TrialBalanceRow } from './api'
import { AccountSelect, ErrorBox, ExportButton, navStore, PartnerSelect, PrintSheet, printSheet, TYPE_VI, useAction, useCatalog } from './common'
import { addMoney, amountInWords, formatDate, formatMoney, parseMoney, today } from './money'

const firstOfMonth = () => today().slice(0, 8) + '01'

export function TrialBalanceView() {
  const [from, setFrom] = useState(firstOfMonth())
  const [to, setTo] = useState(today())
  const [rollup, setRollup] = useState(true)
  const [data, setData] = useState<{ isBalanced: boolean; rows: TrialBalanceRow[]; groups: TrialBalanceRow[]; totals: Record<string, string> } | null>(null)
  const { busy, error, run } = useAction()
  const load = () => run(async () => setData(await get(`/reports/trial-balance?from=${from}&to=${to}&rollup=${rollup}`)))
  const t = data?.totals
  // Parent (summary) accounts interleaved with their children by account code; they never enter the totals.
  const lines = data ? [...data.rows, ...(data.groups ?? [])].sort((a, b) => (a.accountCode < b.accountCode ? -1 : a.accountCode > b.accountCode ? 1 : (b.isGroup ? 1 : 0) - (a.isGroup ? 1 : 0))) : []
  const amounts = (r: TrialBalanceRow) => [r.openingDebit, r.openingCredit, r.periodDebit, r.periodCredit, r.closingDebit, r.closingCredit]
  return (
    <>
      <div className="toolbar">
        <h2>Bảng cân đối số phát sinh</h2>
        <label>Từ ngày<input type="date" value={from} onChange={e => { setFrom(e.target.value); setData(null) }} /></label>
        <label>Đến ngày<input type="date" value={to} onChange={e => { setTo(e.target.value); setData(null) }} /></label>
        <label><input type="checkbox" checked={rollup} onChange={e => setRollup(e.target.checked)} /> Cộng dồn TK tổng hợp</label>
        <button className="primary" onClick={load} disabled={busy}>Xem</button>
        <ExportButton fileName={`BCDPS_${from}_${to}`} sheet="BCĐPS" title="Bảng cân đối số phát sinh" subtitle={`Từ ngày ${from.split('-').reverse().join('/')} đến ngày ${to.split('-').reverse().join('/')}`} disabled={!data}
          columns={[{ header: 'TK' }, { header: 'Tên tài khoản', width: 40 }, ...['Dư đầu Nợ', 'Dư đầu Có', 'PS Nợ', 'PS Có', 'Dư cuối Nợ', 'Dư cuối Có'].map(h => ({ header: h, numeric: true }))]}
          rows={[...lines.map(r => [r.accountCode, '  '.repeat(r.level ?? 0) + r.accountName, ...amounts(r)]),
            ...(t ? [['Cộng', '', t.totalOpeningDebit, t.totalOpeningCredit, t.totalPeriodDebit, t.totalPeriodCredit, t.totalClosingDebit, t.totalClosingCredit]] : [])]} />
      </div>
      <ErrorBox error={error} />
      {data && (
        <>
          <div className={`msg ${data.isBalanced ? 'ok' : 'error'}`}>{data.isBalanced ? 'Cân đối: tổng Nợ = tổng Có ở mọi cột.' : 'KHÔNG cân đối — cần kiểm tra ngay.'}</div>
          <div className="scroll">
            <table className="grid">
              <thead><tr><th>TK</th><th>Tên tài khoản</th><th className="num">Dư đầu Nợ</th><th className="num">Dư đầu Có</th><th className="num">PS Nợ</th><th className="num">PS Có</th><th className="num">Dư cuối Nợ</th><th className="num">Dư cuối Có</th></tr></thead>
              <tbody>{lines.map(r => (
                <tr key={r.accountCode + (r.isGroup ? 'g' : '')} style={{ ...(r.isGroup ? { fontWeight: 600 } : {}), cursor: r.accountId && !r.isGroup ? 'pointer' : undefined }}
                  title={r.accountId && !r.isGroup ? 'Bấm để xem sổ chi tiết tài khoản này' : undefined}
                  onClick={() => { if (r.accountId && !r.isGroup) { navStore.ledger = { accountId: r.accountId, from, to }; navStore.go?.('ledger') } }}><td>{r.accountCode}</td>
                  <td style={{ paddingLeft: 8 + 14 * (r.level ?? 0) }}>{r.accountName}</td>
                  {amounts(r).map((v, i) => <td key={i} className="num">{v === '0' ? '' : formatMoney(v)}</td>)}</tr>))}
              </tbody>
              {t && <tfoot><tr><td colSpan={2}>Cộng</td>
                {['totalOpeningDebit', 'totalOpeningCredit', 'totalPeriodDebit', 'totalPeriodCredit', 'totalClosingDebit', 'totalClosingCredit'].map(k => <td key={k} className="num">{formatMoney(t[k])}</td>)}</tr></tfoot>}
            </table>
          </div>
        </>
      )}
    </>
  )
}

interface LedgerRow { postingDate: string; voucherType: string; voucherNumber: string; description: string | null; counterAccounts: string; debit: string; credit: string; runningBalance: string }
interface Ledger { accountCode: string; accountName: string; openingBalance: string; rows: LedgerRow[]; totalDebit: string; totalCredit: string; closingBalance: string }

const signed = (v: string) => (v.startsWith('-') ? `${formatMoney(v.slice(1))} Có` : v === '0' ? '0' : `${formatMoney(v)} Nợ`)

export function AccountLedgerView() {
  const { accounts, partners } = useCatalog()
  const jump = navStore.ledger
  const [accountId, setAccountId] = useState(jump?.accountId ?? '')
  const [partnerId, setPartnerId] = useState('')
  const [from, setFrom] = useState(jump?.from ?? firstOfMonth())
  const [to, setTo] = useState(jump?.to ?? today())
  const [data, setData] = useState<Ledger | null>(null)
  const { busy, error, run } = useAction()
  const load = () => run(async () => {
    const q = new URLSearchParams({ accountId, from, to, ...(partnerId ? { partnerId } : {}) })
    setData(await get(`/reports/account-ledger?${q}`))
  })
  // Arrived from a trial-balance row: show that account at once, then forget the request.
  useEffect(() => { if (navStore.ledger) { navStore.ledger = undefined; load() } }, [])
  return (
    <>
      <div className="toolbar">
        <h2>Sổ chi tiết tài khoản / Sổ quỹ</h2>
        <label>Tài khoản<AccountSelect accounts={accounts} value={accountId} onChange={setAccountId} /></label>
        <label>Đối tượng<PartnerSelect partners={partners} value={partnerId} onChange={setPartnerId} /></label>
        <label>Từ ngày<input type="date" value={from} onChange={e => { setFrom(e.target.value); setData(null) }} /></label>
        <label>Đến ngày<input type="date" value={to} onChange={e => { setTo(e.target.value); setData(null) }} /></label>
        <button className="primary" onClick={load} disabled={busy || !accountId}>Xem</button>
        <ExportButton fileName={`So_chi_tiet_${data?.accountCode ?? ''}_${from}_${to}`} sheet={`Sổ TK ${data?.accountCode ?? ''}`} title={`Sổ chi tiết tài khoản ${data?.accountCode ?? ''}`}
          subtitle={`${data?.accountName ?? ''} · từ ${from.split('-').reverse().join('/')} đến ${to.split('-').reverse().join('/')}`} disabled={!data}
          columns={[{ header: 'Ngày' }, { header: 'Số CT' }, { header: 'Loại' }, { header: 'Diễn giải', width: 40 }, { header: 'TK đối ứng' },
            { header: 'Nợ', numeric: true }, { header: 'Có', numeric: true }, { header: 'Số dư (+Nợ / −Có)', numeric: true }]}
          rows={data ? [['', '', '', 'Số dư đầu kỳ', '', '', '', data.openingBalance],
            ...data.rows.map(r => [r.postingDate, r.voucherNumber, TYPE_VI[r.voucherType] ?? r.voucherType, r.description, r.counterAccounts, r.debit, r.credit, r.runningBalance]),
            ['', '', '', 'Cộng phát sinh / số dư cuối kỳ', '', data.totalDebit, data.totalCredit, data.closingBalance]] : []} />
      </div>
      <ErrorBox error={error} />
      {data && (
        <div className="scroll">
          <table className="grid">
            <thead><tr><th>Ngày</th><th>Số CT</th><th>Loại</th><th>Diễn giải</th><th>TK đối ứng</th><th className="num">Nợ</th><th className="num">Có</th><th className="num">Số dư</th></tr></thead>
            <tbody>
              <tr><td colSpan={7}><strong>Số dư đầu kỳ</strong></td><td className="num">{signed(data.openingBalance)}</td></tr>
              {data.rows.map((r, i) => (
                <tr key={i}><td>{formatDate(r.postingDate)}</td><td>{r.voucherNumber}</td><td>{TYPE_VI[r.voucherType] ?? r.voucherType}</td><td>{r.description}</td><td>{r.counterAccounts}</td>
                  <td className="num">{r.debit === '0' ? '' : formatMoney(r.debit)}</td><td className="num">{r.credit === '0' ? '' : formatMoney(r.credit)}</td><td className="num">{signed(r.runningBalance)}</td></tr>))}
            </tbody>
            <tfoot><tr><td colSpan={5}>Cộng phát sinh / Số dư cuối kỳ</td><td className="num">{formatMoney(data.totalDebit)}</td><td className="num">{formatMoney(data.totalCredit)}</td><td className="num">{signed(data.closingBalance)}</td></tr></tfoot>
          </table>
        </div>
      )}
    </>
  )
}

interface AllocationRow { id: string; allocationDate: string; amount: string; origin: string; isActive: boolean; voidReason: string | null; debitVoucherNumber: string; creditVoucherNumber: string }
const ORIGIN_VI: Record<string, string> = { VOUCHER: 'Theo chứng từ', MANUAL: 'Thủ công', AUTO_FIFO: 'Tự động FIFO', REVERSAL: 'Đảo chứng từ' }

/** Biên bản đối chiếu công nợ: số dư đầu kỳ, phát sinh, số dư cuối kỳ của một đối tượng, hai bên cùng ký xác nhận. */
function DebtConfirmation({ ledger, partner, from, to, onDone }: { ledger: Ledger; partner: string; from: string; to: string; onDone: () => void }) {
  const [company, setCompany] = useState('')
  useEffect(() => { get<{ id: string; name: string }[]>('/me/companies').then(cs => setCompany(cs.find(c => c.id === session.companyId)?.name ?? '')).catch(() => undefined) }, [])
  useEffect(() => { const t = setTimeout(() => { printSheet(); onDone() }, 150); return () => clearTimeout(t) }, [onDone])
  const d = (s: string) => s.split('-').reverse().join('/')
  const bal = ledger.closingBalance
  const neg = bal.startsWith('-')
  const abs = neg ? bal.slice(1) : bal
  return (
    <PrintSheet>
      <div><b>{company}</b></div>
      <h1>BIÊN BẢN ĐỐI CHIẾU CÔNG NỢ</h1>
      <div className="center">Từ ngày {d(from)} đến ngày {d(to)} · Tài khoản {ledger.accountCode} – {ledger.accountName}</div>
      <p>Đối tượng: <b>{partner}</b></p>
      <table>
        <thead><tr><th>Ngày</th><th>Số CT</th><th>Diễn giải</th><th>Phát sinh Nợ</th><th>Phát sinh Có</th><th>Số dư</th></tr></thead>
        <tbody>
          <tr><td colSpan={5}><b>Số dư đầu kỳ</b></td><td className="num">{formatMoney(ledger.openingBalance)}</td></tr>
          {ledger.rows.map((r, i) => <tr key={i}><td>{d(r.postingDate)}</td><td>{r.voucherNumber}</td><td>{r.description}</td>
            <td className="num">{r.debit === '0' ? '' : formatMoney(r.debit)}</td><td className="num">{r.credit === '0' ? '' : formatMoney(r.credit)}</td><td className="num">{formatMoney(r.runningBalance)}</td></tr>)}
        </tbody>
        <tfoot>
          <tr><td colSpan={3}><b>Cộng phát sinh</b></td><td className="num">{formatMoney(ledger.totalDebit)}</td><td className="num">{formatMoney(ledger.totalCredit)}</td><td /></tr>
          <tr><td colSpan={5}><b>Số dư cuối kỳ ({neg ? 'Có' : 'Nợ'})</b></td><td className="num"><b>{formatMoney(abs)}</b></td></tr>
        </tfoot>
      </table>
      <p>Bằng chữ: <i>{amountInWords(abs)}</i></p>
      <p>Hai bên thống nhất số dư công nợ đến ngày {d(to)} như trên. Mọi chênh lệch (nếu có) xin phản hồi: ……………………………………………</p>
      <div className="signs"><div><b>Đại diện {company}</b><br /><i>(Ký, họ tên, đóng dấu)</i></div><div><b>Đại diện {partner}</b><br /><i>(Ký, họ tên, đóng dấu)</i></div></div>
      <div className="foot">Mẫu in nội bộ HAK Accounting. Số liệu là số đã ghi sổ, in lúc {new Date().toLocaleString('vi-VN')}.</div>
    </PrintSheet>
  )
}

interface AgingRow { partnerId: string; partnerCode: string; partnerName: string; notDue: string; buckets: string[]; openObligations: string; unappliedCredits: string; net: string }

export function ReceivablesView() {
  const { accounts, partners } = useCatalog()
  // Chiều do phân hệ đang mở quyết định: Bán hàng → công nợ phải thu, Mua hàng → công nợ phải trả (hai chu trình không lẫn trong một danh sách tài khoản).
  const [side] = useState(() => navStore.kind)
  const openAccounts = accounts.filter(a => side === 'Sales' ? a.openItemNature === 'Receivable' : side === 'Purchase' ? a.openItemNature === 'Payable' : a.openItemNature !== 'None')
  const [accountId, setAccountId] = useState('')
  const [asOf, setAsOf] = useState(today())
  const [aging, setAging] = useState<{ bucketLabels: string[]; rows: AgingRow[] } | null>(null)
  const [partnerId, setPartnerId] = useState('')
  const [items, setItems] = useState<OpenItem[]>([])
  const [recon, setRecon] = useState<{ isReconciled: boolean; rows: unknown[] } | null>(null)
  const [allocs, setAllocs] = useState<AllocationRow[]>([])
  const [sel, setSel] = useState<string[]>([])
  const [anchor, setAnchor] = useState('')   // khoản gốc để chia cho nhiều chứng từ
  const [allocAmount, setAllocAmount] = useState('')
  const [confirm, setConfirm] = useState<Ledger | null>(null)
  const [info, setInfo] = useState('')
  const { busy, error, run } = useAction()

  const load = () => run(async () => {
    setAging(await get(`/reports/aging?accountId=${accountId}&asOf=${asOf}`))
    setRecon(await get(`/reports/subledger-reconciliation?asOf=${asOf}`))
    setItems(partnerId ? await get(`/settlement/open-items?accountId=${accountId}&partnerId=${partnerId}&asOf=${asOf}`) : [])
    setAllocs(partnerId ? await get(`/settlement/allocations?accountId=${accountId}&partnerId=${partnerId}`) : [])
    setSel([]); setAnchor('')
  })
  const picked = items.filter(i => sel.includes(i.journalLineId))
  const anchorItem = items.find(i => i.journalLineId === anchor)
  const splitTargets = anchorItem ? picked.filter(i => i.side !== anchorItem.side && !i.foreignCurrency) : []
  const splitNeed = addMoney(splitTargets.map(t => t.open))
  const pd = picked.find(i => i.side === 'Debit'), pc = picked.find(i => i.side === 'Credit')
  const canMatch = picked.length === 2 && !!pd && !!pc && !pd.foreignCurrency && !pc.foreignCurrency
  // Largest amount both items can still take (decimal strings; never floats).
  const maxMatch = canMatch ? (addMoney([pd.open, '-' + pc.open]).startsWith('-') ? pd.open : pc.open) : '0'
  const match = () => run(async () => {
    const amount = allocAmount.trim() ? parseMoney(allocAmount) : maxMatch
    if (!amount) throw new Error('Số tiền phân bổ không hợp lệ.')
    await post('/settlement/allocate', { lineA: pd!.journalLineId, lineB: pc!.journalLineId, amount, date: asOf })
    setInfo('Đã phân bổ.'); await load()
  })
  // Chia một khoản (phiếu thu / khoản có chưa phân bổ) cho nhiều chứng từ trong một giao dịch: lấp theo hạn cũ nhất trước đến hết số còn của khoản gốc.
  const split = () => run(async () => {
    const targets = [...splitTargets].sort((a, b) => a.dueDate.localeCompare(b.dueDate) || a.postingDate.localeCompare(b.postingDate)).map(t => ({ lineId: t.journalLineId }))
    const res = await post<{ amount: string }[]>('/settlement/allocate-many', { anchorLineId: anchor, targets, date: asOf })
    setInfo(`Đã phân bổ ${res.length} chứng từ, tổng ${formatMoney(addMoney(res.map(r => r.amount)))}.`); await load()
  })
  const voidAlloc = (a: AllocationRow) => {
    const reason = prompt(`Lý do hủy phân bổ ${a.debitVoucherNumber} ↔ ${a.creditVoucherNumber} (${formatMoney(a.amount)}):`)
    if (reason) run(async () => { await post(`/settlement/${a.id}/void`, { reason }); setInfo('Đã hủy phân bổ.'); await load() })
  }
  const printConfirmation = () => run(async () => {
    const q = new URLSearchParams({ accountId, partnerId, from: `${asOf.slice(0, 4)}-01-01`, to: asOf })
    setConfirm(await get<Ledger>(`/reports/account-ledger?${q}`))
  })
  const auto = () => run(async () => { await post('/settlement/auto', { accountId, partnerId, date: asOf }); await load() })

  return (
    <>
      <div className="toolbar">
        <h2>Công nợ</h2>
        <label>Tài khoản công nợ<AccountSelect accounts={openAccounts} value={accountId} onChange={setAccountId} /></label>
        <label>Đối tượng (chi tiết)<PartnerSelect partners={partners} value={partnerId} onChange={setPartnerId} /></label>
        <label>Đến ngày<input type="date" value={asOf} onChange={e => setAsOf(e.target.value)} /></label>
        <button className="primary" onClick={load} disabled={busy || !accountId}>Xem</button>
        <button onClick={auto} disabled={busy || !accountId || !partnerId}>Tự động bù trừ (FIFO)</button>
        <button onClick={printConfirmation} disabled={busy || !accountId || !partnerId}>Biên bản đối chiếu</button>
        <ExportButton fileName={`Tuoi_no_${asOf}`} sheet="Tuổi nợ" disabled={!aging}
          columns={[{ header: 'Mã' }, { header: 'Đối tượng', width: 36 }, { header: 'Chưa đến hạn', numeric: true },
            ...(aging?.bucketLabels ?? []).map(b => ({ header: `Quá hạn ${b} ngày`, numeric: true })),
            { header: 'Tổng còn nợ', numeric: true }, { header: 'Trả trước chưa phân bổ', numeric: true }, { header: 'Thuần', numeric: true }]}
          rows={(aging?.rows ?? []).map(r => [r.partnerCode, r.partnerName, r.notDue, ...r.buckets, r.openObligations, r.unappliedCredits, r.net])} />
      </div>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      {confirm && <DebtConfirmation ledger={confirm} partner={(() => { const p = partners.find(x => x.id === partnerId); return p ? `${p.code} – ${p.name}` : '' })()} from={`${asOf.slice(0, 4)}-01-01`} to={asOf} onDone={() => setConfirm(null)} />}
      {recon && <div className={`msg ${recon.isReconciled ? 'ok' : 'error'}`}>{recon.isReconciled ? 'Sổ chi tiết công nợ khớp sổ cái.' : `Sổ chi tiết lệch sổ cái: ${recon.rows.length} dòng cần kiểm tra.`}</div>}
      {aging && (
        <div className="panel">
          <strong>Tuổi nợ đến {formatDate(asOf)}</strong>
          <table className="grid">
            <thead><tr><th>Đối tượng</th><th className="num">Chưa đến hạn</th>{aging.bucketLabels.map(b => <th key={b} className="num">Quá hạn {b} ngày</th>)}<th className="num">Tổng còn nợ</th><th className="num">Trả trước chưa phân bổ</th><th className="num">Thuần</th></tr></thead>
            <tbody>{aging.rows.map(r => (
              <tr key={r.partnerId} onClick={() => setPartnerId(r.partnerId)} style={{ cursor: 'pointer' }}>
                <td>{r.partnerCode} – {r.partnerName}</td><td className="num">{formatMoney(r.notDue)}</td>
                {r.buckets.map((b, i) => <td key={i} className="num">{formatMoney(b)}</td>)}
                <td className="num">{formatMoney(r.openObligations)}</td><td className="num">{formatMoney(r.unappliedCredits)}</td><td className="num">{formatMoney(r.net)}</td></tr>))}
            </tbody>
          </table>
        </div>
      )}
      {items.length > 0 && (
        <div className="panel">
          <div className="toolbar">
            <strong>Chứng từ còn mở</strong>
            <span className="muted">Chọn 1 khoản Nợ và 1 khoản Có để ghép tay</span>
            <label>Số tiền (để trống = tối đa {formatMoney(maxMatch)})<input className="num" value={allocAmount} onChange={e => setAllocAmount(e.target.value)} disabled={!canMatch} /></label>
            <button className="primary" disabled={busy || !canMatch} onClick={match}>Ghép</button>
            <span className="muted">| Hoặc chia một khoản cho nhiều chứng từ: chọn "Gốc" ở một dòng rồi tích các dòng đối ứng</span>
            <button disabled={busy || !anchorItem || anchorItem.foreignCurrency != null || splitTargets.length === 0} onClick={split}
              title={anchorItem ? `Khoản gốc còn ${formatMoney(anchorItem.open)}; các chứng từ chọn cần ${formatMoney(splitNeed)}` : ''}>
              Chia khoản gốc cho {splitTargets.length} chứng từ{anchorItem ? ` (còn ${formatMoney(anchorItem.open)})` : ''}</button>
            {anchor && <button className="link" onClick={() => { setAnchor(''); setSel([]) }}>Bỏ khoản gốc</button>}
            {picked.some(i => i.foreignCurrency) && <span className="muted">Khoản ngoại tệ ghép qua màn Ngoại tệ.</span>}
          </div>
          <table className="grid">
            <thead><tr><th /><th>Gốc</th><th>Ngày</th><th>Hạn</th><th>Số CT</th><th>Loại</th><th>Chiều</th><th className="num">Số tiền</th><th className="num">Đã phân bổ</th><th className="num">Còn lại</th></tr></thead>
            <tbody>{items.map(o => (
              <tr key={o.journalLineId}><td><input type="checkbox" aria-label={`Chọn ${o.voucherNumber}`} checked={sel.includes(o.journalLineId)}
                disabled={!!anchorItem && (o.side === anchorItem.side || o.journalLineId === anchor)}
                onChange={e => setSel(s => e.target.checked
                  ? (anchorItem ? [...s, o.journalLineId] : [...s.filter(id => items.find(i => i.journalLineId === id)?.side !== o.side), o.journalLineId])
                  : s.filter(id => id !== o.journalLineId))} /></td>
                <td><input type="radio" name="anchor" aria-label={`Chọn ${o.voucherNumber} làm khoản gốc`} checked={anchor === o.journalLineId} disabled={!!o.foreignCurrency}
                  onChange={() => { setAnchor(o.journalLineId); setSel([]) }} /></td>
                <td>{formatDate(o.postingDate)}</td><td>{formatDate(o.dueDate)}</td><td>{o.voucherNumber}</td><td>{TYPE_VI[o.voucherType] ?? o.voucherType}</td>
                <td>{o.side === 'Debit' ? 'Nợ' : 'Có'}</td><td className="num">{formatMoney(o.amount)}</td><td className="num">{formatMoney(o.allocated)}</td><td className="num">{formatMoney(o.open)}</td></tr>))}
            </tbody>
          </table>
        </div>
      )}
      {allocs.length > 0 && (
        <div className="panel">
          <strong>Lịch sử phân bổ của đối tượng</strong>
          <table className="grid">
            <thead><tr><th>Ngày</th><th>CT bên Nợ</th><th>CT bên Có</th><th className="num">Số tiền</th><th>Nguồn</th><th>Trạng thái</th><th /></tr></thead>
            <tbody>{allocs.map(a => (
              <tr key={a.id} style={a.isActive ? undefined : { opacity: 0.55 }}>
                <td>{formatDate(a.allocationDate)}</td><td>{a.debitVoucherNumber}</td><td>{a.creditVoucherNumber}</td><td className="num">{formatMoney(a.amount)}</td>
                <td>{ORIGIN_VI[a.origin] ?? a.origin}</td><td>{a.isActive ? 'Hiệu lực' : `Đã hủy: ${a.voidReason ?? ''}`}</td>
                <td>{a.isActive && <button className="danger" disabled={busy} onClick={() => voidAlloc(a)}>Hủy</button>}</td></tr>))}
            </tbody>
          </table>
        </div>
      )}
    </>
  )
}

interface JournalRow { postingDate: string; voucherType: string; voucherNumber: string; lineNo: number; accountCode: string; partnerCode: string | null; debit: string; credit: string; description: string | null }

/** Sổ Nhật ký chung: every posted line in date / voucher order. */
export function GeneralJournalView() {
  const [from, setFrom] = useState(firstOfMonth())
  const [to, setTo] = useState(today())
  const [rows, setRows] = useState<JournalRow[] | null>(null)
  const { busy, error, run } = useAction()
  return (
    <>
      <div className="toolbar">
        <h2>Sổ Nhật ký chung</h2>
        <label>Từ ngày<input type="date" value={from} onChange={e => { setFrom(e.target.value); setRows(null) }} /></label>
        <label>Đến ngày<input type="date" value={to} onChange={e => { setTo(e.target.value); setRows(null) }} /></label>
        <button className="primary" disabled={busy} onClick={() => run(async () => setRows(await get<JournalRow[]>(`/reports/general-journal?from=${from}&to=${to}`)))}>Xem</button>
        <ExportButton fileName={`So_nhat_ky_chung_${from}_${to}`} sheet="Nhật ký chung" title="Sổ Nhật ký chung" subtitle={`Từ ngày ${from.split('-').reverse().join('/')} đến ngày ${to.split('-').reverse().join('/')}`} disabled={!rows}
          columns={[{ header: 'Ngày' }, { header: 'Số CT' }, { header: 'Loại' }, { header: 'Diễn giải', width: 40 }, { header: 'TK' }, { header: 'Đối tượng' },
            { header: 'Nợ', numeric: true }, { header: 'Có', numeric: true }]}
          rows={(rows ?? []).map(r => [r.postingDate, r.voucherNumber, TYPE_VI[r.voucherType] ?? r.voucherType, r.description, r.accountCode, r.partnerCode, r.debit, r.credit])} />
      </div>
      <ErrorBox error={error} />
      {rows && <div className="scroll"><table className="grid">
        <thead><tr><th>Ngày</th><th>Số CT</th><th>Loại</th><th>Diễn giải</th><th>TK</th><th>Đối tượng</th><th className="num">Nợ</th><th className="num">Có</th></tr></thead>
        <tbody>{rows.map(r => <tr key={`${r.voucherNumber}-${r.lineNo}`}><td>{formatDate(r.postingDate)}</td><td>{r.voucherNumber}</td><td>{TYPE_VI[r.voucherType] ?? r.voucherType}</td>
          <td>{r.description}</td><td>{r.accountCode}</td><td>{r.partnerCode}</td>
          <td className="num">{r.debit === '0' ? '' : formatMoney(r.debit)}</td><td className="num">{r.credit === '0' ? '' : formatMoney(r.credit)}</td></tr>)}
          {rows.length === 0 && <tr><td colSpan={8} className="muted">Không có bút toán trong khoảng ngày.</td></tr>}</tbody>
        <tfoot><tr><td colSpan={6}>Cộng</td><td className="num">{formatMoney(addMoney(rows.map(r => r.debit)))}</td><td className="num">{formatMoney(addMoney(rows.map(r => r.credit)))}</td></tr></tfoot>
      </table></div>}
    </>
  )
}