import { useCallback, useEffect, useState } from 'react'
import { get, post, type Account } from './api'
import { AccountSelect, ErrorBox, useAction } from './common'
import { formatDate, today } from './money'

interface RoleRow { code: string; module: string; description: string; optional: boolean; account: string | null }
interface Warehouse { id: string; code: string; name: string; isActive: boolean; policies: { method: string; effectiveFrom: string; note: string }[] }
interface RuleSet { id: string; code: string; version: number; status: string; effectiveFrom: string; effectiveTo: string | null; description: string | null
  steps: { sequence: number; description: string; sourcePrefixes: string; targetAccountCode: string }[] }
interface StepForm { description: string; sourcePrefixes: string; targetId: string }

const METHOD_VI: Record<string, string> = { Fifo: 'Nhập trước xuất trước', MovingAverage: 'Bình quân tức thời', MonthlyAverage: 'Bình quân cuối tháng' }
const STATUS_VI: Record<string, string> = { Draft: 'Nháp', Active: 'Đang hiệu lực', Retired: 'Đã thay thế' }

/** Mẫu gợi ý cho form bước kết chuyển. Chỉ điền sẵn form — người dùng chọn lại tài khoản theo chế độ đang áp dụng rồi mới lưu, không tự kích hoạt. */
const TEMPLATES: { key: string; label: string; steps: { description: string; sourcePrefixes: string; target: string }[] }[] = [
  { key: 'standard', label: 'Kết chuyển xác định kết quả kinh doanh (mã TK theo hệ thống TT200 quen thuộc)', steps: [
    { description: 'Kết chuyển các khoản giảm trừ doanh thu (chiết khấu thương mại, giảm giá, hàng bán bị trả lại) sang doanh thu', sourcePrefixes: '521', target: '511' },
    { description: 'Kết chuyển doanh thu và thu nhập', sourcePrefixes: '511,515,711', target: '911' },
    { description: 'Kết chuyển giá vốn và chi phí', sourcePrefixes: '632,635,641,642,811,821', target: '911' },
    { description: 'Kết chuyển lãi / lỗ', sourcePrefixes: '911', target: '4212' } ] },
  { key: 'fx', label: 'Kết chuyển chênh lệch tỷ giá đánh giá lại (chọn TK 515 nếu lãi, 635 nếu lỗ)', steps: [
    { description: 'Kết chuyển chênh lệch tỷ giá đánh giá lại cuối kỳ', sourcePrefixes: '413', target: '' } ] },
]

/**
 * "Hạch toán tự động": một nơi xem công ty đã thiết lập đủ các bút toán tự động chưa (vai trò tài khoản, kết chuyển, giá xuất kho, khấu hao,
 * duyệt) và cấu hình bộ quy tắc kết chuyển cuối kỳ.
 */
export function AutoPosting({ go }: { go: (tab: 'roles' | 'stock' | 'approval') => void }) {
  const [roles, setRoles] = useState<RoleRow[]>([])
  const [warehouses, setWarehouses] = useState<Warehouse[]>([])
  const [sets, setSets] = useState<RuleSet[]>([])
  const [accounts, setAccounts] = useState<Account[]>([])
  const [distinct, setDistinct] = useState<boolean | null>(null)
  const [info, setInfo] = useState('')
  const { error, run } = useAction()
  const load = useCallback(() => run(async () => {
    setRoles(await get<RoleRow[]>('/rules/account-roles'))
    setWarehouses(await get<Warehouse[]>('/warehouses'))
    setSets(await get<RuleSet[]>('/closing/rule-sets'))
    setAccounts(await get<Account[]>('/accounts'))
    setDistinct((await get<{ requireDistinctApprover: boolean }>('/settings/approval')).requireDistinctApprover)
  }), [run])
  useEffect(() => { load() }, [load])

  const missingRoles = (codes: string[]) => codes.filter(c => !roles.find(r => r.code === c)?.account)
  const activeWarehouses = warehouses.filter(w => w.isActive)
  const noMethod = activeWarehouses.filter(w => w.policies.length === 0)
  const hasActiveClosing = sets.some(s => s.status === 'Active')
  const rows: { label: string; detail: string; ok: boolean; fix?: () => void; fixLabel?: string }[] = [
    { label: 'Hóa đơn mua / bán (công nợ, doanh thu, thuế GTGT)', ok: missingRoles(['AR_TRADE', 'AP_TRADE', 'VAT_INPUT', 'VAT_OUTPUT', 'SALES_REVENUE']).length === 0,
      detail: `Vai trò tài khoản còn thiếu: ${missingRoles(['AR_TRADE', 'AP_TRADE', 'VAT_INPUT', 'VAT_OUTPUT', 'SALES_REVENUE']).join(', ')}`, fix: () => go('roles'), fixLabel: 'Gán vai trò TK' },
    { label: 'Khấu trừ thuế GTGT cuối kỳ', ok: missingRoles(['VAT_INPUT', 'VAT_OUTPUT']).length === 0,
      detail: `Thiếu vai trò: ${missingRoles(['VAT_INPUT', 'VAT_OUTPUT']).join(', ')}. Thực hiện ở Thuế → Khấu trừ thuế cuối kỳ`, fix: () => go('roles'), fixLabel: 'Gán vai trò TK' },
    { label: 'Chênh lệch tỷ giá (đánh giá lại, đã thực hiện)', ok: missingRoles(['FX_REVALUATION', 'FX_REALIZED_GAIN', 'FX_REALIZED_LOSS']).length === 0,
      detail: `Thiếu vai trò: ${missingRoles(['FX_REVALUATION', 'FX_REALIZED_GAIN', 'FX_REALIZED_LOSS']).join(', ')}`, fix: () => go('roles'), fixLabel: 'Gán vai trò TK' },
    { label: 'Kết chuyển cuối kỳ', ok: hasActiveClosing, detail: 'Chưa có bộ quy tắc kết chuyển đang hiệu lực — tạo và kích hoạt ở phần bên dưới' },
    { label: 'Phương pháp tính giá xuất kho', ok: noMethod.length === 0,
      detail: `Kho chưa chọn phương pháp: ${noMethod.map(w => w.code).join(', ')}`, fix: () => go('stock'), fixLabel: 'Chọn ở Kho & vật tư' },
    { label: 'Tài khoản mặc định khi lập thẻ CCDC / TSCĐ (tùy chọn)', ok: missingRoles(['TOOL_COST_DEFAULT', 'TOOL_EXPENSE_DEFAULT']).length === 0,
      detail: 'Chưa đặt tài khoản CCDC (242, 153…) và chi phí phân bổ mặc định — form thẻ mới sẽ để trống, chọn tay từng thẻ', fix: () => go('roles'), fixLabel: 'Gán vai trò TK' },
    { label: 'Chiết khấu thương mại và chiết khấu thanh toán (tùy chọn)', ok: missingRoles(['SALES_TRADE_DISCOUNT', 'PAYMENT_DISCOUNT_INCOME', 'PAYMENT_DISCOUNT_EXPENSE']).length === 0,
      detail: 'Chưa gán đủ: chiết khấu thương mại (giảm trừ doanh thu — nếu để trống dùng chung TK giảm giá hàng bán), chiết khấu thanh toán được hưởng (thu nhập tài chính) và cho khách (chi phí tài chính)', fix: () => go('roles'), fixLabel: 'Gán vai trò TK' },
    { label: 'Quy trình lập và duyệt', ok: distinct !== null, detail: '', fix: () => go('approval'), fixLabel: 'Đổi cách duyệt' },
  ]

  return (
    <>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      <section className="panel">
        <h3>Tình trạng thiết lập các bút toán tự động</h3>
        <table className="grid"><thead><tr><th>Nghiệp vụ</th><th>Trạng thái</th><th /></tr></thead><tbody>
          {rows.map(r => <tr key={r.label}><td>{r.label}</td>
            <td style={{ whiteSpace: 'normal' }}>{r.ok ? <span className="badge Posted">Đã sẵn sàng</span> : <><span className="badge Locked">Chưa đủ</span> <span className="muted">{r.detail}</span></>}
              {r.label === 'Quy trình lập và duyệt' && distinct !== null && <span className="muted"> {distinct ? 'Hai người khác nhau' : 'Một người lập và duyệt được'}</span>}</td>
            <td>{r.fix && (!r.ok || r.label === 'Quy trình lập và duyệt') && <button onClick={r.fix}>{r.fixLabel}</button>}</td></tr>)}
        </tbody></table>
      </section>

      <ClosingSets sets={sets} accounts={accounts} reload={async m => { setInfo(m); await load() }} />

      <section className="panel">
        <h3>Phương pháp tính giá xuất kho</h3>
        <table className="grid"><thead><tr><th>Kho</th><th>Phương pháp hiện hành</th><th>Từ ngày</th></tr></thead><tbody>
          {activeWarehouses.map(w => { const p = w.policies[w.policies.length - 1]
            return <tr key={w.id}><td>{w.code} – {w.name}</td><td>{p ? METHOD_VI[p.method] ?? p.method : <span className="muted">chưa chọn</span>}</td><td>{p ? formatDate(p.effectiveFrom) : ''}</td></tr> })}
        </tbody></table>
        <p className="muted">Phương pháp chỉ đổi được từ ngày đầu một năm tài chính, khi kho chưa có nghiệp vụ từ ngày đó (nguyên tắc nhất quán). <button className="link" onClick={() => go('stock')}>Chọn / đổi ở Kho & vật tư</button></p>
      </section>
    </>
  )
}

function ClosingSets({ sets, accounts, reload }: { sets: RuleSet[]; accounts: Account[]; reload: (msg: string) => Promise<void> }) {
  const [code, setCode] = useState('KET-CHUYEN')
  const [from, setFrom] = useState(today().slice(0, 4) + '-01-01')
  const [steps, setSteps] = useState<StepForm[]>([{ description: '', sourcePrefixes: '', targetId: '' }])
  const { busy, error, run } = useAction()
  const setStep = (i: number, patch: Partial<StepForm>) => setSteps(s => s.map((x, j) => (j === i ? { ...x, ...patch } : x)))
  const applyTemplate = (key: string) => {
    const t = TEMPLATES.find(x => x.key === key)
    if (!t) return
    setSteps(t.steps.map(s => ({ description: s.description, sourcePrefixes: s.sourcePrefixes, targetId: accounts.find(a => a.code === s.target && a.isPostable)?.id ?? '' })))
  }
  const valid = code.trim() !== '' && from !== '' && steps.every(s => s.description.trim() && s.sourcePrefixes.trim() && s.targetId)
  const save = () => run(async () => {
    await post('/closing/rule-sets', { code: code.trim(), effectiveFrom: from, effectiveTo: null, description: null,
      steps: steps.map(s => ({ description: s.description.trim(), sourcePrefixes: s.sourcePrefixes.trim(), targetAccountCode: accounts.find(a => a.id === s.targetId)?.code })) })
    setSteps([{ description: '', sourcePrefixes: '', targetId: '' }])
    await reload('Đã lưu bộ quy tắc ở trạng thái Nháp. Kiểm tra rồi bấm "Kích hoạt" để dùng.')
  })
  return (
    <section className="panel">
      <h3>Bút toán kết chuyển cuối kỳ</h3>
      <p className="muted">Mỗi bước chuyển số dư các tài khoản nguồn (nhập đầu số, cách nhau dấu phẩy) sang một tài khoản đích. Bộ quy tắc có phiên bản; chỉ một bộ đang hiệu lực cho mỗi mã.
        Sau khi kích hoạt, tạo chứng từ kết chuyển từng kỳ ở Tổng hợp → Kỳ kế toán.</p>
      <ErrorBox error={error} />
      <div className="scroll"><table className="grid"><thead><tr><th>Mã</th><th>Phiên bản</th><th>Trạng thái</th><th>Hiệu lực từ</th><th>Các bước</th><th /></tr></thead><tbody>
        {sets.length === 0 && <tr><td colSpan={6} className="muted">Chưa có bộ quy tắc kết chuyển.</td></tr>}
        {sets.map(s => <tr key={s.id}><td>{s.code}</td><td className="num">{s.version}</td><td>{STATUS_VI[s.status] ?? s.status}</td><td>{formatDate(s.effectiveFrom)}</td>
          <td style={{ whiteSpace: 'normal' }}>{s.steps.map(x => <div key={x.sequence}>{x.sequence}. {x.description}: {x.sourcePrefixes} → {x.targetAccountCode}</div>)}</td>
          <td>{s.status === 'Draft' && <button className="primary" disabled={busy} onClick={() => run(async () => { await post(`/closing/rule-sets/${s.id}/activate`); await reload(`Đã kích hoạt ${s.code} phiên bản ${s.version}.`) })}>Kích hoạt</button>}</td></tr>)}
      </tbody></table></div>

      <h4>Tạo bộ quy tắc mới</h4>
      <div className="form-row">
        <label>Mã bộ quy tắc <input value={code} onChange={e => setCode(e.target.value.toUpperCase())} /></label>
        <label>Hiệu lực từ <input type="date" value={from} onChange={e => setFrom(e.target.value)} /></label>
        <label>Điền từ mẫu gợi ý <select value="" onChange={e => applyTemplate(e.target.value)}><option value="">— chọn mẫu —</option>
          {TEMPLATES.map(t => <option key={t.key} value={t.key}>{t.label}</option>)}</select></label>
      </div>
      <p className="muted">Mẫu chỉ điền sẵn biểu mẫu để bạn sửa. Hãy đối chiếu mã tài khoản với chế độ kế toán công ty đang áp dụng (TT99 hay TT200) trước khi lưu.</p>
      <table className="grid"><thead><tr><th>Diễn giải</th><th>TK nguồn (đầu số)</th><th>TK đích</th><th /></tr></thead><tbody>
        {steps.map((s, i) => <tr key={i}>
          <td><input aria-label="Diễn giải" value={s.description} onChange={e => setStep(i, { description: e.target.value })} style={{ width: '100%' }} /></td>
          <td><input aria-label="TK nguồn" value={s.sourcePrefixes} placeholder="VD: 511,515,711" onChange={e => setStep(i, { sourcePrefixes: e.target.value })} /></td>
          <td><AccountSelect accounts={accounts} value={s.targetId} onChange={v => setStep(i, { targetId: v })} ariaLabel="TK đích" /></td>
          <td><button disabled={steps.length === 1} onClick={() => setSteps(x => x.filter((_, j) => j !== i))}>Xóa</button></td></tr>)}
      </tbody></table>
      <div className="form-row" style={{ marginTop: 8 }}>
        <button onClick={() => setSteps(s => [...s, { description: '', sourcePrefixes: '', targetId: '' }])}>Thêm bước</button>
        <button className="primary" disabled={busy || !valid} onClick={save}>Lưu bộ quy tắc (Nháp)</button>
      </div>
    </section>
  )
}
