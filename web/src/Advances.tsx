import { useCallback, useEffect, useMemo, useState } from 'react'
import { get, post } from './api'
import { AccountSelect, ErrorBox, ExportButton, Modal, PartnerSelect, PrintSheet, printSheet, useAction, useCatalog, useCompanyName, FlowStrip } from './common'
import { addMoney, amountInWords, formatDate, formatMoney, parseMoney, today } from './money'
import { SmartFilter, useFilterState } from './SmartFilter'
import { matchesQuery } from './filter'

type AStatus = 'Requested' | 'Approved' | 'Paid' | 'Settled' | 'Rejected' | 'Cancelled'
interface Row { id: string; number: string; employeeId: string; employeeCode: string; employeeName: string; purpose: string; amount: string; requestDate: string; dueDate: string; status: AStatus
  paymentVoucherNumber: string | null; settlementVoucherNumber: string | null; paymentPending: boolean; settlementPending: boolean; outstanding: string; spentAmount: string | null; overdueDays: number }
interface View { row: Row; decisionNote: string | null; lines: { lineNo: number; expenseAccountCode: string; amount: string; description: string | null; documentNo: string | null; documentDate: string | null }[] }
interface Aging { employeeId: string; employeeCode: string; employeeName: string; notDue: string; over1To30: string; over31To60: string; over60: string; total: string }
interface SettleLine { accountId: string; amount: string; description: string; documentNo: string; documentDate: string }

const STATUS_VI: Record<AStatus, string> = { Requested: 'Chờ duyệt', Approved: 'Đã duyệt', Paid: 'Đã chi', Settled: 'Đã quyết toán', Rejected: 'Từ chối', Cancelled: 'Đã hủy' }
const stateText = (r: Row) => r.status === 'Approved' && r.paymentPending ? `Đã duyệt · chứng từ chi ${r.paymentVoucherNumber} chờ ghi sổ`
  : r.status === 'Paid' && r.settlementPending ? `Đã chi · quyết toán ${r.settlementVoucherNumber} chờ ghi sổ` : STATUS_VI[r.status]

/** Tạm ứng và hoàn ứng: đề nghị → duyệt (người khác) → chứng từ chi nháp → quyết toán (bảng kê chi, hoàn lại hoặc chi bổ sung) → tuổi nợ theo hạn quyết toán. */
export function AdvancesView() {
  const { accounts, partners } = useCatalog()
  const [tab, setTab] = useState<'list' | 'aging'>('list')
  const [rows, setRows] = useState<Row[]>([])
  const [aging, setAging] = useState<Aging[]>([])
  const [asOf, setAsOf] = useState(today())
  const [f, pf, cf] = useFilterState('advances')
  const [form, setForm] = useState<{ employeeId: string; purpose: string; amount: string; date: string; due: string } | null>(null)
  const [pay, setPay] = useState<{ row: Row; cash: string; date: string } | null>(null)
  const [settle, setSettle] = useState<{ row: Row; date: string; cash: string; lines: SettleLine[] } | null>(null)
  const [view, setView] = useState<View | null>(null)
  const [info, setInfo] = useState('')
  const { busy, error, run, setError } = useAction()
  const employees = partners.filter(p => p.isEmployee)
  const cashAccounts = accounts.filter(a => a.cashKind !== 'None')

  const load = useCallback(() => run(async () => {
    setRows(await get<Row[]>(`/advances?asOf=${asOf}`))
    setAging(await get<Aging[]>(`/advances/aging?asOf=${asOf}`))
  }), [run, asOf])
  useEffect(() => { load() }, [load])
  const act = (msg: string, fn: () => Promise<unknown>) => run(async () => { await fn(); setInfo(msg); await load() })
  const ask = (q: string) => { const v = prompt(q); return v && v.trim() ? v.trim() : null }

  const shown = useMemo(() => rows.filter(r => (!f.chip || (f.chip === 'overdue' ? r.overdueDays > 0 : r.status === f.chip))
    && matchesQuery(`${r.number} ${r.employeeCode} ${r.employeeName} ${r.purpose}`, Number(r.amount), f.q)), [rows, f])
  const chip = (key: string, label: string, n: number) => ({ key, label, count: n })

  const create = () => run(async () => {
    if (!form) return
    const amount = parseMoney(form.amount)
    if (!form.employeeId || !amount) { setError(new Error('Chọn người nhận và nhập số tiền.')); return }
    await post('/advances', { employeeId: form.employeeId, purpose: form.purpose, amount, requestDate: form.date, dueDate: form.due })
    setForm(null); setInfo('Đã lập đề nghị tạm ứng.'); await load()
  })
  const startSettle = (r: Row) => setSettle({ row: r, date: today(), cash: '', lines: [{ accountId: '', amount: '', description: '', documentNo: '', documentDate: '' }] })
  const spent = settle ? addMoney(settle.lines.map(l => parseMoney(l.amount) ?? '0')) : '0'
  const diff = settle ? addMoney([settle.row.amount, '-' + spent]) : '0'
  const doSettle = () => run(async () => {
    if (!settle) return
    const lines = settle.lines.filter(l => l.accountId || l.amount).map(l => ({ expenseAccountId: l.accountId, amount: parseMoney(l.amount), description: l.description || null, documentNo: l.documentNo || null, documentDate: l.documentDate || null }))
    if (lines.some(l => !l.expenseAccountId || !l.amount)) { setError(new Error('Mỗi khoản chi cần tài khoản chi phí và số tiền hợp lệ.')); return }
    const v = await post<{ number: string }>(`/advances/${settle.row.id}/settle`, { date: settle.date, lines, cashAccountId: settle.cash || null })
    setSettle(null); setInfo(`Đã lập chứng từ quyết toán ${v.number} (nháp) — duyệt và ghi sổ ở màn Chứng từ.`); await load()
  })

  return (
    <>
      <div className="toolbar"><h2>Tạm ứng và hoàn ứng</h2>
        <button className={tab === 'list' ? 'primary' : ''} onClick={() => setTab('list')}>Đề nghị tạm ứng</button>
        <button className={tab === 'aging' ? 'primary' : ''} onClick={() => setTab('aging')}>Tuổi nợ tạm ứng</button>
        <label>Tính đến ngày<input type="date" value={asOf} onChange={e => setAsOf(e.target.value)} /></label>
        <button onClick={() => load()} disabled={busy}>Tải lại</button>
        <ExportButton fileName={`Tam_ung_${asOf}`} sheet="Tạm ứng" disabled={shown.length === 0}
          columns={[{ header: 'Số', width: 16 }, { header: 'Nhân viên', width: 24 }, { header: 'Lý do', width: 36 }, { header: 'Số tiền', numeric: true, width: 16 }, { header: 'Ngày đề nghị', width: 14 }, { header: 'Hạn quyết toán', width: 14 }, { header: 'Trạng thái', width: 26 }, { header: 'Còn nợ', numeric: true, width: 16 }, { header: 'Quá hạn (ngày)', numeric: true, width: 12 }]}
          rows={shown.map(r => [r.number, `${r.employeeCode} – ${r.employeeName}`, r.purpose, r.amount, r.requestDate, r.dueDate, stateText(r), r.outstanding, String(r.overdueDays)])} />
        <button className="primary" onClick={() => setForm({ employeeId: '', purpose: '', amount: '', date: today(), due: today() })}>Lập đề nghị tạm ứng</button>
      </div>
      <FlowStrip steps={[
        { label: 'Đề nghị tạm ứng (người lập)', journal: false, count: `${rows.filter(r => r.status === 'Requested').length} chờ duyệt` },
        { label: 'Duyệt (người khác) → chứng từ chi nháp', journal: false, count: `${rows.filter(r => r.status === 'Approved').length} chờ chi` },
        { label: 'Chi tạm ứng (Nhật ký: Nợ TK tạm ứng / Có tiền)', journal: true, screen: 'vouchers', count: `${rows.filter(r => r.status === 'Paid').length} đang giữ tiền` },
        { label: 'Quyết toán, hoàn ứng (Nhật ký: chi phí / TK tạm ứng)', journal: true, screen: 'vouchers', count: `${rows.filter(r => r.status === 'Settled').length} đã quyết toán` },
        { label: 'Tuổi nợ theo hạn quyết toán', journal: false, count: `${rows.filter(r => r.overdueDays > 0).length} khoản quá hạn` },
      ]} />
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}

      {tab === 'list' && <>
        <SmartFilter label="Tạm ứng" state={f} patch={pf} clear={cf} shown={shown.length} total={rows.length} placeholder="Tìm số, nhân viên, lý do, số tiền (>5tr)"
          chips={[chip('Requested', 'Chờ duyệt', rows.filter(r => r.status === 'Requested').length), chip('Approved', 'Chờ chi', rows.filter(r => r.status === 'Approved').length), chip('Paid', 'Đang giữ tiền', rows.filter(r => r.status === 'Paid').length),
            chip('overdue', 'Quá hạn quyết toán', rows.filter(r => r.overdueDays > 0).length), chip('Settled', 'Đã quyết toán', rows.filter(r => r.status === 'Settled').length)]} />
        <div className="scroll"><table className="grid">
          <thead><tr><th>Số</th><th>Nhân viên</th><th>Lý do</th><th className="num">Số tiền</th><th>Hạn quyết toán</th><th>Trạng thái</th><th className="num">Còn nợ</th><th /></tr></thead>
          <tbody>{shown.map(r => <tr key={r.id}>
            <td>{r.number}<div className="muted">{formatDate(r.requestDate)}</div></td><td>{r.employeeCode} – {r.employeeName}</td><td>{r.purpose}</td>
            <td className="num">{formatMoney(r.amount)}</td><td>{formatDate(r.dueDate)}{r.overdueDays > 0 && <div className="msg error" style={{ padding: '0 6px', display: 'inline-block' }}>quá {r.overdueDays} ngày</div>}</td>
            <td>{stateText(r)}</td><td className="num">{r.outstanding === '0' ? '' : formatMoney(r.outstanding)}</td>
            <td style={{ whiteSpace: 'nowrap' }}>
              {r.status === 'Requested' && <><button className="primary" disabled={busy} onClick={() => act('Đã duyệt.', () => post(`/advances/${r.id}/decide`, { approve: true }))}>Duyệt</button>
                <button disabled={busy} onClick={() => { const n = ask('Lý do từ chối:'); if (n) act('Đã từ chối.', () => post(`/advances/${r.id}/decide`, { approve: false, note: n })) }}>Từ chối</button></>}
              {r.status === 'Approved' && !r.paymentPending && <button className="primary" disabled={busy} onClick={() => setPay({ row: r, cash: '', date: today() })}>Lập chứng từ chi</button>}
              {r.status === 'Paid' && !r.settlementPending && <button className="primary" disabled={busy} onClick={() => startSettle(r)}>Quyết toán</button>}
              {(r.status === 'Requested' || (r.status === 'Approved' && !r.paymentPending)) && <button disabled={busy} onClick={() => { const n = ask('Lý do hủy:'); if (n) act('Đã hủy đề nghị.', () => post(`/advances/${r.id}/cancel`, { reason: n })) }}>Hủy</button>}
              <button onClick={() => run(async () => setView(await get<View>(`/advances/${r.id}`)))}>Xem</button>
            </td></tr>)}
            {shown.length === 0 && <tr><td colSpan={8} className="muted">Không có đề nghị tạm ứng.</td></tr>}</tbody>
        </table></div>
      </>}

      {tab === 'aging' && <div className="scroll"><table className="grid">
        <thead><tr><th>Nhân viên</th><th className="num">Chưa đến hạn</th><th className="num">Quá 1–30 ngày</th><th className="num">Quá 31–60 ngày</th><th className="num">Quá 60 ngày</th><th className="num">Tổng còn nợ</th></tr></thead>
        <tbody>{aging.map(a => <tr key={a.employeeId}><td>{a.employeeCode} – {a.employeeName}</td>{[a.notDue, a.over1To30, a.over31To60, a.over60, a.total].map((v, i) => <td key={i} className="num">{v === '0' ? '' : formatMoney(v)}</td>)}</tr>)}
          {aging.length === 0 && <tr><td colSpan={6} className="muted">Không có khoản tạm ứng đang giữ.</td></tr>}</tbody>
        <tfoot>{aging.length > 0 && <tr><td><b>Cộng</b></td>{(['notDue', 'over1To30', 'over31To60', 'over60', 'total'] as const).map(k => <td key={k} className="num"><b>{formatMoney(addMoney(aging.map(a => a[k])))}</b></td>)}</tr>}</tfoot>
      </table></div>}

      {form && <Modal title="Đề nghị tạm ứng" onClose={() => setForm(null)} busy={busy} onSubmit={create}
        footer={<><button onClick={() => setForm(null)}>Đóng</button><button className="primary" disabled={busy} onClick={create}>Lập đề nghị</button></>}>
        <div className="form-row">
          <label>Người nhận (nhân viên)<PartnerSelect partners={employees} value={form.employeeId} onChange={id => setForm({ ...form, employeeId: id })} /></label>
          <label>Số tiền<input className="num" value={form.amount} onChange={e => setForm({ ...form, amount: e.target.value })} /></label>
          <label>Ngày đề nghị<input type="date" value={form.date} onChange={e => setForm({ ...form, date: e.target.value, due: form.due < e.target.value ? e.target.value : form.due })} /></label>
          <label>Hạn quyết toán<input type="date" value={form.due} min={form.date} onChange={e => setForm({ ...form, due: e.target.value })} /></label>
        </div>
        <label style={{ display: 'block' }}>Lý do tạm ứng<input style={{ width: '100%' }} value={form.purpose} onChange={e => setForm({ ...form, purpose: e.target.value })} /></label>
        {employees.length === 0 && <p className="muted">Chưa có đối tượng nào có vai trò Nhân viên (thêm ở Danh mục đối tượng).</p>}
      </Modal>}

      {pay && <Modal title={`Chi tạm ứng ${pay.row.number}`} onClose={() => setPay(null)} busy={busy}
        footer={<><button onClick={() => setPay(null)}>Đóng</button><button className="primary" disabled={busy || !pay.cash} onClick={() => run(async () => {
          const v = await post<{ number: string }>(`/advances/${pay.row.id}/payment-voucher`, { cashAccountId: pay.cash, date: pay.date })
          setPay(null); setInfo(`Đã lập chứng từ chi ${v.number} (nháp) — gửi duyệt và ghi sổ ở màn Chứng từ; ghi sổ xong khoản tạm ứng chuyển sang Đã chi.`); await load()
        })}>Lập chứng từ chi</button></>}>
        <p>{pay.row.employeeName} nhận <b>{formatMoney(pay.row.amount)}</b> — Nợ TK tạm ứng (theo người) / Có tài khoản tiền.</p>
        <div className="form-row"><label>Tài khoản tiền chi<AccountSelect accounts={cashAccounts} value={pay.cash} onChange={id => setPay({ ...pay, cash: id })} /></label>
          <label>Ngày chi<input type="date" value={pay.date} onChange={e => setPay({ ...pay, date: e.target.value })} /></label></div>
      </Modal>}

      {settle && <Modal title={`Quyết toán ${settle.row.number} — ${settle.row.employeeName}`} onClose={() => setSettle(null)} busy={busy}
        footer={<><button onClick={() => setSettle(null)}>Đóng</button><button className="primary" disabled={busy} onClick={doSettle}>Lập chứng từ quyết toán</button></>}>
        <p>Đã tạm ứng <b>{formatMoney(settle.row.amount)}</b>. Liệt kê các khoản đã chi (kèm số hóa đơn, biên lai).</p>
        <div className="scroll"><table className="grid">
          <thead><tr><th>TK chi phí</th><th className="num">Số tiền</th><th>Nội dung</th><th>Số chứng từ</th><th>Ngày</th><th /></tr></thead>
          <tbody>{settle.lines.map((l, i) => {
            const set = (p: Partial<SettleLine>) => setSettle({ ...settle, lines: settle.lines.map((x, j) => (j === i ? { ...x, ...p } : x)) })
            return <tr key={i}>
              <td><AccountSelect accounts={accounts} filter={a => !a.requiresPartner && a.openItemNature === 'None' && a.cashKind === 'None'} value={l.accountId} onChange={id => set({ accountId: id })} ariaLabel="TK chi phí" /></td>
              <td><input className="num" aria-label="Số tiền" size={12} value={l.amount} onChange={e => set({ amount: e.target.value })} /></td>
              <td><input aria-label="Nội dung" value={l.description} onChange={e => set({ description: e.target.value })} /></td>
              <td><input aria-label="Số chứng từ" size={10} value={l.documentNo} onChange={e => set({ documentNo: e.target.value })} /></td>
              <td><input type="date" aria-label="Ngày chứng từ" value={l.documentDate} onChange={e => set({ documentDate: e.target.value })} /></td>
              <td><button aria-label="Xóa dòng" onClick={() => setSettle({ ...settle, lines: settle.lines.filter((_, j) => j !== i) })}>✕</button></td></tr>
          })}</tbody>
        </table></div>
        <button onClick={() => setSettle({ ...settle, lines: [...settle.lines, { accountId: '', amount: '', description: '', documentNo: '', documentDate: '' }] })}>+ Thêm khoản chi</button>
        <p>Chi thực tế: <b>{formatMoney(spent)}</b> · {diff === '0' ? 'Khớp số đã tạm ứng.' : diff.startsWith('-') ? <>Chi vượt <b>{formatMoney(diff.slice(1))}</b>: công ty chi bổ sung cho nhân viên.</> : <>Chưa chi hết <b>{formatMoney(diff)}</b>: nhân viên hoàn lại.</>}</p>
        {diff !== '0' && <label>Tài khoản tiền ({diff.startsWith('-') ? 'chi bổ sung' : 'nhận hoàn ứng'})<AccountSelect accounts={cashAccounts} value={settle.cash} onChange={id => setSettle({ ...settle, cash: id })} /></label>}
        <label>Ngày quyết toán<input type="date" value={settle.date} onChange={e => setSettle({ ...settle, date: e.target.value })} /></label>
      </Modal>}

      {view && <AdvanceDetail view={view} onClose={() => setView(null)} />}
    </>
  )
}

function AdvanceDetail({ view, onClose }: { view: View; onClose: () => void }) {
  const r = view.row
  const company = useCompanyName()
  const [printing, setPrinting] = useState(false)
  useEffect(() => { if (!printing) return; const t = setTimeout(() => { printSheet(); setPrinting(false) }, 150); return () => clearTimeout(t) }, [printing])
  return (
    <Modal title={`Tạm ứng ${r.number}`} onClose={onClose} footer={<><button onClick={() => setPrinting(true)}>In giấy đề nghị / bảng kê</button><button onClick={onClose}>Đóng</button></>}>
      <p><b>{r.employeeCode} – {r.employeeName}</b> · {formatMoney(r.amount)} · {stateText(r)}</p>
      <p>Lý do: {r.purpose} · Hạn quyết toán {formatDate(r.dueDate)}{view.decisionNote ? ` · Ghi chú duyệt: ${view.decisionNote}` : ''}</p>
      {r.paymentVoucherNumber && <p>Chứng từ chi: {r.paymentVoucherNumber}{r.settlementVoucherNumber ? ` · Chứng từ quyết toán: ${r.settlementVoucherNumber}` : ''}</p>}
      {view.lines.length > 0 && <table className="grid"><thead><tr><th>TK</th><th>Nội dung</th><th>Chứng từ</th><th className="num">Số tiền</th></tr></thead>
        <tbody>{view.lines.map(l => <tr key={l.lineNo}><td>{l.expenseAccountCode}</td><td>{l.description}</td><td>{l.documentNo}{l.documentDate ? ` · ${formatDate(l.documentDate)}` : ''}</td><td className="num">{formatMoney(l.amount)}</td></tr>)}</tbody></table>}
      {printing && <PrintSheet>
        <div><b>{company}</b></div>
        <h1>{view.lines.length ? 'GIẤY THANH TOÁN TẠM ỨNG' : 'GIẤY ĐỀ NGHỊ TẠM ỨNG'}</h1>
        <div className="center">Số: <b>{r.number}</b> · Ngày {formatDate(r.requestDate)}</div>
        <p>Họ tên người nhận: <b>{r.employeeName}</b> ({r.employeeCode})</p>
        <p>Lý do: {r.purpose}</p>
        <p>Số tiền tạm ứng: <b>{formatMoney(r.amount)}</b> đồng — Bằng chữ: <i>{amountInWords(r.amount)}</i></p>
        <p>Thời hạn thanh toán: {formatDate(r.dueDate)}</p>
        {view.lines.length > 0 && <table><thead><tr><th>STT</th><th>Nội dung</th><th>Chứng từ</th><th>Số tiền</th></tr></thead>
          <tbody>{view.lines.map(l => <tr key={l.lineNo}><td className="center">{l.lineNo}</td><td>{l.description}</td><td>{l.documentNo}{l.documentDate ? ` ${formatDate(l.documentDate)}` : ''}</td><td className="num">{formatMoney(l.amount)}</td></tr>)}</tbody>
          <tfoot><tr><td colSpan={3}><b>Cộng chi thực tế</b></td><td className="num"><b>{formatMoney(r.spentAmount ?? '0')}</b></td></tr></tfoot></table>}
        <div className="signs"><div><b>Người đề nghị</b><br /><i>(Ký, họ tên)</i></div><div><b>Kế toán trưởng</b><br /><i>(Ký, họ tên)</i></div><div><b>Giám đốc</b><br /><i>(Ký, họ tên, đóng dấu)</i></div></div>
        <div className="foot">Mẫu in nội bộ HAK Accounting — chưa đối chiếu với mẫu giấy đề nghị tạm ứng / thanh toán tạm ứng theo chế độ kế toán [CẦN XÁC MINH]. In lúc {new Date().toLocaleString('vi-VN')}.</div>
      </PrintSheet>}
    </Modal>
  )
}

// ====================================================================== kiểm kê quỹ

interface CRow { id: string; number: string; cashAccountId: string; cashAccountCode: string; countDate: string; status: 'Draft' | 'Confirmed' | 'Cancelled'; countedTotal: string; bookBalance: string | null; difference: string | null; adjustmentVoucherNumber: string | null; adjustmentPending: boolean; note: string | null }
interface CView { row: CRow; participants: string | null; cancelReason: string | null; lines: { denomination: string; quantity: number; amount: string }[]; createdByName: string | null; confirmedByName: string | null }
const DENOMS = ['500000', '200000', '100000', '50000', '20000', '10000', '5000', '2000', '1000', '500']
const CSTATUS_VI = { Draft: 'Nháp (chờ xác nhận)', Confirmed: 'Đã xác nhận', Cancelled: 'Đã hủy' } as const
const mul = (denom: string, qty: string) => String(BigInt(denom) * BigInt(/^\d+$/.test(qty) ? qty : '0'))

/** Kiểm kê quỹ tiền mặt: đếm theo mệnh giá, xác nhận (người khác) chốt số dư sổ quỹ và chênh lệch, lập chứng từ xử lý chênh lệch, in biên bản. */
export function CashCountView() {
  const { accounts } = useCatalog()
  const cash = accounts.filter(a => a.cashKind === 'Cash')
  const [rows, setRows] = useState<CRow[]>([])
  const [f, pf, cf] = useFilterState('cashcount')
  const [form, setForm] = useState<{ cash: string; date: string; qty: Record<string, string>; note: string; people: string } | null>(null)
  const [view, setView] = useState<CView | null>(null)
  const [adjust, setAdjust] = useState<{ row: CRow; date: string } | null>(null)
  const [info, setInfo] = useState('')
  const { busy, error, run, setError } = useAction()
  const load = useCallback(() => run(async () => setRows(await get<CRow[]>(`/cash-counts?from=${f.from}&to=${f.to}`))), [run, f.from, f.to])
  useEffect(() => { load() }, [load])
  const act = (msg: string, fn: () => Promise<unknown>) => run(async () => { await fn(); setInfo(msg); await load(); setView(null) })
  const shown = rows.filter(r => (!f.chip || (f.chip === 'diff' ? !!r.difference && r.difference !== '0' : r.status === f.chip)) && matchesQuery(`${r.number} ${r.cashAccountCode} ${r.note ?? ''}`, null, f.q))
  const total = form ? addMoney(DENOMS.map(d => mul(d, form.qty[d] ?? ''))) : '0'
  const save = () => run(async () => {
    if (!form) return
    const c = form.cash || cash[0]?.id
    if (!c) { setError(new Error('Chọn tài khoản quỹ.')); return }
    const lines = DENOMS.filter(d => /^\d+$/.test(form.qty[d] ?? '') && Number(form.qty[d]) > 0).map(d => ({ denomination: d, quantity: Number(form.qty[d]) }))
    if (lines.length === 0) { setError(new Error('Nhập số tờ ít nhất một mệnh giá.')); return }
    await post('/cash-counts', { cashAccountId: c, countDate: form.date, lines, note: form.note || null, participants: form.people || null })
    setForm(null); setInfo('Đã lập biên bản kiểm kê quỹ (nháp) — người khác xác nhận để chốt số dư sổ quỹ.'); await load()
  })

  return (
    <>
      <div className="toolbar"><h2>Kiểm kê quỹ tiền mặt</h2>
        <button onClick={() => load()} disabled={busy}>Tải lại</button>
        <button className="primary" onClick={() => setForm({ cash: '', date: today(), qty: {}, note: '', people: '' })}>Lập biên bản kiểm kê</button></div>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      <SmartFilter label="Kiểm kê quỹ" state={f} patch={pf} clear={cf} dateLabel="ngày kiểm kê" shown={shown.length} total={rows.length} placeholder="Tìm số biên bản, tài khoản, ghi chú"
        chips={[{ key: 'Draft', label: 'Chờ xác nhận', count: rows.filter(r => r.status === 'Draft').length }, { key: 'Confirmed', label: 'Đã xác nhận', count: rows.filter(r => r.status === 'Confirmed').length },
          { key: 'diff', label: 'Có chênh lệch', count: rows.filter(r => r.difference && r.difference !== '0').length }]} />
      <div className="scroll"><table className="grid">
        <thead><tr><th>Số</th><th>Ngày</th><th>Quỹ</th><th className="num">Kiểm đếm</th><th className="num">Sổ quỹ</th><th className="num">Chênh lệch</th><th>Trạng thái</th><th /></tr></thead>
        <tbody>{shown.map(r => <tr key={r.id}><td>{r.number}</td><td>{formatDate(r.countDate)}</td><td>{r.cashAccountCode}</td><td className="num">{formatMoney(r.countedTotal)}</td>
          <td className="num">{r.bookBalance == null ? '' : formatMoney(r.bookBalance)}</td>
          <td className="num" style={{ color: r.difference && r.difference !== '0' ? 'var(--danger, #c0392b)' : undefined }}>{r.difference == null ? '' : r.difference === '0' ? 'Khớp' : `${r.difference.startsWith('-') ? 'Thiếu ' : 'Thừa '}${formatMoney(r.difference.replace('-', ''))}`}</td>
          <td>{CSTATUS_VI[r.status]}{r.adjustmentVoucherNumber ? ` · xử lý ${r.adjustmentVoucherNumber}${r.adjustmentPending ? ' (chờ ghi sổ)' : ''}` : ''}</td>
          <td style={{ whiteSpace: 'nowrap' }}>
            {r.status === 'Draft' && <button className="primary" disabled={busy} onClick={() => act('Đã xác nhận biên bản; số dư sổ quỹ đã chốt.', () => post(`/cash-counts/${r.id}/confirm`))}>Xác nhận</button>}
            {r.status === 'Confirmed' && r.difference && r.difference !== '0' && !r.adjustmentVoucherNumber && <button disabled={busy} onClick={() => setAdjust({ row: r, date: r.countDate })}>Xử lý chênh lệch</button>}
            {r.status !== 'Cancelled' && !r.adjustmentVoucherNumber && <button disabled={busy} onClick={() => { const n = prompt('Lý do hủy biên bản:'); if (n?.trim()) act('Đã hủy biên bản.', () => post(`/cash-counts/${r.id}/cancel`, { reason: n.trim() })) }}>Hủy</button>}
            <button onClick={() => run(async () => setView(await get<CView>(`/cash-counts/${r.id}`)))}>Xem / In</button></td></tr>)}
          {shown.length === 0 && <tr><td colSpan={8} className="muted">Chưa có biên bản kiểm kê quỹ trong khoảng ngày.</td></tr>}</tbody>
      </table></div>

      {form && <Modal title="Biên bản kiểm kê quỹ" onClose={() => setForm(null)} busy={busy}
        footer={<><button onClick={() => setForm(null)}>Đóng</button><button className="primary" disabled={busy} onClick={save}>Lưu biên bản</button></>}>
        <div className="form-row"><label>Quỹ<AccountSelect accounts={cash} value={form.cash || cash[0]?.id || ''} onChange={id => setForm({ ...form, cash: id })} /></label>
          <label>Ngày kiểm kê<input type="date" value={form.date} onChange={e => setForm({ ...form, date: e.target.value })} /></label></div>
        <table className="grid"><thead><tr><th className="num">Mệnh giá</th><th className="num">Số tờ</th><th className="num">Thành tiền</th></tr></thead>
          <tbody>{DENOMS.map(d => <tr key={d}><td className="num">{formatMoney(d)}</td>
            <td className="num"><input className="num" aria-label={`Số tờ ${formatMoney(d)}`} inputMode="numeric" size={6} value={form.qty[d] ?? ''} onChange={e => setForm({ ...form, qty: { ...form.qty, [d]: e.target.value.replace(/\D/g, '') } })} /></td>
            <td className="num">{formatMoney(mul(d, form.qty[d] ?? ''))}</td></tr>)}</tbody>
          <tfoot><tr><td colSpan={2}><b>Tổng kiểm đếm</b></td><td className="num"><b>{formatMoney(total)}</b></td></tr></tfoot></table>
        <label style={{ display: 'block' }}>Thành phần kiểm kê<input style={{ width: '100%' }} value={form.people} placeholder="Thủ quỹ, kế toán trưởng, …" onChange={e => setForm({ ...form, people: e.target.value })} /></label>
        <label style={{ display: 'block' }}>Ghi chú<input style={{ width: '100%' }} value={form.note} onChange={e => setForm({ ...form, note: e.target.value })} /></label>
      </Modal>}

      {adjust && <Modal title={`Xử lý chênh lệch ${adjust.row.number}`} onClose={() => setAdjust(null)} busy={busy}
        footer={<><button onClick={() => setAdjust(null)}>Đóng</button><button className="primary" disabled={busy} onClick={() => run(async () => {
          const v = await post<{ number: string }>(`/cash-counts/${adjust.row.id}/adjust`, { date: adjust.date })
          setAdjust(null); setInfo(`Đã lập chứng từ xử lý chênh lệch ${v.number} (nháp) — duyệt và ghi sổ ở màn Chứng từ.`); await load()
        })}>Lập chứng từ</button></>}>
        <p>Chênh lệch: <b>{adjust.row.difference?.startsWith('-') ? 'thiếu' : 'thừa'} {formatMoney((adjust.row.difference ?? '0').replace('-', ''))}</b>. Hạch toán qua vai trò tài khoản CASH_SHORTAGE (thiếu) / CASH_SURPLUS (thừa) — cần ánh xạ ở Thiết lập › Vai trò tài khoản.</p>
        <label>Ngày hạch toán<input type="date" value={adjust.date} onChange={e => setAdjust({ ...adjust, date: e.target.value })} /></label>
      </Modal>}

      {view && <CashCountDetail view={view} onClose={() => setView(null)} />}
    </>
  )
}

function CashCountDetail({ view, onClose }: { view: CView; onClose: () => void }) {
  const r = view.row
  const company = useCompanyName()
  const [printing, setPrinting] = useState(false)
  useEffect(() => { if (!printing) return; const t = setTimeout(() => { printSheet(); setPrinting(false) }, 150); return () => clearTimeout(t) }, [printing])
  const diff = r.difference
  return (
    <Modal title={`Biên bản ${r.number}`} onClose={onClose} footer={<><button onClick={() => setPrinting(true)}>In biên bản</button><button onClick={onClose}>Đóng</button></>}>
      <p>Quỹ <b>{r.cashAccountCode}</b> ngày {formatDate(r.countDate)} · {CSTATUS_VI[r.status]}{view.cancelReason ? ` · Lý do hủy: ${view.cancelReason}` : ''}</p>
      <table className="grid"><thead><tr><th className="num">Mệnh giá</th><th className="num">Số tờ</th><th className="num">Thành tiền</th></tr></thead>
        <tbody>{view.lines.map(l => <tr key={l.denomination}><td className="num">{formatMoney(l.denomination)}</td><td className="num">{l.quantity}</td><td className="num">{formatMoney(l.amount)}</td></tr>)}</tbody>
        <tfoot><tr><td colSpan={2}><b>Tổng kiểm đếm</b></td><td className="num"><b>{formatMoney(r.countedTotal)}</b></td></tr></tfoot></table>
      {r.bookBalance != null && <p>Số dư sổ quỹ: <b>{formatMoney(r.bookBalance)}</b> · Chênh lệch: <b>{diff === '0' ? 'khớp' : `${diff?.startsWith('-') ? 'thiếu' : 'thừa'} ${formatMoney((diff ?? '0').replace('-', ''))}`}</b></p>}
      {printing && <PrintSheet>
        <div><b>{company}</b></div>
        <h1>BIÊN BẢN KIỂM KÊ QUỸ</h1>
        <div className="center">Số: <b>{r.number}</b> · Thời điểm kiểm kê: ngày {formatDate(r.countDate)}</div>
        {r.status !== 'Confirmed' && <div className="center"><span className="stamp">{CSTATUS_VI[r.status].toUpperCase()}</span></div>}
        <p>Quỹ: {r.cashAccountCode}. Thành phần kiểm kê: {view.participants || '………………………………'}</p>
        <table><thead><tr><th>STT</th><th>Loại tiền (mệnh giá)</th><th>Số tờ</th><th>Thành tiền</th></tr></thead>
          <tbody>{view.lines.map((l, i) => <tr key={l.denomination}><td className="center">{i + 1}</td><td className="num">{formatMoney(l.denomination)}</td><td className="num">{l.quantity}</td><td className="num">{formatMoney(l.amount)}</td></tr>)}</tbody>
          <tfoot><tr><td colSpan={3}><b>Số tiền thực tế kiểm đếm</b></td><td className="num"><b>{formatMoney(r.countedTotal)}</b></td></tr>
            {r.bookBalance != null && <><tr><td colSpan={3}>Số dư theo sổ quỹ</td><td className="num">{formatMoney(r.bookBalance)}</td></tr>
              <tr><td colSpan={3}><b>Chênh lệch ({diff?.startsWith('-') ? 'thiếu' : 'thừa'} nếu có)</b></td><td className="num"><b>{formatMoney((diff ?? '0').replace('-', ''))}</b></td></tr></>}</tfoot></table>
        <p>Bằng chữ (số kiểm đếm): <i>{amountInWords(r.countedTotal)}</i></p>
        <p>Ý kiến giải quyết chênh lệch: {r.note || '…………………………………………………………………'}</p>
        <div className="signs"><div><b>Thủ quỹ</b><br /><i>(Ký, họ tên)</i>{view.createdByName && <div>{view.createdByName}</div>}</div><div><b>Kế toán</b><br /><i>(Ký, họ tên)</i>{view.confirmedByName && <div>{view.confirmedByName}</div>}</div><div><b>Giám đốc</b><br /><i>(Ký, họ tên, đóng dấu)</i></div></div>
        <div className="foot">Mẫu in nội bộ HAK Accounting — chưa đối chiếu với mẫu 08a-TT theo chế độ kế toán [CẦN XÁC MINH]. In lúc {new Date().toLocaleString('vi-VN')}.</div>
      </PrintSheet>}
    </Modal>
  )
}
