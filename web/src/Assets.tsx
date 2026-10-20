import { useCallback, useEffect, useState } from 'react'
import { get, post, put, type Period } from './api'
import { AccountSelect, ErrorBox, ExportButton, useAction, useCatalog } from './common'
import { QrLabelsButton } from './AssetQr'
import { AssetCyclePanels } from './AssetCycle'
import { AssetJournal } from './AssetJournal'
import { ExpenseAllocationReport, ExpenseSplitEditor } from './AssetExpenseSplit'
import { AssetUsagePanel } from './AssetUsage'
import { SmartFilter, useFilterState } from './SmartFilter'
import { matchesQuery, toNumber } from './filter'
import { addMoney, formatDate, formatMoney, formatMonth, parseMoney, today } from './money'

interface RegisterRow {
  method?: 'StraightLine' | 'TimeBased' | 'UnitsOfProduction' | 'DecliningBalance'; frequency?: 'Monthly' | 'Quarterly' | 'Yearly'; totalUnits?: string | null; unitName?: string | null; scheduleEndDate?: string | null
  id: string; code: string; name: string; class: 'FixedAsset' | 'Tool'; status: 'Draft' | 'Active' | 'Disposed' | 'Cancelled'
  assetAccountCode: string; inServiceDate: string; scheduleStartDate: string; usefulLifeMonths: number; originalCost: string; accumulated: string; netBookValue: string; disposalDate: string | null
}
interface Candidate { journalLineId: string; voucherNumber: string; postingDate: string; accountCode: string; description: string | null; lineAmount: string; assigned: string }
interface Run { id: string; periodStart: string; periodEnd: string; status: 'Active' | 'Reversed'; voucherId: string | null }
interface ReconRow { accountId: string; accountCode: string; kind: string; registerBalance: string; ledgerBalance: string; difference: string }
interface Detail {
  asset: { id: string; code: string; name: string; status?: string; method?: string; proration: string; allocationFrequency: string; scheduleStartDate: string; openingAccumulated: string; remainingLifeMonths: number | null }
  sources: { journalLineId: string; voucherNumber: string; postingDate: string; accountCode: string; lineAmount: string; amount: string }[]
  schedule: { periodEnd: string; planned: string; cumulative: string; booked: string | null }[]
}

const CLASS_VI = { FixedAsset: 'TSCĐ', Tool: 'CCDC' } as const
const METHOD_VI: Record<string, string> = { StraightLine: 'Đường thẳng', TimeBased: 'Theo thời gian (số ngày)', UnitsOfProduction: 'Theo sản lượng', DecliningBalance: 'Số dư giảm dần' }
const FREQ_VI: Record<string, string> = { Monthly: 'hàng tháng', Quarterly: 'hàng quý', Yearly: 'hàng năm' }
const STATUS_VI = { Draft: 'Nháp', Active: 'Đang dùng', Disposed: 'Đã ghi giảm', Cancelled: 'Đã hủy' } as const

/** Amount of a candidate line not yet assigned (exact string arithmetic). */
const remaining = (c: Candidate) => addMoney([c.lineAmount, '-' + c.assigned])

export function AssetsView() {
  const { accounts } = useCatalog()
  const [asOf, setAsOf] = useState(today())
  const [rows, setRows] = useState<RegisterRow[]>([])
  const [runs, setRuns] = useState<Run[]>([])
  const [periods, setPeriods] = useState<Period[]>([])
  const [recon, setRecon] = useState<ReconRow[] | null>(null)
  const [detail, setDetail] = useState<Detail | null>(null)
  const [f, pf, cf] = useFilterState('assets')
  const [journalId, setJournalId] = useState<string | null>(null)
  const [splitFor, setSplitFor] = useState<RegisterRow | null>(null)
  const [creating, setCreating] = useState(false)
  const [disposing, setDisposing] = useState<RegisterRow | null>(null)
  const [info, setInfo] = useState('')
  const { busy, error, run } = useAction()

  const load = useCallback(() => run(async () => {
    setRows(await get<RegisterRow[]>(`/assets?asOf=${asOf}`))
    setRuns(await get<Run[]>('/assets/depreciation-runs'))
    setPeriods(await get<Period[]>('/periods'))
  }), [run, asOf])
  useEffect(() => { load() }, [load])

  const act = (fn: () => Promise<unknown>, msg: string) => run(async () => { await fn(); setInfo(msg); setDetail(null); await load() })
  const assetChips = { active: (r: RegisterRow) => r.status === 'Active', fullyDepreciated: (r: RegisterRow) => r.status === 'Active' && toNumber(r.netBookValue) === 0, disposed: (r: RegisterRow) => r.status === 'Disposed', draft: (r: RegisterRow) => r.status === 'Draft' }
  const shown = rows.filter(r => (!f.sel.class || r.class === f.sel.class) && (!f.sel.status || r.status === f.sel.status) && (!f.chip || assetChips[f.chip as keyof typeof assetChips]?.(r))
    && matchesQuery(`${r.code} ${r.name} ${r.assetAccountCode} ${r.inServiceDate}`, toNumber(r.originalCost), f.q))
  const lastActive = runs.find(r => r.status === 'Active')
  // Next period after the last run; before the first run, the period of the earliest depreciation start (runs go in period order from there).
  const earliest = rows.filter(r => r.status === 'Active').map(r => r.scheduleStartDate).sort()[0]
  const nextPeriod = lastActive
    ? periods.find(p => p.status === 'Open' && p.startDate > lastActive.periodEnd)
    : periods.find(p => p.status === 'Open' && earliest !== undefined && p.startDate <= earliest && p.endDate >= earliest)
  const [periodId, setPeriodId] = useState('')

  return (
    <>
      <div className="toolbar">
        <h2>Tài sản cố định & công cụ dụng cụ</h2>
        <label>Tại ngày <input type="date" value={asOf} onChange={e => setAsOf(e.target.value)} /></label>
        <button onClick={() => load()}>Tải lại</button>
        <button className="primary" onClick={() => { setCreating(true); setDetail(null) }}>Thêm thẻ tài sản</button>
        <QrLabelsButton items={shown.filter(r => r.status === 'Active').map(r => ({ code: r.code, name: r.name }))} />
        <ExportButton fileName={'So_TSCD_' + asOf} sheet="Sổ tài sản" columns={[{ header: 'Mã' }, { header: 'Tên', width: 30 }, { header: 'Loại' }, { header: 'Trạng thái' }, { header: 'TK' }, { header: 'Ngày sử dụng' },
          { header: 'Số tháng', numeric: true }, { header: 'Nguyên giá', numeric: true }, { header: 'Hao mòn / đã phân bổ', numeric: true }, { header: 'Còn lại', numeric: true }, { header: 'Ngày ghi giảm' }]}
          rows={rows.map(r => [r.code, r.name, CLASS_VI[r.class], STATUS_VI[r.status], r.assetAccountCode, r.inServiceDate, String(r.usefulLifeMonths), r.originalCost, r.accumulated, r.netBookValue, r.disposalDate])} />
      </div>
      <p className="muted">Nguyên giá lấy từ bút toán đã ghi sổ (hóa đơn mua, chứng từ, số dư đầu) nên sổ tài sản luôn khớp sổ cái. Phương pháp, kỳ ghi sổ và cách tính tháng đầu / cuối chọn theo từng thẻ (căn cứ: Thông tư 45/2013/TT-BTC, xem legal/KHAU-HAO).</p>
      <SmartFilter label="Sổ tài sản" state={f} patch={pf} clear={cf} shown={shown.length} total={rows.length} placeholder="Tìm mã, tên, TK, ngày; ví dụ: may nghien >100tr"
        selects={[{ key: 'class', label: 'Loại', options: [{ value: 'FixedAsset', label: 'TSCĐ' }, { value: 'Tool', label: 'CCDC' }] }, { key: 'status', label: 'Trạng thái', options: Object.entries(STATUS_VI).map(([value, label]) => ({ value, label })) }]}
        chips={[{ key: 'active', label: 'Đang dùng', count: rows.filter(assetChips.active).length }, { key: 'fullyDepreciated', label: 'Đã khấu hao hết', count: rows.filter(assetChips.fullyDepreciated).length },
          { key: 'draft', label: 'Chưa ghi tăng', count: rows.filter(assetChips.draft).length }, { key: 'disposed', label: 'Đã ghi giảm', count: rows.filter(assetChips.disposed).length }]} />
      <table className="grid">
        <thead><tr><th>Mã</th><th>Tên</th><th>Loại</th><th>Trạng thái</th><th>TK</th><th>Ngày sử dụng</th><th>Phương pháp, kỳ ghi sổ</th><th>Tháng</th>
          <th className="num">Nguyên giá</th><th className="num">Hao mòn / đã phân bổ</th><th className="num">Còn lại</th><th>Thao tác</th></tr></thead>
        <tbody>{shown.map(r => (
          <tr key={r.id}>
            <td>{r.code}</td><td>{r.name}</td><td>{CLASS_VI[r.class]}</td>
            <td><span className={`badge ${r.status}`}>{STATUS_VI[r.status]}{r.disposalDate ? ` ${formatDate(r.disposalDate)}` : ''}</span></td>
            <td>{r.assetAccountCode}</td><td>{formatDate(r.inServiceDate)}</td><td title={r.method === 'UnitsOfProduction' && r.totalUnits ? `Tổng sản lượng dự kiến ${formatMoney(r.totalUnits)} ${r.unitName ?? ''}` : r.method === 'TimeBased' && r.scheduleEndDate ? `Phân bổ đến ${formatDate(r.scheduleEndDate)}` : undefined}>{METHOD_VI[r.method ?? 'StraightLine']} · {FREQ_VI[r.frequency ?? 'Monthly']}</td><td>{r.method === 'UnitsOfProduction' ? '—' : r.usefulLifeMonths}</td>
            <td className="num">{formatMoney(r.originalCost)}</td><td className="num">{formatMoney(r.accumulated)}</td><td className="num">{formatMoney(r.netBookValue)}</td>
            <td style={{ display: 'flex', gap: 6 }}>
              <button onClick={() => run(async () => setDetail(await get<Detail>(`/assets/${r.id}`)))}>Chi tiết</button>
              <button onClick={() => { setJournalId(r.id); setDetail(null) }}>Nhật ký</button>
              {r.status === 'Draft' && <>
                <button disabled={busy} onClick={() => act(() => post(`/assets/${r.id}/activate`), `Đã ghi tăng ${r.code}.`)}>Ghi tăng</button>
                <button disabled={busy} onClick={() => { const reason = prompt('Lý do hủy thẻ:'); if (reason) act(() => post(`/assets/${r.id}/cancel`, { reason }), `Đã hủy thẻ ${r.code}.`) }}>Hủy</button>
              </>}
              {(r.status === 'Active' || r.status === 'Draft') && <button disabled={busy} onClick={() => { setSplitFor(r); setDetail(null); setJournalId(null) }}>Chia chi phí</button>}
              {r.status === 'Active' && <button disabled={busy} onClick={() => setDisposing(r)}>Ghi giảm</button>}
              {r.status === 'Disposed' && <button disabled={busy} onClick={() => {
                const reason = prompt('Lý do hủy ghi giảm:')
                if (reason) act(() => post(`/assets/${r.id}/dispose/reverse`, { reason }), `Đã hủy ghi giảm ${r.code}.`)
              }}>Hủy ghi giảm</button>}
            </td>
          </tr>))}
        </tbody>
      </table>

      <AssetCyclePanels assets={rows} depreciationNote={lastActive ? `kỳ gần nhất đến ${formatDate(lastActive.periodEnd)}` : 'chưa chạy kỳ nào'} />
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}

      {creating && <NewAsset accounts={accounts} onDone={msg => { setCreating(false); if (msg) { setInfo(msg); load() } }} />}


      {disposing && <Dispose row={disposing} accounts={accounts} busy={busy}
        onSubmit={(date, residualAccountId, description) => act(async () => {
          await post(`/assets/${disposing.id}/dispose`, { date, residualAccountId, description }); setDisposing(null)
        }, `Đã ghi giảm ${disposing.code}.`)}
        onCancel={() => setDisposing(null)} />}

      {splitFor && <ExpenseSplitEditor key={splitFor.id} assetId={splitFor.id} assetLabel={`${splitFor.code} – ${splitFor.name}`} accounts={accounts} onClose={() => setSplitFor(null)} />}
      {journalId && <AssetJournal key={journalId} assetId={journalId} onClose={() => setJournalId(null)} />}

      {detail && (
        <section className="panel">
          <div className="toolbar"><h3>{detail.asset.code} – {detail.asset.name}</h3><button onClick={() => setDetail(null)}>Đóng</button></div>
          <p className="muted">Bắt đầu khấu hao {formatDate(detail.asset.scheduleStartDate)}; tính tháng đầu/cuối: {detail.asset.proration === 'Daily' ? 'theo ngày' : 'tròn tháng'}; ghi sổ {({ Monthly: 'hàng tháng', Quarterly: 'hàng quý', Yearly: 'hàng năm' } as Record<string, string>)[detail.asset.allocationFrequency] ?? 'hàng tháng'}
            {detail.asset.remainingLifeMonths ? `; chuyển từ hệ thống cũ: hao mòn đầu ${formatMoney(detail.asset.openingAccumulated)}, còn ${detail.asset.remainingLifeMonths} tháng` : ''}</p>
          {detail.asset.method !== 'UnitsOfProduction' && ['Active', 'Draft'].includes(detail.asset.status ?? '') && <div className="form-row">
            <label title="Áp dụng từ lần chạy sau; số phải ghi là lũy kế kế hoạch trừ số đã ghi nên không mất, không trùng">Kỳ ghi sổ khấu hao / phân bổ
              <select aria-label="Đổi kỳ ghi sổ" value={detail.asset.allocationFrequency} disabled={busy} onChange={e => act(() => put(`/assets/${detail.asset.id}/frequency`, { frequency: e.target.value }), `Đã đổi kỳ ghi sổ của ${detail.asset.code}; áp dụng từ lần chạy sau.`)}>
                <option value="Monthly">Hàng tháng</option><option value="Quarterly">Hàng quý</option><option value="Yearly">Hàng năm</option></select></label></div>}
          <h4>Nguồn nguyên giá</h4>
          <table className="grid"><thead><tr><th>Chứng từ</th><th>Ngày</th><th>TK</th><th className="num">Dòng bút toán</th><th className="num">Gán cho tài sản</th></tr></thead>
            <tbody>{detail.sources.map(x => <tr key={x.journalLineId}><td>{x.voucherNumber}</td><td>{formatDate(x.postingDate)}</td><td>{x.accountCode}</td>
              <td className="num">{formatMoney(x.lineAmount)}</td><td className="num">{formatMoney(x.amount)}</td></tr>)}</tbody></table>
          <h4>Kế hoạch khấu hao / phân bổ</h4>
          <table className="grid"><thead><tr><th>Tháng</th><th className="num">Kế hoạch</th><th className="num">Lũy kế</th><th className="num">Đã ghi sổ</th></tr></thead>
            <tbody>{detail.schedule.map(x => <tr key={x.periodEnd}><td>{formatMonth(x.periodEnd)}</td><td className="num">{formatMoney(x.planned)}</td>
              <td className="num">{formatMoney(x.cumulative)}</td><td className="num">{x.booked == null ? '' : formatMoney(x.booked)}</td></tr>)}</tbody></table>
        </section>
      )}

      <AssetUsagePanel assets={rows.filter(r => r.method === 'UnitsOfProduction' && r.status === 'Active').map(r => ({ id: r.id, code: r.code, name: r.name, unitName: r.unitName ?? null, totalUnits: r.totalUnits ?? null }))} periods={periods} />

      <ExpenseAllocationReport />

      <section className="panel">
        <h3>Chạy khấu hao / phân bổ theo kỳ</h3>
        <p className="muted">Chạy lần lượt từng kỳ; hủy từ kỳ gần nhất trở về trước. Thẻ ghi theo quý / năm chỉ phát sinh bút toán ở kỳ cuối quý / cuối năm tài chính (hoặc khi hết thời gian phân bổ). Bút toán sinh ra chỉ đảo được bằng chức năng hủy ở đây.</p>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          <select value={periodId || nextPeriod?.id || ''} onChange={e => setPeriodId(e.target.value)} aria-label="Kỳ khấu hao">
            <option value="">— chọn kỳ —</option>
            {periods.filter(p => p.status === 'Open').map(p => <option key={p.id} value={p.id}>{formatMonth(p.startDate)}</option>)}
          </select>
          <button className="primary" disabled={busy || !(periodId || nextPeriod)} onClick={() => {
            const id = periodId || nextPeriod!.id
            setPeriodId('')
            act(() => post(`/assets/depreciation-runs?periodId=${id}`), 'Đã chạy khấu hao và ghi sổ bút toán.')
          }}>Chạy khấu hao</button>
        </div>
        <table className="grid"><thead><tr><th>Kỳ</th><th>Trạng thái</th><th>Thao tác</th></tr></thead>
          <tbody>{runs.map(r => <tr key={r.id}><td>{formatMonth(r.periodStart)}</td><td>{r.status === 'Active' ? 'Hiệu lực' : 'Đã hủy'}{r.status === 'Active' && !r.voucherId ? ' (không phát sinh)' : ''}</td>
            <td>{r.id === lastActive?.id && <button disabled={busy} onClick={() => {
              const reason = prompt('Lý do hủy lần chạy khấu hao:')
              if (reason) act(() => post(`/assets/depreciation-runs/${r.id}/reverse`, { reason }), 'Đã hủy lần chạy khấu hao.')
            }}>Hủy</button>}</td></tr>)}</tbody></table>
      </section>

      <section className="panel">
        <div className="toolbar"><h3>Đối chiếu sổ tài sản ↔ sổ cái tại {formatDate(asOf)}</h3>
          <button onClick={() => run(async () => setRecon(await get<ReconRow[]>(`/assets/reconciliation?asOf=${asOf}`)))}>Đối chiếu</button></div>
        {recon && <table className="grid"><thead><tr><th>TK</th><th>Nội dung</th><th className="num">Sổ tài sản</th><th className="num">Sổ cái</th><th className="num">Chênh lệch</th></tr></thead>
          <tbody>{recon.map(r => <tr key={r.accountId} className={r.difference !== '0' ? 'warn' : ''}><td>{r.accountCode}</td><td>{r.kind}</td>
            <td className="num">{formatMoney(r.registerBalance)}</td><td className="num">{formatMoney(r.ledgerBalance)}</td><td className="num">{formatMoney(r.difference)}</td></tr>)}</tbody></table>}
        {recon && <p className="muted">Số dương = dư Nợ, số âm = dư Có. Chênh lệch là bút toán trên TK tài sản/hao mòn chưa gắn với thẻ tài sản nào.</p>}
      </section>
    </>
  )
}

function Dispose({ row, accounts, busy, onSubmit, onCancel }: {
  row: RegisterRow; accounts: ReturnType<typeof useCatalog>['accounts']; busy: boolean
  onSubmit: (date: string, accountId: string, description: string) => void; onCancel: () => void
}) {
  const [date, setDate] = useState(today())
  const [accountId, setAccountId] = useState('')
  const [description, setDescription] = useState('')
  return (
    <section className="panel">
      <h3>Ghi giảm {row.code} – {row.name}</h3>
      <p className="muted">Hệ thống ghi khấu hao của tháng ghi giảm theo cách tính tháng đã chọn, xóa nguyên giá và hao mòn; giá trị còn lại ghi vào TK chọn dưới đây. Tiền thu thanh lý lập hóa đơn bán riêng.</p>
      <label>Ngày ghi giảm <input type="date" value={date} onChange={e => setDate(e.target.value)} /></label>
      <label>TK nhận giá trị còn lại <AccountSelect accounts={accounts} value={accountId} onChange={setAccountId} ariaLabel="TK giá trị còn lại" /></label>
      <label>Căn cứ / diễn giải <input value={description} onChange={e => setDescription(e.target.value)} placeholder="Biên bản thanh lý số…" /></label>
      <div style={{ display: 'flex', gap: 8 }}>
        <button className="primary" disabled={busy || !accountId || !description.trim()} onClick={() => onSubmit(date, accountId, description)}>Ghi giảm</button>
        <button onClick={onCancel}>Hủy</button>
      </div>
    </section>
  )
}

function NewAsset({ accounts, onDone }: { accounts: ReturnType<typeof useCatalog>['accounts']; onDone: (msg?: string) => void }) {
  const [f, setF] = useState({
    code: '', name: '', class: 'FixedAsset' as 'FixedAsset' | 'Tool', assetAccountId: '', accumulatedAccountId: '', expenseAccountId: '',
    proration: 'Daily', frequency: 'Monthly', method: 'StraightLine', scheduleEndDate: '', decliningFactor: '', totalUnits: '', unitName: '', residualValue: '0', usefulLifeMonths: '60', inServiceDate: today(), note: '',
    takenOver: false, openingAccumulated: '0', remainingLifeMonths: '', scheduleStartDate: '',
  })
  const [candidates, setCandidates] = useState<Candidate[]>([])
  const [picked, setPicked] = useState<Record<string, string>>({})
  const { busy, error, run } = useAction()
  const set = (k: keyof typeof f, v: string | boolean) => setF(x => ({ ...x, [k]: v }))

  useEffect(() => {
    setPicked({})
    if (!f.assetAccountId) { setCandidates([]); return }
    get<Candidate[]>(`/assets/cost-candidates?accountId=${f.assetAccountId}`).then(setCandidates).catch(() => setCandidates([]))
  }, [f.assetAccountId])

  const submit = () => run(async () => {
    const money = (s: string, label: string) => { const m = parseMoney(s); if (m == null) throw new Error(`${label} không hợp lệ.`); return m }
    const costSources = Object.entries(picked).map(([journalLineId, amount]) => ({ journalLineId, amount: money(amount, 'Số tiền gán') }))
    if (costSources.length === 0) throw new Error('Chọn ít nhất một bút toán hình thành nguyên giá.')
    const a = await post<{ code: string }>('/assets', {
      code: f.code, name: f.name, class: f.class, assetAccountId: f.assetAccountId,
      accumulatedAccountId: f.class === 'FixedAsset' ? f.accumulatedAccountId || null : null, expenseAccountId: f.expenseAccountId,
      proration: f.proration, frequency: f.frequency, residualValue: money(f.residualValue, 'Giá trị thu hồi'), usefulLifeMonths: f.method === 'TimeBased' || f.method === 'UnitsOfProduction' ? (Number(f.usefulLifeMonths) || 1) : Number(f.usefulLifeMonths),
      method: f.method, scheduleEndDate: f.method === 'TimeBased' ? f.scheduleEndDate || null : null,
      decliningFactor: f.method === 'DecliningBalance' && f.decliningFactor.trim() ? money(f.decliningFactor, 'Hệ số') : null,
      totalUnits: f.method === 'UnitsOfProduction' ? money(f.totalUnits, 'Tổng sản lượng dự kiến') : null, unitName: f.method === 'UnitsOfProduction' ? f.unitName || null : null,
      inServiceDate: f.inServiceDate, costSources, note: f.note || null,
      openingAccumulated: f.takenOver ? money(f.openingAccumulated, 'Hao mòn lũy kế đầu') : '0',
      remainingLifeMonths: f.takenOver ? Number(f.remainingLifeMonths) : null,
      scheduleStartDate: f.takenOver ? f.scheduleStartDate : null,
    })
    onDone(`Đã lập thẻ ${a.code} (Nháp). Người khác bấm "Ghi tăng" để bắt đầu khấu hao.`)
  })

  return (
    <section className="panel">
      <h3>Thẻ tài sản mới</h3>
      <ErrorBox error={error} />
      <div className="form-grid">
        <label>Mã <input value={f.code} onChange={e => set('code', e.target.value)} /></label>
        <label>Tên <input value={f.name} onChange={e => set('name', e.target.value)} /></label>
        <label>Loại <select value={f.class} onChange={e => setF(x => ({ ...x, class: e.target.value as 'FixedAsset' | 'Tool', ...(e.target.value === 'Tool' ? { proration: 'FullMonth', ...(x.method === 'DecliningBalance' ? { method: 'StraightLine' } : {}) } : { proration: 'Daily' }) }))}><option value="FixedAsset">TSCĐ</option><option value="Tool">CCDC / chi phí trả trước</option></select></label>
        <label>TK tài sản <AccountSelect accounts={accounts} value={f.assetAccountId} onChange={v => set('assetAccountId', v)} filter={a => a.cashKind === 'None' && a.openItemNature === 'None' && !a.requiresPartner} ariaLabel="TK tài sản" /></label>
        {f.class === 'FixedAsset' && <label>TK hao mòn <AccountSelect accounts={accounts} value={f.accumulatedAccountId} onChange={v => set('accumulatedAccountId', v)} ariaLabel="TK hao mòn" /></label>}
        <label>TK chi phí <AccountSelect accounts={accounts} value={f.expenseAccountId} onChange={v => set('expenseAccountId', v)} ariaLabel="TK chi phí" /></label>
        <label>Ngày đưa vào sử dụng <input type="date" value={f.inServiceDate} onChange={e => set('inServiceDate', e.target.value)} /></label>
        <label>Phương pháp <select aria-label="Phương pháp khấu hao" value={f.method} onChange={e => setF(x => ({ ...x, method: e.target.value, ...(e.target.value === 'UnitsOfProduction' ? { frequency: 'Monthly' } : {}) }))}>
          <option value="StraightLine">Đường thẳng theo tháng</option><option value="TimeBased">Theo thời gian (số ngày thực tế)</option>
          <option value="UnitsOfProduction">Theo sản lượng</option>{f.class === 'FixedAsset' && <option value="DecliningBalance">Số dư giảm dần (có điều chỉnh)</option>}</select></label>
        {(f.method === 'StraightLine' || f.method === 'DecliningBalance') && <label>Thời gian sử dụng (tháng) <input inputMode="numeric" value={f.usefulLifeMonths} onChange={e => set('usefulLifeMonths', e.target.value)} /></label>}
        {f.method === 'TimeBased' && <label>Phân bổ đến ngày <input type="date" aria-label="Ngày kết thúc phân bổ" value={f.scheduleEndDate} onChange={e => set('scheduleEndDate', e.target.value)} /></label>}
        {f.method === 'DecliningBalance' && <div className="msg" style={{ gridColumn: '1 / -1' }}>Chỉ áp dụng cho doanh nghiệp thuộc lĩnh vực công nghệ đòi hỏi thay đổi, phát triển nhanh và TSCĐ là máy móc, thiết bị, dụng cụ đo lường, thí nghiệm đầu tư mới (Thông tư 45/2013 Điều 13 khoản 2). Phần mềm không kiểm tra được điều kiện này.</div>}
        {f.method === 'UnitsOfProduction' && <div className="msg" style={{ gridColumn: '1 / -1' }}>Chỉ áp dụng cho TSCĐ trực tiếp tham gia sản xuất, xác định được tổng sản lượng theo công suất thiết kế và công suất sử dụng thực tế bình quân không thấp hơn 100% công suất thiết kế (Thông tư 45/2013 Điều 13 khoản 3).</div>}
        {f.method === 'DecliningBalance' && <label title="Để trống: tự chọn theo thời gian sử dụng (đến 4 năm 1,5; trên 4 đến 6 năm 2,0; trên 6 năm 2,5 — Thông tư 45/2013 Điều 13)">Hệ số điều chỉnh <input inputMode="decimal" aria-label="Hệ số số dư giảm dần" placeholder="tự động" value={f.decliningFactor} onChange={e => set('decliningFactor', e.target.value)} /></label>}
        {f.method === 'UnitsOfProduction' && <><label>Tổng sản lượng dự kiến <input inputMode="decimal" aria-label="Tổng sản lượng dự kiến" value={f.totalUnits} onChange={e => set('totalUnits', e.target.value)} /></label>
          <label>Đơn vị sản lượng <input aria-label="Đơn vị sản lượng" placeholder="tấn, giờ máy, km…" value={f.unitName} onChange={e => set('unitName', e.target.value)} /></label></>}
        <label>Giá trị thu hồi ước tính <input inputMode="decimal" value={f.residualValue} onChange={e => set('residualValue', e.target.value)} /></label>
        {(f.method === 'StraightLine' || f.method === 'DecliningBalance') && <label>Tính tháng đầu/cuối <select value={f.proration} onChange={e => set('proration', e.target.value)}>
          <option value="Daily">Theo số ngày sử dụng (Thông tư 45/2013 Điều 9 khoản 9)</option><option value="FullMonth">Tròn tháng (tính tháng tăng, không tính tháng giảm)</option></select>
          {f.class === 'FixedAsset' && f.proration === 'FullMonth' && <span className="muted" style={{ fontSize: 12 }}>Thông tư 45/2013 tính khấu hao TSCĐ từ ngày tăng / giảm theo số ngày của tháng; tròn tháng là chính sách nội bộ, chênh lệch phải điều chỉnh khi quyết toán thuế.</span>}</label>}
        <label title="Số phải ghi mỗi kỳ là lũy kế theo kế hoạch trừ số đã ghi, nên ghi theo quý hoặc năm thì dồn đủ các tháng trước đó; thẻ kết thúc giữa quý / năm thì ghi nốt ở tháng kết thúc">Ghi sổ khấu hao / phân bổ <select aria-label="Kỳ ghi sổ" value={f.frequency} disabled={f.method === 'UnitsOfProduction'} onChange={e => set('frequency', e.target.value)}>
          <option value="Monthly">Hàng tháng</option><option value="Quarterly">Hàng quý (cuối quý của năm tài chính)</option><option value="Yearly">Hàng năm (cuối năm tài chính)</option></select></label>
        <label><input type="checkbox" checked={f.takenOver} onChange={e => set('takenOver', e.target.checked)} /> Chuyển từ hệ thống cũ (đã khấu hao một phần)</label>
        {f.takenOver && <>
          <label>Hao mòn lũy kế đến ngày chuyển <input inputMode="decimal" value={f.openingAccumulated} onChange={e => set('openingAccumulated', e.target.value)} /></label>
          <label>Số tháng còn lại <input inputMode="numeric" value={f.remainingLifeMonths} onChange={e => set('remainingLifeMonths', e.target.value)} /></label>
          <label>Bắt đầu khấu hao trên hệ thống mới <input type="date" value={f.scheduleStartDate} onChange={e => set('scheduleStartDate', e.target.value)} /></label>
        </>}
      </div>
      <h4>Bút toán hình thành nguyên giá (phát sinh Nợ TK tài sản, đã ghi sổ)</h4>
      {f.assetAccountId && candidates.length === 0 && <p className="muted">Không có bút toán nào còn chưa gán trên tài khoản này.</p>}
      {candidates.length > 0 && <table className="grid"><thead><tr><th></th><th>Chứng từ</th><th>Ngày</th><th>Diễn giải</th><th className="num">Số tiền</th><th className="num">Đã gán</th><th className="num">Gán cho thẻ này</th></tr></thead>
        <tbody>{candidates.map(c => <tr key={c.journalLineId}>
          <td><input type="checkbox" aria-label={`Chọn ${c.voucherNumber}`} checked={c.journalLineId in picked} onChange={e => setPicked(p => {
            const n = { ...p }; if (e.target.checked) n[c.journalLineId] = remaining(c); else delete n[c.journalLineId]; return n })} /></td>
          <td>{c.voucherNumber}</td><td>{formatDate(c.postingDate)}</td><td>{c.description}</td><td className="num">{formatMoney(c.lineAmount)}</td><td className="num">{formatMoney(c.assigned)}</td>
          <td className="num">{c.journalLineId in picked && <input inputMode="decimal" value={picked[c.journalLineId]} aria-label="Số tiền gán"
            onChange={e => setPicked(p => ({ ...p, [c.journalLineId]: e.target.value }))} />}</td>
        </tr>)}</tbody></table>}
      <div style={{ display: 'flex', gap: 8, marginTop: 8 }}>
        <button className="primary" disabled={busy || !f.code || !f.name || !f.assetAccountId || !f.expenseAccountId} onClick={submit}>Lưu nháp</button>
        <button onClick={() => onDone()}>Hủy</button>
      </div>
    </section>
  )
}
