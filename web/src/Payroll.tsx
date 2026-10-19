import { useCallback, useEffect, useState } from 'react'
import { get, post, postRaw } from './api'
import { AccountSelect, ErrorBox, ExportButton, FlowStrip, STATUS_VI, useAction, useCatalog } from './common'
import { formatMoney, today } from './money'

interface Totals { employees: number; gross: string; socialInsurance: string; healthInsurance: string; unemploymentInsurance: string; personalIncomeTax: string; otherDeduction: string; net: string; employerContributions: string }
interface ImportResult {
  packageId: string; sourceId: string; sourceVersion: string; mode: string; outcome: 'VALID' | 'CREATED' | 'DUPLICATE' | 'CONFLICT' | 'INVALID'
  totals: Totals | null; voucherNumber: string | null; errors: { code: string; message: string; line: number | null }[]
}
interface Sheet { id: string; sourceId: string; sourceVersion: string; periodFrom: string; periodTo: string; postingDate: string; description: string; voucherNumber: string; voucherStatus: string; employees: number; gross: string; net: string }
interface SummaryRow { employeeId: string; employeeCode: string; employeeName: string; gross: string; socialInsurance: string; healthInsurance: string; unemploymentInsurance: string; personalIncomeTax: string; otherDeduction: string; net: string; employerContributions: string }

const OUTCOME_VI: Record<ImportResult['outcome'], string> = {
  VALID: 'Hợp lệ (xem trước — chưa ghi gì)', CREATED: 'Đã nhập, lập chứng từ nháp', DUPLICATE: 'Đã nhập phiên bản này trước đây',
  CONFLICT: 'Xung đột: phiên bản khác đang có hiệu lực', INVALID: 'Không hợp lệ — không ghi gì',
}

export function PayrollView() {
  const year = today().slice(0, 4)
  const [from, setFrom] = useState(`${year}-01-01`)
  const [to, setTo] = useState(`${year}-12-31`)
  const [file, setFile] = useState<{ name: string; text: string } | null>(null)
  const [result, setResult] = useState<ImportResult | null>(null)
  const [sheets, setSheets] = useState<Sheet[]>([])
  const [summary, setSummary] = useState<SummaryRow[]>([])
  const [unpaid, setUnpaid] = useState(0)
  const { busy, error, run } = useAction()

  const load = useCallback(() => run(async () => {
    setSheets(await get<Sheet[]>(`/payroll/sheets?from=${from}&to=${to}`))
    setSummary(await get<SummaryRow[]>(`/payroll/summary?from=${from}&to=${to}`))
  }), [run, from, to])
  useEffect(() => { load() }, [load])

  const send = (commit: boolean) => run(async () => {
    if (!file) throw new Error('Chọn file bảng lương (JSON).')
    const r = await postRaw<ImportResult>(`/payroll/import?commit=${commit}`, file.text)
    setResult(r)
    if (commit) await load()
  })

  return (
    <>
      <div className="toolbar">
        <h2>Lương — nhận bảng lương từ phần mềm tính lương</h2>
        <label>Từ <input type="date" value={from} onChange={e => setFrom(e.target.value)} /></label>
        <label>Đến <input type="date" value={to} onChange={e => setTo(e.target.value)} /></label>
        <button onClick={() => load()}>Tải lại</button>
      </div>
      <p className="muted">File JSON theo schema payroll 1.0, có chữ ký của phần mềm lương. HAK không tự tính bảo hiểm / thuế TNCN; chỉ kiểm tra lương gộp − khấu trừ = thực lĩnh, nhân viên có trong danh mục, rồi lập chứng từ nháp theo các vai trò TK PAYROLL_*.</p>
      <ErrorBox error={error} />

      <section className="panel">
        <div className="form-row">
          <label>File bảng lương <input type="file" accept=".json,application/json" onChange={async e => {
            const f = e.target.files?.[0]; setResult(null)
            setFile(f ? { name: f.name, text: await f.text() } : null)
          }} /></label>
          <button disabled={busy || !file} onClick={() => send(false)}>Kiểm tra (xem trước)</button>
          <button className="primary" disabled={busy || result?.outcome !== 'VALID'} onClick={() => send(true)}>Nhập và lập chứng từ nháp</button>
        </div>
        {result && (
          <div className={`msg ${result.outcome === 'VALID' || result.outcome === 'CREATED' ? 'ok' : 'error'}`}>
            <strong>{OUTCOME_VI[result.outcome]}</strong> — bảng lương {result.sourceId} phiên bản {result.sourceVersion}
            {result.voucherNumber && <> · chứng từ {result.voucherNumber}{result.outcome === 'CREATED' && ' (gửi duyệt và ghi sổ ở màn hình Chứng từ)'}</>}
            {result.totals && <div>{result.totals.employees} nhân viên · lương gộp {formatMoney(result.totals.gross)} · thuế TNCN {formatMoney(result.totals.personalIncomeTax)} · thực lĩnh {formatMoney(result.totals.net)} · DN đóng {formatMoney(result.totals.employerContributions)}</div>}
            {result.errors.length > 0 && <ul>{result.errors.map((e, i) => <li key={i}>{e.line ? `Dòng ${e.line}: ` : ''}{e.code}: {e.message}</li>)}</ul>}
          </div>
        )}
      </section>

      <section className="panel">
        <h3>Bảng lương đã nhận</h3>
        <table className="grid"><thead><tr><th>Bảng lương</th><th>Phiên bản</th><th>Kỳ</th><th>Ngày hạch toán</th><th>Chứng từ</th><th>Trạng thái</th><th className="num">Số NV</th><th className="num">Lương gộp</th><th className="num">Thực lĩnh</th></tr></thead>
          <tbody>{sheets.map(s => <tr key={s.id}><td title={s.description}>{s.sourceId}</td><td>{s.sourceVersion}</td><td>{s.periodFrom} – {s.periodTo}</td><td>{s.postingDate}</td>
            <td>{s.voucherNumber}</td><td>{STATUS_VI[s.voucherStatus] ?? s.voucherStatus}</td><td className="num">{s.employees}</td><td className="num">{formatMoney(s.gross)}</td><td className="num">{formatMoney(s.net)}</td></tr>)}
            {sheets.length === 0 && <tr><td colSpan={9} className="muted">Chưa có bảng lương trong khoảng ngày.</td></tr>}</tbody></table>
      </section>

      <FlowStrip steps={[
        { label: 'Nhận bảng lương từ phần mềm lương (JSON có chữ ký)', journal: false },
        { label: 'Kiểm tra gói, nhân viên, lương gộp − khấu trừ = thực lĩnh', journal: false },
        { label: 'Chứng từ lương (Nợ chi phí / Có phải trả người lao động, BHXH, thuế)', journal: true, screen: 'vouchers', count: `${sheets.filter(x => x.voucherStatus === 'Draft').length} chứng từ nháp chờ ghi sổ` },
        { label: 'Hàng chờ trả lương → lệnh thanh toán lương Nháp', journal: false, count: `${unpaid} khoản chưa có lệnh` },
        { label: 'Ngân hàng chi', journal: true, screen: 'bank' },
      ]} />
      <PayrollPayable onCount={setUnpaid} />
      <ExchangeKeys />

      <section className="panel">
        <div className="toolbar"><h3>Tổng hợp theo nhân viên (chỉ bảng lương đã ghi sổ)</h3>
          <ExportButton fileName={'Tong_hop_luong_' + from + '_' + to} sheet="Tổng hợp lương" columns={[{ header: 'Mã' }, { header: 'Họ tên', width: 28 },
            ...['Lương gộp', 'BHXH', 'BHYT', 'BHTN', 'Thuế TNCN', 'Khấu trừ khác', 'Thực lĩnh', 'DN đóng'].map(h => ({ header: h, numeric: true }))]}
            rows={summary.map(r => [r.employeeCode, r.employeeName, r.gross, r.socialInsurance, r.healthInsurance, r.unemploymentInsurance, r.personalIncomeTax, r.otherDeduction, r.net, r.employerContributions])} /></div>
        <div className="scroll"><table className="grid"><thead><tr><th>Mã</th><th>Họ tên</th><th className="num">Lương gộp</th><th className="num">BHXH</th><th className="num">BHYT</th><th className="num">BHTN</th>
          <th className="num">Thuế TNCN</th><th className="num">Khấu trừ khác</th><th className="num">Thực lĩnh</th><th className="num">DN đóng</th></tr></thead>
          <tbody>{summary.map(r => <tr key={r.employeeId}><td>{r.employeeCode}</td><td>{r.employeeName}</td>
            {[r.gross, r.socialInsurance, r.healthInsurance, r.unemploymentInsurance, r.personalIncomeTax, r.otherDeduction, r.net, r.employerContributions].map((v, i) => <td key={i} className="num">{formatMoney(v)}</td>)}</tr>)}</tbody></table></div>
      </section>
    </>
  )
}

interface PayableRow { journalLineId: string; voucherNumber: string; postingDate: string; employeeCode: string; employeeName: string; net: string; open: string; description: string | null; paymentOrderNumber: string | null; paymentOrderStatus: string | null }
const ORDER_STATUS_VI: Record<string, string> = { Created: 'Chờ duyệt', Approved: 'Đã duyệt', SentToBank: 'Đã gửi ngân hàng', ExecutedByBank: 'NH đã chi', Posted: 'Đã ghi sổ', Reconciled: 'Đã đối chiếu' }

/** Bàn giao từ nhân sự – lương sang thanh toán: phải trả người lao động còn nợ → lệnh thanh toán lương Nháp (người khác duyệt ở màn Ngân hàng). */
function PayrollPayable({ onCount }: { onCount?: (n: number) => void }) {
  const { accounts } = useCatalog()
  const [rows, setRows] = useState<PayableRow[]>([])
  const [pick, setPick] = useState<Set<string>>(new Set())
  const [bank, setBank] = useState('')
  const [info, setInfo] = useState('')
  const { busy, error, run } = useAction()
  const asOf = today()
  const load = useCallback(() => run(async () => { const r = await get<PayableRow[]>(`/cycles/finance/payroll-payable?asOf=${asOf}`); setRows(r); onCount?.(r.filter(x => !x.paymentOrderNumber).length) }), [run, asOf, onCount])
  useEffect(() => { load() }, [load])
  const free = rows.filter(r => !r.paymentOrderNumber)
  return (
    <section className="panel">
      <h3>Hàng chờ trả lương <span className="muted">· phải trả người lao động còn nợ sau khi ghi sổ phiếu lương (cần ánh xạ vai trò TK PAYROLL_PAYABLE)</span></h3>
      <ErrorBox error={error} />
      {info && <div className="msg ok">{info}</div>}
      <table className="grid"><thead><tr><th /><th>Nhân viên</th><th>Chứng từ</th><th>Ngày</th><th className="num">Còn phải trả</th><th>Lệnh thanh toán</th></tr></thead>
        <tbody>{rows.map(r => <tr key={r.journalLineId}>
          <td><input type="checkbox" aria-label={`Chọn ${r.employeeCode}`} disabled={!!r.paymentOrderNumber} checked={pick.has(r.journalLineId)}
            onChange={e => setPick(v => { const n = new Set(v); if (e.target.checked) n.add(r.journalLineId); else n.delete(r.journalLineId); return n })} /></td>
          <td>{r.employeeCode} – {r.employeeName}</td><td>{r.voucherNumber}</td><td>{r.postingDate}</td><td className="num">{formatMoney(r.open)}</td>
          <td>{r.paymentOrderNumber ? `${r.paymentOrderNumber} (${ORDER_STATUS_VI[r.paymentOrderStatus ?? ''] ?? r.paymentOrderStatus})` : '—'}</td></tr>)}
          {rows.length === 0 && <tr><td colSpan={6} className="muted">Không có khoản phải trả lương nào đang mở.</td></tr>}</tbody></table>
      {free.length > 0 && <div className="toolbar">
        <button onClick={() => setPick(pick.size === free.length ? new Set() : new Set(free.map(r => r.journalLineId)))}>{pick.size === free.length ? 'Bỏ chọn hết' : 'Chọn hết'}</button>
        <label>Tài khoản ngân hàng chi<AccountSelect accounts={accounts} value={bank} onChange={setBank} filter={a => a.cashKind === 'Bank'} ariaLabel="Tài khoản ngân hàng chi lương" /></label>
        <button className="primary" disabled={busy || !bank || pick.size === 0} onClick={() => run(async () => {
          const created = await post<{ number: string }[]>('/cycles/finance/payroll-payable/orders', { bankAccountId: bank, journalLineIds: [...pick] })
          setPick(new Set()); setInfo(`Đã lập ${created.length} lệnh thanh toán lương nháp (${created.map(o => o.number).join(', ')}); người khác duyệt ở màn Ngân hàng.`); await load()
        })}>Lập lệnh thanh toán lương ({pick.size})</button></div>}
    </section>
  )
}

interface KeyRow { id: string; sourceSystem: string; keyId: string; isActive: boolean }

/** HMAC keys of systems allowed to send packages. Hidden for users without config.manage. */
function ExchangeKeys() {
  const [keys, setKeys] = useState<KeyRow[] | null>(null)
  const [source, setSource] = useState('')
  const [keyId, setKeyId] = useState('')
  const [issued, setIssued] = useState<{ sourceSystem: string; keyId: string; secretBase64: string } | null>(null)
  const { busy, error, run } = useAction()
  const load = useCallback(() => get<KeyRow[]>('/exchange/keys').then(setKeys).catch(() => setKeys(null)), [])
  useEffect(() => { load() }, [load])
  if (keys === null) return null
  return (
    <section className="panel">
      <h3>Khóa trao đổi dữ liệu (phần mềm lương, hệ thống khác gửi gói JSON)</h3>
      <ErrorBox error={error} />
      {issued && <div className="msg ok">Khóa bí mật của {issued.sourceSystem}/{issued.keyId} — <strong>chỉ hiển thị một lần</strong>, cấu hình ngay vào hệ thống gửi:
        <div><code style={{ userSelect: 'all' }}>{issued.secretBase64}</code></div></div>}
      <div className="form-row">
        <label>Mã hệ thống nguồn <input value={source} onChange={e => setSource(e.target.value.toUpperCase())} placeholder="VD: HRM" /></label>
        <label>Mã khóa <input value={keyId} onChange={e => setKeyId(e.target.value)} placeholder="VD: 2026-01" /></label>
        <button disabled={busy || !source || !keyId} onClick={() => run(async () => {
          setIssued(await post('/exchange/keys', { sourceSystem: source, keyId })); setKeyId(''); await load()
        })}>Tạo khóa</button>
      </div>
      <table className="grid"><thead><tr><th>Hệ thống</th><th>Mã khóa</th><th>Trạng thái</th><th /></tr></thead>
        <tbody>{keys.map(k => <tr key={k.id}><td>{k.sourceSystem}</td><td>{k.keyId}</td><td>{k.isActive ? 'Đang dùng' : 'Ngừng dùng'}</td>
          <td>{k.isActive && <button disabled={busy} onClick={() => { const reason = prompt('Lý do ngừng dùng khóa:'); if (reason) run(async () => { await post(`/exchange/keys/${k.id}/deactivate`, { reason }); await load() }) }}>Ngừng dùng</button>}</td></tr>)}</tbody></table>
    </section>
  )
}