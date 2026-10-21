import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { ApiError, get, session, type Account, type Partner } from './api'
import { formatMoney } from './money'
import { downloadXlsx, type Cell, type Column } from './xlsx'

/**
 * Thanh trạng thái của chứng từ (kiểu Odoo): các bước theo thứ tự, bước hiện tại tô đậm, các bước đã qua đánh dấu; trạng thái dừng ngoài luồng
 * (đã đảo, đã hủy) hiện thành một bước đỏ ở cuối. Nút hành động của luồng (gửi duyệt, duyệt, ghi sổ…) đặt ở bên phải qua children.
 */
export function StatusBar({ steps, current, stopped, children }: { steps: { key: string; label: string }[]; current: string; stopped?: Record<string, string>; children?: ReactNode }) {
  const idx = steps.findIndex(s => s.key === current)
  return (
    <div className="statusbar" role="group" aria-label="Trạng thái chứng từ">
      {steps.map((s, i) => <span key={s.key} className={`step${stopped?.[current] ? (i < steps.length ? ' done' : '') : i < idx ? ' done' : i === idx ? ' current' : ''}`} aria-current={i === idx ? 'step' : undefined}>{s.label}</span>)}
      {stopped?.[current] && <span className="step stopped" aria-current="step">{stopped[current]}</span>}
      {children && <span className="actions">{children}</span>}
    </div>
  )
}

export interface FlowStep { label: string; journal: boolean; screen?: string; note?: string; count?: string | null }

/** Dải các khâu của một chu trình (tệp phân tích ERP, mục V): khâu nào ghi Nhật ký, bấm để sang màn hình của khâu, số chứng từ đang chờ ở khâu. */
export function FlowStrip({ steps }: { steps: FlowStep[] }) {
  return (
    <div className="panel" style={{ display: 'flex', gap: 6, flexWrap: 'wrap', alignItems: 'stretch' }}>
      {steps.map((st, i) => (
        <div key={st.label} style={{ flex: '1 1 140px', border: '1px solid var(--line)', borderRadius: 6, padding: '6px 8px', cursor: st.screen ? 'pointer' : undefined }} onClick={() => st.screen && navStore.go?.(st.screen)}>
          <div style={{ fontWeight: 600 }}>{i + 1}. {st.label}</div>
          <div className="muted">{st.note ?? (st.journal ? 'ghi Nhật ký' : 'không ghi Nhật ký')}</div>
          {st.count != null && <div>{st.count}</div>}
        </div>))}
    </div>
  )
}

/** "Xuất Excel" for any report table: exact decimal values, typed numeric columns. */
export function ExportButton({ fileName, sheet, columns, rows, disabled, title, subtitle }:
  { fileName: string; sheet: string; columns: Column[]; rows: Cell[][]; disabled?: boolean; title?: string; subtitle?: string }) {
  const [printing, setPrinting] = useState(false)
  return <>
    <button disabled={disabled || rows.length === 0} onClick={() => downloadXlsx(fileName, sheet, columns, rows)}>Xuất Excel</button>
    <button disabled={disabled || rows.length === 0} onClick={() => setPrinting(true)} title="In bản chính thức: tên công ty, tiêu đề, chữ ký">In</button>
    {printing && <TablePrint title={title ?? sheet} subtitle={subtitle ?? fileName.replace(/_/g, ' ')} columns={columns} rows={rows} onDone={() => setPrinting(false)} />}
  </>
}

/** Cross-screen jump (e.g. trial balance row → account ledger): App registers `go`, the target screen consumes `ledger` once on mount. */
/** kind: hướng bán/mua của phân hệ đang mở (Bán hàng → 'Sales', Mua hàng → 'Purchase'); các màn dùng chung (hợp đồng, đơn hàng, hóa đơn, nghiệm thu, chiết khấu) lấy làm giá trị ban đầu. */
export const navStore: { go?: (screen: string) => void; setupTab?: string; kind?: 'Sales' | 'Purchase'; ledger?: { accountId: string; from: string; to: string }; receipt?: { partnerId: string; settleJournalLineId: string; amount: string } } = {}

export function useCompanyName() {
  const [name, setName] = useState('')
  useEffect(() => { get<{ id: string; name: string }[]>('/me/companies').then(cs => setName(cs.find(c => c.id === session.companyId)?.name ?? '')).catch(() => undefined) }, [])
  return name
}

/** Any report table on paper: company, title, period line, table with repeating header, signatures. Numeric columns use Vietnamese grouping from the exact string. */
function TablePrint({ title, subtitle, columns, rows, onDone }: { title: string; subtitle: string; columns: Column[]; rows: Cell[][]; onDone: () => void }) {
  const company = useCompanyName()
  useEffect(() => { const t = setTimeout(() => { printSheet(); onDone() }, 150); return () => clearTimeout(t) }, [onDone])
  return (
    <PrintSheet>
      <div><b>{company}</b></div>
      <h1>{title.toUpperCase()}</h1>
      <div className="center">{subtitle}</div>
      <table>
        <thead><tr>{columns.map((c, i) => <th key={i}>{c.header}</th>)}</tr></thead>
        <tbody>{rows.map((r, i) => <tr key={i}>{columns.map((c, j) => {
          const v = r[j]
          return <td key={j} className={c.numeric ? 'num' : undefined} style={{ whiteSpace: 'pre-wrap' }}>{v == null ? '' : c.numeric ? formatMoney(v) : v}</td>
        })}</tr>)}</tbody>
      </table>
      <div className="signs"><div><b>Người lập biểu</b><br /><i>(Ký, họ tên)</i></div><div><b>Kế toán trưởng</b><br /><i>(Ký, họ tên)</i></div><div><b>Giám đốc</b><br /><i>(Ký, họ tên, đóng dấu)</i></div></div>
      <div className="foot">HAK Accounting · in lúc {new Date().toLocaleString('vi-VN')}. Mẫu in nội bộ, chưa đối chiếu mẫu sổ/báo cáo theo chế độ kế toán [CẦN XÁC MINH].</div>
    </PrintSheet>
  )
}

/** A document laid out for paper, outside the app shell; shown only while printSheet() prints it. */
export function PrintSheet({ children }: { children: ReactNode }) {
  return createPortal(<div className="print-sheet">{children}</div>, document.body)
}

/** Prints only the PrintSheet content (window.print blocks until the print dialog closes). */
export function printSheet() {
  document.body.classList.add('print-sheet-mode')
  try { window.print() } finally { document.body.classList.remove('print-sheet-mode') }
}

/** Modal dialog: Esc or the backdrop closes it (unless busy); focus moves into it and Tab stays inside it; Enter in a field runs onSubmit (pass it only when the form is valid). */
export function Modal({ title, onClose, children, footer, busy, onSubmit }: { title: string; onClose: () => void; children: ReactNode; footer?: ReactNode; busy?: boolean; onSubmit?: () => void }) {
  const [box, setBox] = useState<HTMLDivElement | null>(null)
  const latest = useRef({ onClose, busy, onSubmit })
  latest.current = { onClose, busy, onSubmit }
  useEffect(() => {
    if (!box) return
    const focusable = () => [...box.querySelectorAll<HTMLElement>('input, select, textarea, button')].filter(el => !(el as HTMLButtonElement).disabled)
    const back = document.activeElement as HTMLElement | null
    focusable().find(el => el.tagName !== 'BUTTON')?.focus()
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !latest.current.busy) latest.current.onClose()
      const t = e.target as HTMLInputElement
      if (e.key === 'Enter' && t.tagName === 'INPUT' && t.type !== 'checkbox' && box.contains(t) && !latest.current.busy && latest.current.onSubmit) {
        e.preventDefault(); latest.current.onSubmit()
      }
      if (e.key !== 'Tab') return
      const els = focusable()
      if (els.length === 0) return
      const i = els.indexOf(document.activeElement as HTMLElement)
      if (e.shiftKey && i <= 0) { e.preventDefault(); els[els.length - 1].focus() }
      else if (!e.shiftKey && (i === -1 || i === els.length - 1)) { e.preventDefault(); els[0].focus() }
    }
    window.addEventListener('keydown', onKey)
    return () => { window.removeEventListener('keydown', onKey); back?.focus?.() }
  }, [box])
  return createPortal(
    <div className="modal-backdrop" onMouseDown={e => { if (e.target === e.currentTarget && !busy) onClose() }}>
      <div className="modal" role="dialog" aria-modal="true" aria-label={title} ref={setBox}>
        <header><h3>{title}</h3><button className="tree-toggle" aria-label="Đóng" disabled={busy} onClick={onClose}>✕</button></header>
        <div className="modal-body">{children}</div>
        {footer && <footer>{footer}</footer>}
      </div>
    </div>, document.body)
}

export function ErrorBox({ error }: { error: unknown }) {
  if (!error) return null
  if (error instanceof ApiError)
    return (
      <div className="msg error" role="alert">
        <strong>{error.code}</strong>: {error.message}
        {error.errors.length > 1 && (
          <ul>{error.errors.map((e, i) => <li key={i}>{e.lineNo ? `Dòng ${e.lineNo}: ` : ''}{e.message}</li>)}</ul>
        )}
      </div>
    )
  return <div className="msg error" role="alert">{String(error)}</div>
}

/** Runs an async action, tracking busy state and the last error. */
export function useAction() {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const run = useCallback(async <T,>(fn: () => Promise<T>): Promise<T | undefined> => {
    setBusy(true); setError(null)
    try { return await fn() } catch (e) { setError(e); return undefined } finally { setBusy(false) }
  }, [])
  return { busy, error, setError, run }
}

export function useCatalog() {
  const [accounts, setAccounts] = useState<Account[]>([])
  const [partners, setPartners] = useState<Partner[]>([])
  useEffect(() => {
    get<Account[]>('/accounts').then(setAccounts).catch(() => setAccounts([]))
    get<Partner[]>('/partners').then(setPartners).catch(() => setPartners([]))
  }, [])
  return { accounts, partners, postable: accounts.filter(a => a.isPostable && a.isActive) }
}

export function AccountSelect({ accounts, value, onChange, filter, ariaLabel }:
  { accounts: Account[]; value: string; onChange: (id: string) => void; filter?: (a: Account) => boolean; ariaLabel?: string }) {
  return (
    <select value={value} onChange={e => onChange(e.target.value)} aria-label={ariaLabel}>
      <option value="">— chọn —</option>
      {accounts.filter(a => a.isPostable && a.isActive && (!filter || filter(a))).map(a =>
        <option key={a.id} value={a.id}>{a.code} – {a.name}</option>)}
    </select>
  )
}

export function PartnerSelect({ partners, value, onChange }: { partners: Partner[]; value: string; onChange: (id: string) => void }) {
  return (
    <select value={value} onChange={e => onChange(e.target.value)} aria-label="Đối tượng">
      <option value="">—</option>
      {partners.filter(p => p.isActive).map(p => <option key={p.id} value={p.id}>{p.code} – {p.name}</option>)}
    </select>
  )
}

export const STATUS_VI: Record<string, string> = {
  Draft: 'Nháp', Submitted: 'Chờ duyệt', Approved: 'Đã duyệt', Posted: 'Đã ghi sổ', Reversed: 'Đã đảo', Cancelled: 'Đã hủy',
}

/** Mã số B03-DN (TT 99/2025) for cash lines; empty = derived from the counter-account. */
export const CASHFLOW_VI: Record<string, string> = {
  '01': 'Thu bán hàng, cung cấp DV', '02': 'Chi trả người cung cấp', '03': 'Chi trả người lao động', '04': 'Chi phí đi vay đã trả',
  '05': 'Thuế TNDN đã nộp', '06': 'Thu khác HĐKD', '07': 'Chi khác HĐKD', '21': 'Chi mua sắm, XD TSCĐ, TS dài hạn',
  '22': 'Thu thanh lý TSCĐ, TS dài hạn', '23': 'Chi cho vay, mua công cụ nợ', '24': 'Thu hồi cho vay, bán công cụ nợ',
  '25': 'Chi góp vốn vào đơn vị khác', '26': 'Thu hồi góp vốn', '27': 'Thu lãi, cổ tức, lợi nhuận được chia',
  '31': 'Thu phát hành CP, nhận vốn góp', '32': 'Trả lại vốn góp, mua lại CP', '33': 'Thu từ đi vay', '34': 'Trả nợ gốc vay',
  '35': 'Trả nợ gốc thuê tài chính', '36': 'Cổ tức, lợi nhuận đã trả', '61': 'Ảnh hưởng chênh lệch tỷ giá',
}

export const TYPE_VI: Record<string, string> = {
  GJ: 'Chứng từ khác', CLS: 'Kết chuyển', ALC: 'Phân bổ', ADJ: 'Điều chỉnh', REV: 'Đảo', FXR: 'Đánh giá', OPB: 'Số dư đầu',
  CR: 'Phiếu thu', CP: 'Phiếu chi', BR: 'Báo Có', BP: 'Báo Nợ',
  IR: 'Nhập kho', II: 'Xuất kho', IC: 'Tính giá xuất kho', PI: 'Hóa đơn mua', SI: 'Hóa đơn bán',
  DEP: 'Khấu hao / phân bổ', FAD: 'Ghi giảm tài sản', LIA: 'Trích lãi vay',
}
