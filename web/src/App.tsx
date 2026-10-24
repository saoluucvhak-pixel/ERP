import { Suspense, lazy, useEffect, useRef, useState } from 'react'
import { get, post, session, setSessionExpiredHandler } from './api'
import { DashboardView } from './Dashboard'
import { HomeLauncher } from './Home'
import { FlowTab, MODULES, moduleOfScreen } from './Modules'
import { QuickAdd } from './QuickAdd'
import { ErrorBox, navStore, useAction } from './common'
import { confirmLeave } from './unsaved'
import { setPermissions } from './perms'
import { MobileMaintenance, isMobileRoute } from './MobileMaintenance'

// Màn hình nghiệp vụ tải theo nhu cầu (mỗi màn một chunk): lần mở đầu chỉ tải khung, Tổng quan và màn đang mở.
const AdminView = lazy(() => import('./Admin').then(m => ({ default: m.AdminView })))
const AssetsView = lazy(() => import('./Assets').then(m => ({ default: m.AssetsView })))
const BankingView = lazy(() => import('./Banking').then(m => ({ default: m.BankingView })))
const CashDocsView = lazy(() => import('./CashDocs').then(m => ({ default: m.CashDocsView })))
const CatalogView = lazy(() => import('./Catalog').then(m => ({ default: m.CatalogView })))
const AnalysisView = lazy(() => import('./Analysis').then(m => ({ default: m.AnalysisView })))
const OpeningBalancesView = lazy(() => import('./OpeningBalances').then(m => ({ default: m.OpeningBalancesView })))
const FxView = lazy(() => import('./Fx').then(m => ({ default: m.FxView })))
const InventoryView = lazy(() => import('./Inventory').then(m => ({ default: m.InventoryView })))
const LoansView = lazy(() => import('./Loans').then(m => ({ default: m.LoansView })))
const OrdersView = lazy(() => import('./Orders').then(m => ({ default: m.OrdersView })))
const ContractsView = lazy(() => import('./Contracts').then(m => ({ default: m.ContractsView })))
const AdvancesView = lazy(() => import('./Advances').then(m => ({ default: m.AdvancesView })))
const CashCountView = lazy(() => import('./Advances').then(m => ({ default: m.CashCountView })))
const EquityView = lazy(() => import('./Equity').then(m => ({ default: m.EquityView })))
const SalesApprovalsView = lazy(() => import('./SalesApprovals').then(m => ({ default: m.SalesApprovalsView })))
const ServiceAcceptancesView = lazy(() => import('./ServiceAcceptances').then(m => ({ default: m.ServiceAcceptancesView })))
const SpecialJournalsView = lazy(() => import('./SpecialJournals').then(m => ({ default: m.SpecialJournalsView })))
const PaymentDiscountView = lazy(() => import('./PaymentDiscount').then(m => ({ default: m.PaymentDiscountView })))
const GroupsView = lazy(() => import('./Groups').then(m => ({ default: m.GroupsView })))
const PayrollView = lazy(() => import('./Payroll').then(m => ({ default: m.PayrollView })))
const ProcurementView = lazy(() => import('./Procurement').then(m => ({ default: m.ProcurementView })))
const RevenueView = lazy(() => import('./Revenue').then(m => ({ default: m.RevenueView })))
const SetupView = lazy(() => import('./Setup').then(m => ({ default: m.SetupView })))
const ProductionView = lazy(() => import('./ProductionOrders').then(m => ({ default: m.ProductionView })))
const ReportEngineView = lazy(() => import('./ReportEngine').then(m => ({ default: m.ReportEngineView })))
const PeriodsView = lazy(() => import('./Periods').then(m => ({ default: m.PeriodsView })))
const TradeView = lazy(() => import('./Trade').then(m => ({ default: m.TradeView })))
const VatView = lazy(() => import('./Vat').then(m => ({ default: m.VatView })))
const AccountLedgerView = lazy(() => import('./Reports').then(m => ({ default: m.AccountLedgerView })))
const GeneralJournalView = lazy(() => import('./Reports').then(m => ({ default: m.GeneralJournalView })))
const ReceivablesView = lazy(() => import('./Reports').then(m => ({ default: m.ReceivablesView })))
const TrialBalanceView = lazy(() => import('./Reports').then(m => ({ default: m.TrialBalanceView })))
const VouchersView = lazy(() => import('./Vouchers').then(m => ({ default: m.VouchersView })))

interface Company { id: string; code: string; name: string; roles?: string[]; permissions?: string[] }

/** Các màn hình (tab) của phần mềm; phân hệ ở thanh bên xếp chúng thành tab theo cấu trúc trong Modules.tsx. */
const SCREENS = [
  { key: 'home', label: 'Tổng quan', el: () => <DashboardView /> },
  { key: 'cycles', label: 'Quản lý chu trình', el: () => <HomeLauncher screens={LAUNCHER} /> },
  { key: 'contracts', label: 'Hợp đồng', el: () => <ContractsView /> },
  { key: 'orders', label: 'Đơn hàng', el: () => <OrdersView /> },
  { key: 'revenue', label: 'Bán hàng → Thu tiền', el: () => <RevenueView /> },
  { key: 'arap', label: 'Công nợ', el: () => <ReceivablesView /> },
  { key: 'procurement', label: 'Mua hàng → Thanh toán', el: () => <ProcurementView /> },
  { key: 'trade', label: 'Hóa đơn', el: () => <TradeView /> },
  { key: 'stock', label: 'Kho', el: () => <InventoryView /> },
  { key: 'production', label: 'Sản xuất', el: () => <ProductionView /> },
  { key: 'payroll', label: 'Lương', el: () => <PayrollView /> },
  { key: 'cash', label: 'Thu / Chi tiền', el: () => <CashDocsView /> },
  { key: 'advances', label: 'Tạm ứng', el: () => <AdvancesView /> },
  { key: 'cashcount', label: 'Kiểm kê quỹ', el: () => <CashCountView /> },
  { key: 'equity', label: 'Vốn góp, cổ tức', el: () => <EquityView /> },
  { key: 'salesapproval', label: 'Phê duyệt bán hàng', el: () => <SalesApprovalsView /> },
  { key: 'services', label: 'Nghiệm thu dịch vụ', el: () => <ServiceAcceptancesView /> },
  { key: 'paydiscount', label: 'Chiết khấu thanh toán', el: () => <PaymentDiscountView /> },
  { key: 'groups', label: 'Nhóm danh mục', el: () => <GroupsView /> },
  { key: 'bank', label: 'Ngân hàng', el: () => <BankingView /> },
  { key: 'fx', label: 'Ngoại tệ', el: () => <FxView /> },
  { key: 'loans', label: 'Vay', el: () => <LoansView /> },
  { key: 'vat', label: 'Thuế GTGT', el: () => <VatView /> },
  { key: 'assets', label: 'Tài sản / CCDC', el: () => <AssetsView /> },
  { key: 'vouchers', label: 'Chứng từ kế toán', el: () => <VouchersView /> },
  { key: 'journal', label: 'Sổ Nhật ký chung', el: () => <GeneralJournalView /> },
  { key: 'specialjournals', label: 'Sổ nhật ký đặc biệt', el: () => <SpecialJournalsView /> },
  { key: 'ledger', label: 'Sổ chi tiết / Sổ quỹ', el: () => <AccountLedgerView /> },
  { key: 'tb', label: 'Cân đối phát sinh', el: () => <TrialBalanceView /> },
  { key: 'reports', label: 'Báo cáo theo mẫu', el: () => <ReportEngineView /> },
  { key: 'catalog', label: 'Danh mục đối tượng', el: () => <CatalogView /> },
  { key: 'setup', label: 'Thiết lập', el: () => <SetupView /> },
  { key: 'periods', label: 'Kỳ kế toán', el: () => <PeriodsView /> },
  { key: 'admin', label: 'Quản trị', el: () => <AdminView /> },
  { key: 'opening', label: 'Số dư ban đầu', el: () => <OpeningBalancesView /> },
  { key: 'analysis', label: 'Các chỉ số phân tích', el: () => <AnalysisView /> },
] as const
/** Màn hình "Việc cần làm" giữ nguyên: ô theo chu trình, mỗi ô một màn hình; bấm ô mở đúng tab trong phân hệ tương ứng. */
const LAUNCHER = [
  // Đơn và hóa đơn bán thuộc chu trình doanh thu, đơn và hóa đơn mua thuộc chu trình cung ứng: hai ô riêng, mỗi ô mở đúng phân hệ và đúng chiều.
  { key: 'orders:Sales', screen: 'orders', kind: 'Sales', group: 'Chu trình doanh thu', label: 'Đơn bán hàng' },
  { key: 'orders:Purchase', screen: 'orders', kind: 'Purchase', group: 'Chu trình cung ứng', label: 'Đơn mua hàng' },
  { key: 'revenue', group: 'Chu trình doanh thu', label: 'Bán hàng → Thu tiền' },
  { key: 'arap', group: 'Chu trình doanh thu', label: 'Công nợ phải thu / phải trả' },
  { key: 'procurement', group: 'Chu trình cung ứng', label: 'Mua hàng → Thanh toán' },
  { key: 'trade:Sales', screen: 'trade', kind: 'Sales', group: 'Chu trình doanh thu', label: 'Hóa đơn bán hàng' },
  { key: 'trade:Purchase', screen: 'trade', kind: 'Purchase', group: 'Chu trình cung ứng', label: 'Hóa đơn mua hàng' },
  { key: 'stock', group: 'Chu trình cung ứng', label: 'Kho (phiếu, kiểm kê, tồn)' },
  { key: 'production', group: 'Chu trình chuyển đổi', label: 'Sản xuất (lệnh, giá thành)' },
  { key: 'payroll', group: 'Nhân sự – tiền lương', label: 'Lương' },
  { key: 'cash', group: 'Tài chính, thanh toán', label: 'Thu / Chi tiền' },
  { key: 'advances', group: 'Tài chính, thanh toán', label: 'Tạm ứng' },
  { key: 'cashcount', group: 'Tài chính, thanh toán', label: 'Kiểm kê quỹ' },
  { key: 'equity', group: 'Tài chính, thanh toán', label: 'Vốn góp, cổ tức' },
  { key: 'salesapproval', group: 'Chu trình doanh thu', label: 'Phê duyệt bán hàng' },
  { key: 'services', group: 'Chu trình doanh thu', label: 'Nghiệm thu dịch vụ' },
  { key: 'paydiscount', group: 'Chu trình doanh thu', label: 'Chiết khấu thanh toán' },
  { key: 'groups', group: 'Danh mục và hệ thống', label: 'Nhóm danh mục' },
  { key: 'bank', group: 'Tài chính, thanh toán', label: 'Ngân hàng' },
  { key: 'fx', group: 'Tài chính, thanh toán', label: 'Ngoại tệ' },
  { key: 'loans', group: 'Tài chính, thanh toán', label: 'Vay' },
  { key: 'vat', group: 'Tài chính, thanh toán', label: 'Thuế GTGT' },
  { key: 'assets', group: 'Tài chính, thanh toán', label: 'Tài sản / CCDC' },
  { key: 'vouchers', group: 'Kế toán tổng hợp (Nhật ký, sổ, báo cáo)', label: 'Chứng từ kế toán' },
  { key: 'journal', group: 'Kế toán tổng hợp (Nhật ký, sổ, báo cáo)', label: 'Sổ Nhật ký chung' },
  { key: 'specialjournals', group: 'Kế toán tổng hợp (Nhật ký, sổ, báo cáo)', label: 'Sổ nhật ký đặc biệt' },
  { key: 'ledger', group: 'Kế toán tổng hợp (Nhật ký, sổ, báo cáo)', label: 'Sổ chi tiết / Sổ quỹ' },
  { key: 'tb', group: 'Kế toán tổng hợp (Nhật ký, sổ, báo cáo)', label: 'Cân đối phát sinh' },
  { key: 'reports', group: 'Kế toán tổng hợp (Nhật ký, sổ, báo cáo)', label: 'Báo cáo theo mẫu' },
  { key: 'catalog', group: 'Danh mục và hệ thống', label: 'Danh mục đối tượng' },
  { key: 'setup', group: 'Danh mục và hệ thống', label: 'Thiết lập' },
  { key: 'periods', group: 'Danh mục và hệ thống', label: 'Kỳ kế toán' },
  { key: 'admin', group: 'Danh mục và hệ thống', label: 'Quản trị' },
] as const
const SCREEN_LABELS: Record<string, string> = Object.fromEntries(SCREENS.map(s => [s.key, s.label]))

/**
 * Đơn hàng, hóa đơn, hợp đồng, nghiệm thu dịch vụ dùng chung màn hình cho hai chu trình. Mỗi phân hệ (Mua hàng / Bán hàng) chỉ thấy MỘT chiều:
 * nhãn tab nói rõ mua hay bán và số việc chờ chỉ tính chiều của phân hệ đó.
 */
const KIND_OF_MODULE: Record<string, 'Purchase' | 'Sales'> = { purchase: 'Purchase', sales: 'Sales' }
const KINDED_SCREENS = new Set(['orders', 'trade', 'services'])
const workKey = (moduleKey: string, screen: string) => (KIND_OF_MODULE[moduleKey] && KINDED_SCREENS.has(screen) ? `${screen}:${KIND_OF_MODULE[moduleKey]}` : screen)
const KIND_LABELS: Record<string, Record<string, string>> = {
  purchase: { orders: 'Đơn mua hàng', trade: 'Hóa đơn mua', contracts: 'Hợp đồng mua', services: 'Nghiệm thu dịch vụ mua', arap: 'Công nợ phải trả', paydiscount: 'Chiết khấu được hưởng' },
  sales: { orders: 'Đơn bán hàng', trade: 'Hóa đơn bán', contracts: 'Hợp đồng bán', services: 'Nghiệm thu dịch vụ bán', arap: 'Công nợ phải thu', paydiscount: 'Chiết khấu cho khách' },
}
const tabLabel = (moduleKey: string, screen: string) => KIND_LABELS[moduleKey]?.[screen] ?? SCREEN_LABELS[screen]

export default function App() {
  const [token, setToken] = useState(session.token)
  const [companies, setCompanies] = useState<Company[] | null>(null)
  const [companyId, setCompanyId] = useState(session.companyId)
  const [isSystemAdmin, setIsSystemAdmin] = useState(false)
  const [systemAdmin, setSystemAdmin] = useState(false)
  const [moduleKey, setModuleKey] = useState('home')
  const [tab, setTab] = useState<string>('flow')
  const [navSeq, setNavSeq] = useState(0)   // tăng khi phải dựng lại màn đang mở cho một ý định mới (Thêm nhanh)
  const tabRef = useRef(tab)
  tabRef.current = tab
  const appliedIntent = useRef('')
  const [work, setWork] = useState<Record<string, number>>({})
  const [mobile, setMobile] = useState(isMobileRoute())
  const [expired, setExpired] = useState(false)
  // A request refused as unauthenticated (session expired / revoked / server restarted) returns to the login screen instead of leaving empty lists.
  useEffect(() => { setSessionExpiredHandler(() => { setExpired(true); setToken(null); setCompanies(null); setSystemAdmin(false) }); return () => setSessionExpiredHandler(null) }, [])
  useEffect(() => { const h = () => setMobile(isMobileRoute()); window.addEventListener('hashchange', h); return () => window.removeEventListener('hashchange', h) }, [])

  useEffect(() => {
    if (!token) return
    setCompanies(null)
    get<Company[]>('/me/companies').then(setCompanies).catch(() => { session.setToken(null); setToken(null) })
    get<{ isSystemAdmin: boolean }>('/me').then(m => setIsSystemAdmin(m.isSystemAdmin)).catch(() => setIsSystemAdmin(false))
  }, [token])

  // Màn dùng chung giữa Mua hàng và Bán hàng (hợp đồng, đơn hàng, hóa đơn…): ở lại phân hệ đang mở nếu phân hệ đó có màn này,
  // không nhảy sang phân hệ đầu tiên chứa nó (trước đây "Hợp đồng bán" luôn nhảy vào Mua hàng).
  const moduleKeyRef = useRef(moduleKey)
  moduleKeyRef.current = moduleKey
  // Cùng màn đang mở: đường 'dựng lại màn' bên dưới có hộp xác nhận riêng, không hỏi hai lần.
  const sameScreenNow = (key: string) => tabRef.current === key
  useEffect(() => {
    navStore.go = (key, kind, intent) => {
      if (!(intent && sameScreenNow(key)) && !confirmLeave()) return   // đi sang màn khác khi còn nội dung nhập dở: hỏi trước
      if (key === 'home' || key === 'cycles') { setModuleKey(key); setTab('flow'); return }
      const current = MODULES.find(x => x.key === moduleKeyRef.current)
      // Có chỉ định chiều bán / mua thì vào đúng phân hệ Bán hàng / Mua hàng (màn dùng chung đọc chiều từ phân hệ đang mở).
      const wanted = kind === 'Sales' ? 'sales' : kind === 'Purchase' ? 'purchase' : undefined
      const byKind = wanted ? MODULES.find(x => x.key === wanted && x.screens.includes(key)) : undefined
      const m = byKind ?? (current?.screens.includes(key) ? current : moduleOfScreen(key))
      if (!m) return
      const intentKey = intent ? JSON.stringify(intent) : ''
      const sameScreen = m.key === moduleKeyRef.current && key === tabRef.current
      if (sameScreen && intentKey) {
        // Đã đang ở đúng màn: chỉ dựng lại khi ý định khác cái đang mở, và hỏi trước vì nội dung nhập dở chưa lưu sẽ mất.
        if (intentKey === appliedIntent.current) return
        if (!window.confirm('Mở lại màn hình này cho chức năng mới? Nội dung đang nhập chưa lưu sẽ bị bỏ.')) return
        navStore.intent = intent; navStore.intentDest = `${m.key}/${key}`; appliedIntent.current = intentKey
        setNavSeq(n => n + 1)
        return
      }
      navStore.intent = intent; navStore.intentDest = intent ? `${m.key}/${key}` : undefined; appliedIntent.current = intentKey
      setModuleKey(m.key); setTab(key)
    }
  }, [])
  // Ý định gắn với màn đích: màn dùng lazy-load nên có thể dựng SAU lần commit này, không được xóa khi còn ở đúng màn đích;
  // rời màn đích (kể cả bấm tab tay) thì bỏ, để lần mở sau không thừa hưởng ý định cũ.
  useEffect(() => {
    if (navStore.intentDest !== `${moduleKey}/${tab}`) { navStore.intent = undefined; navStore.intentDest = undefined; appliedIntent.current = '' }
  }, [moduleKey, tab, navSeq])
  // Số việc đang chờ theo phân hệ để hiện trên thanh bên.
  useEffect(() => {
    if (!token || !companyId) return
    get<{ screen: string; count: number; kind?: string | null }[]>('/cycles/home/work-queue').then(w => setWork(Object.fromEntries(w.map(x => [x.kind ? `${x.screen}:${x.kind}` : x.screen, x.count])))).catch(() => setWork({}))
  }, [token, companyId, moduleKey, tab])

  // Quyền tại công ty đang chọn (để ẩn chức năng không được phép, ví dụ Xuất Excel khi thiếu data.export).
  useEffect(() => {
    if (!token || !companyId) { setPermissions(null); return }
    let live = true
    get<string[]>('/me/permissions').then(p => { if (live) setPermissions(p) }).catch(() => { if (live) setPermissions(null) })
    return () => { live = false }
  }, [token, companyId])

  const logout = async () => {
    try { await post('/auth/logout') } catch { /* already expired */ }
    session.setToken(null); session.setCompany(null); setCompanyId(null); setCompanies(null); setSystemAdmin(false); setToken(null)
  }
  if (!token) return <Login expired={expired} onLogin={t => { session.setToken(t); setToken(t); setExpired(false) }} />
  if (companies === null) return <div className="login"><div className="panel"><span className="muted">Đang tải…</span></div></div>
  if (systemAdmin) {
    // System administration needs no company: users, grants, backups (a fresh installation has no company role yet).
    return (
      <main className="main">
        <div className="toolbar"><button onClick={() => setSystemAdmin(false)}>← Chọn công ty</button><button onClick={logout}>Đăng xuất</button></div>
        <Suspense fallback={<span className="muted">Đang tải…</span>}><AdminView /></Suspense>
      </main>
    )
  }
  const company = companies.find(c => c.id === companyId)
  if (!company) {
    return (
      <div className="login"><div className="panel">
        <h2>Chọn công ty</h2>
        {companies.length === 0 && <p className="muted">Tài khoản chưa được cấp quyền ở công ty nào{isSystemAdmin ? ' — dùng "Quản trị hệ thống" để cấp quyền.' : '.'}</p>}
        {companies.map(c => <button key={c.id} onClick={() => { session.setCompany(c.id); setCompanyId(c.id) }}>{c.code} – {c.name}{c.roles?.length ? ` (vai trò: ${c.roles.join(', ')})` : ''}</button>)}
        {isSystemAdmin && <button onClick={() => setSystemAdmin(true)}>Quản trị hệ thống (người dùng, sao lưu)</button>}
        <button onClick={logout}>Đăng xuất</button>
      </div></div>
    )
  }

  // A grant without voucher.view (e.g. only 'Administrator') cannot open the accounting screens: say so and how to fix it.
  if (company.permissions && !company.permissions.includes('voucher.view')) {
    return (
      <div className="login"><div className="panel">
        <h2>{company.code} – {company.name}</h2>
        <div className="msg error">Tài khoản này tại công ty {company.code} chỉ có vai trò: <b>{(company.roles ?? []).join(', ') || '—'}</b>, chưa được xem chứng từ và sổ sách.
          Vai trò &quot;Administrator&quot; chỉ dùng để quản trị; để làm kế toán cần cấp thêm vai trò <b>Kế toán trưởng</b> hoặc <b>Kế toán tổng hợp</b>.</div>
        <p className="muted">Cách cấp: Đổi công ty → Quản trị hệ thống → Người dùng &amp; phân quyền → Cấp vai trò tại công ty (chọn tài khoản, công ty {company.code}, vai trò Kế toán trưởng) → Cấp; sau đó đăng xuất rồi đăng nhập lại.</p>
        <button onClick={() => { session.setCompany(null); setCompanyId(null) }}>Đổi công ty</button>
        <button onClick={logout}>Đăng xuất</button>
      </div></div>
    )
  }
  if (mobile) return <MobileMaintenance company={`${company.code} – ${company.name}`} onFull={() => { window.location.hash = '' }} onLogout={logout} />
  const module = MODULES.find(m => m.key === moduleKey) ?? MODULES[0]
  navStore.kind = module.key === 'purchase' ? 'Purchase' : module.key === 'sales' ? 'Sales' : undefined   // đọc khi màn dùng chung được dựng
  const moduleCount = (m: (typeof MODULES)[number]) => m.screens.reduce((n, k) => n + (work[workKey(m.key, k)] ?? 0), 0)
  const screenEl = (key: string) => SCREENS.find(s => s.key === key)?.el()
  const openModule = (key: string) => { if (!confirmLeave()) return; setModuleKey(key); setTab('flow') }
  return (
    <div className="shell">
      <nav className="sidebar" aria-label="Phân hệ">
        <h1>HAK Accounting</h1>
        <div className="company">{company.code} – {company.name}</div>
        <QuickAdd />
        {MODULES.map(m => <button key={m.key} className={m.key === moduleKey ? 'active' : ''} onClick={() => openModule(m.key)}>
          <span className="nav-icon" aria-hidden>{m.icon}</span>{m.label}{moduleCount(m) > 0 && <span className="nav-count" aria-label={`${moduleCount(m)} việc đang chờ`}>{moduleCount(m)}</span>}</button>)}
        <button onClick={() => { session.setCompany(null); setCompanyId(null) }}>Đổi công ty</button>
        <button onClick={logout}>Đăng xuất</button>
      </nav>
      <div className="content">
        {module.screens.length > 0 && <div className="module-tabs" role="tablist" aria-label={module.label}>
          <button role="tab" aria-selected={tab === 'flow'} className={tab === 'flow' ? 'active' : ''} onClick={() => { if (confirmLeave()) setTab('flow') }}>Quy trình</button>
          {module.screens.map(k => <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'active' : ''} onClick={() => { if (confirmLeave()) setTab(k) }}>
            {tabLabel(module.key, k)}{work[workKey(module.key, k)] ? <span className="nav-count">{work[workKey(module.key, k)]}</span> : null}</button>)}
        </div>}
        <main className="main" key={company.id + module.key + tab + ':' + navSeq}>
          <Suspense fallback={<span className="muted">Đang tải…</span>}>
            {module.screens.length === 0 ? screenEl(module.key)
              : tab === 'flow' ? <FlowTab module={module} labels={SCREEN_LABELS} counts={Object.fromEntries(module.screens.filter(k => work[workKey(module.key, k)]).map(k => [k, work[workKey(module.key, k)]]))} />
              : screenEl(tab)}
          </Suspense>
        </main>
      </div>
    </div>
  )
}

function Login({ onLogin, expired = false }: { onLogin: (token: string) => void; expired?: boolean }) {
  const [userName, setUserName] = useState('')
  const [password, setPassword] = useState('')
  // null until the server says whether this is the first run — shows "đang kết nối" instead of a login form that may not apply.
  const [firstRun, setFirstRun] = useState<boolean | null>(null)
  const [statusError, setStatusError] = useState(false)
  const [fullName, setFullName] = useState('')
  const [confirm, setConfirm] = useState('')
  const { busy, error, run } = useAction()
  useEffect(() => {
    let stop = false
    const check = (attempt: number) => get<{ needsBootstrap: boolean }>('/setup/status')
      .then(s => { if (!stop) { setFirstRun(s.needsBootstrap); setStatusError(false) } })
      .catch(() => { if (stop) return; setStatusError(attempt >= 3); setTimeout(() => check(attempt + 1), 2000) })
    check(0)
    return () => { stop = true }
  }, [])
  if (firstRun === null)
    return <div className="login"><div className="panel"><h2 style={{ margin: 0 }}>HAK Accounting</h2>
      <span className="muted">{statusError ? 'Chưa kết nối được máy chủ HAK — kiểm tra cửa sổ HAK còn chạy không; trang sẽ tự thử lại.' : 'Đang kết nối máy chủ…'}</span></div></div>
  const login = async () => onLogin((await post<{ token: string }>('/auth/login', { userName, password, device: 'web' })).token)
  const submit = (e: React.FormEvent) => {
    e.preventDefault()
    run(async () => {
      if (firstRun) {
        if (password !== confirm) throw new Error('Hai lần nhập mật khẩu không khớp.')
        await post('/setup/bootstrap', { userName, fullName, password })
      }
      await login()
    })
  }
  return (
    <form className="login" onSubmit={submit}>
      <div className="panel">
        <h2 style={{ margin: 0 }}>HAK Accounting</h2>
        {firstRun
          ? <span className="muted">Lần chạy đầu: cơ sở dữ liệu chưa có người dùng. Tạo <b>quản trị hệ thống</b> đầu tiên — người này tạo công ty, người dùng và phân quyền. Ghi nhớ mật khẩu và nên tạo thêm một quản trị dự phòng; nếu quên, đặt lại tại máy chủ bằng lệnh khôi phục (docs/08).</span>
          : <span className="muted">Đăng nhập — dữ liệu chỉ lưu trên máy chủ nội bộ</span>}
        {expired && <div className="msg error" role="alert">Phiên đăng nhập đã hết hạn hoặc máy chủ đã khởi động lại. Đăng nhập lại để tiếp tục.</div>}
        <ErrorBox error={error} />
        {firstRun && <label>Họ tên<input value={fullName} onChange={e => setFullName(e.target.value)} /></label>}
        <label>Tên đăng nhập<input autoFocus value={userName} onChange={e => setUserName(e.target.value)} autoComplete="username" /></label>
        <label>Mật khẩu<input type="password" value={password} onChange={e => setPassword(e.target.value)} autoComplete={firstRun ? 'new-password' : 'current-password'} /></label>
        {firstRun && <label>Nhập lại mật khẩu<input type="password" value={confirm} onChange={e => setConfirm(e.target.value)} autoComplete="new-password" /></label>}
        <button className="primary" disabled={busy || !userName || !password || (firstRun && (!fullName || !confirm))}>{firstRun ? 'Tạo quản trị viên và đăng nhập' : 'Đăng nhập'}</button>
      </div>
    </form>
  )
}
