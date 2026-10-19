import { navStore } from './common'

/** Một nút trên sơ đồ quy trình: bấm để mở tab (màn hình) tương ứng. */
export interface FlowNode { label: string; icon: string; screen: string }
export interface ModuleLink { label: string; screen: string }
export interface AppModule {
  key: string
  label: string
  icon: string
  /** Các màn hình (khóa trong bảng màn hình của App) hiện thành tab sau tab "Quy trình". */
  screens: string[]
  title: string
  flow: FlowNode[]
  reports: ModuleLink[]
  catalogs: ModuleLink[]
}

const CAT_PARTNERS: ModuleLink = { label: 'Khách hàng, nhà cung cấp', screen: 'catalog' }
const CAT_SETUP: ModuleLink = { label: 'Tùy chọn', screen: 'setup' }

/**
 * Cấu trúc phân hệ theo mẫu giao diện chị gửi: mỗi phân hệ một mục ở thanh bên; bên trong tab "Quy trình" vẽ các nghiệp vụ nối nhau theo thứ tự các khâu
 * của chu trình (docs/10), bên phải là các báo cáo, dưới cùng là lối tắt danh mục. Màn hình cũ vẫn giữ nguyên, chỉ được xếp vào tab của phân hệ.
 */
export const MODULES: AppModule[] = [
  { key: 'home', label: 'Tổng quan', icon: '▦', screens: [], title: 'Tổng quan', flow: [], reports: [], catalogs: [] },
  { key: 'cycles', label: 'Quản lý chu trình', icon: '🔄', screens: [], title: 'Quản lý chu trình', flow: [], reports: [], catalogs: [] },
  {
    key: 'money', label: 'Tiền', icon: '💵', screens: ['cash', 'bank', 'fx', 'loans'], title: 'NGHIỆP VỤ TIỀN MẶT, TIỀN GỬI, VAY',
    flow: [
      { label: 'Thu tiền', icon: '🧾', screen: 'cash' }, { label: 'Đề nghị chi tiền (từ chấp nhận thanh toán, lương)', icon: '📝', screen: 'bank' }, { label: 'Chi tiền', icon: '💸', screen: 'cash' },
      { label: 'Lệnh thanh toán ngân hàng', icon: '🏦', screen: 'bank' }, { label: 'Đối chiếu ngân hàng', icon: '✅', screen: 'bank' }, { label: 'Thu, chi ngoại tệ', icon: '💱', screen: 'fx' }, { label: 'Vay, lịch trả nợ', icon: '🏷️', screen: 'loans' },
    ],
    reports: [{ label: 'Sổ quỹ tiền mặt, sổ tiền gửi', screen: 'ledger' }, { label: 'Dự báo dòng tiền (Tổng quan)', screen: 'home' }, { label: 'Bảng kê khế ước vay và lịch trả nợ', screen: 'loans' }, { label: 'Đối chiếu sao kê ngân hàng', screen: 'bank' }],
    catalogs: [{ label: 'Tài khoản ngân hàng', screen: 'setup' }, CAT_PARTNERS, CAT_SETUP],
  },
  {
    key: 'purchase', label: 'Mua hàng', icon: '🛒', screens: ['procurement', 'contracts', 'orders', 'trade', 'arap'], title: 'NGHIỆP VỤ MUA HÀNG',
    flow: [
      { label: 'Cần đặt hàng', icon: '📋', screen: 'procurement' }, { label: 'Hợp đồng mua', icon: '📑', screen: 'contracts' }, { label: 'Đơn mua hàng', icon: '📝', screen: 'orders' }, { label: 'Nhận hàng, kiểm nghiệm, nhập kho', icon: '📦', screen: 'procurement' },
      { label: 'Hóa đơn mua', icon: '📄', screen: 'trade' }, { label: 'Chấp nhận thanh toán', icon: '✔️', screen: 'procurement' }, { label: 'Thanh toán', icon: '💸', screen: 'procurement' }, { label: 'Công nợ phải trả', icon: '🧮', screen: 'arap' },
    ],
    reports: [{ label: 'Tiến độ, lịch thanh toán, chênh lệch giá hợp đồng', screen: 'contracts' }, { label: 'Hàng chờ thanh toán theo ngày đáo hạn', screen: 'procurement' }, { label: 'Công nợ phải trả nhà cung cấp', screen: 'arap' }, { label: 'Xếp hạng nhà cung cấp', screen: 'procurement' }, { label: 'Sổ chi tiết công nợ', screen: 'ledger' }],
    catalogs: [{ label: 'Nhà cung cấp', screen: 'catalog' }, { label: 'Vật tư, hàng hóa', screen: 'setup' }, CAT_SETUP],
  },
  {
    key: 'sales', label: 'Bán hàng', icon: '🛍️', screens: ['revenue', 'contracts', 'orders', 'trade', 'arap'], title: 'NGHIỆP VỤ BÁN HÀNG',
    flow: [
      { label: 'Hợp đồng bán', icon: '📑', screen: 'contracts' }, { label: 'Đơn bán, kiểm tra tín dụng', icon: '📝', screen: 'orders' }, { label: 'Lệnh bán hàng', icon: '📋', screen: 'revenue' }, { label: 'Giao hàng, xuất kho', icon: '🚚', screen: 'revenue' },
      { label: 'Hóa đơn bán', icon: '📄', screen: 'trade' }, { label: 'Theo dõi phải thu', icon: '🧮', screen: 'arap' }, { label: 'Thu tiền', icon: '💵', screen: 'revenue' },
    ],
    reports: [{ label: 'Tiến độ, lịch thanh toán, chênh lệch giá hợp đồng', screen: 'contracts' }, { label: 'Phân tích tiêu thụ, giá vốn, lãi gộp', screen: 'revenue' }, { label: 'Thu tiền bán hàng', screen: 'revenue' }, { label: 'Thông báo nợ quá hạn', screen: 'revenue' }, { label: 'Công nợ phải thu khách hàng', screen: 'arap' }],
    catalogs: [{ label: 'Khách hàng', screen: 'catalog' }, { label: 'Vật tư, hàng hóa', screen: 'setup' }, CAT_SETUP],
  },
  {
    key: 'stock', label: 'Kho', icon: '📦', screens: ['stock'], title: 'NGHIỆP VỤ KHO',
    flow: [
      { label: 'Nhập kho', icon: '📥', screen: 'stock' }, { label: 'Xuất kho', icon: '📤', screen: 'stock' }, { label: 'Chuyển kho', icon: '🔁', screen: 'stock' },
      { label: 'Tính giá xuất kho', icon: '🧮', screen: 'stock' }, { label: 'Kiểm kê', icon: '✅', screen: 'stock' },
    ],
    reports: [{ label: 'Sổ chi tiết vật tư hàng hóa (thẻ kho)', screen: 'stock' }, { label: 'Tổng hợp tồn kho', screen: 'stock' }, { label: 'Đối chiếu kho và sổ cái', screen: 'stock' }],
    catalogs: [{ label: 'Kho, vật tư', screen: 'setup' }, CAT_SETUP],
  },
  {
    key: 'production', label: 'Sản xuất', icon: '🏭', screens: ['production'], title: 'NGHIỆP VỤ SẢN XUẤT',
    flow: [
      { label: 'Định mức nguyên vật liệu', icon: '📐', screen: 'production' }, { label: 'Lệnh sản xuất', icon: '📋', screen: 'production' }, { label: 'Xuất nguyên vật liệu', icon: '📤', screen: 'stock' },
      { label: 'Báo công', icon: '⏱️', screen: 'production' }, { label: 'Nhập thành phẩm, giá thành', icon: '📥', screen: 'production' },
    ],
    reports: [{ label: 'Bảng giá thành lệnh sản xuất', screen: 'production' }, { label: 'So sánh thực xuất với định mức', screen: 'production' }, { label: 'Giá thành cuối kỳ theo đối tượng', screen: 'production' }],
    catalogs: [{ label: 'Định mức, tổ xưởng', screen: 'production' }, { label: 'Vật tư', screen: 'setup' }, CAT_SETUP],
  },
  {
    key: 'assets', label: 'Tài sản, CCDC', icon: '🏗️', screens: ['assets'], title: 'NGHIỆP VỤ TÀI SẢN CỐ ĐỊNH, CÔNG CỤ DỤNG CỤ',
    flow: [
      { label: 'Đề nghị mua sắm', icon: '📝', screen: 'assets' }, { label: 'Ghi tăng', icon: '➕', screen: 'assets' }, { label: 'Bàn giao, điều chuyển', icon: '🔁', screen: 'assets' },
      { label: 'Khấu hao, phân bổ', icon: '🧮', screen: 'assets' }, { label: 'Kiểm kê', icon: '✅', screen: 'assets' }, { label: 'Ghi giảm', icon: '➖', screen: 'assets' },
    ],
    reports: [{ label: 'Sổ tài sản cố định, công cụ dụng cụ', screen: 'assets' }, { label: 'Bảng khấu hao, phân bổ', screen: 'assets' }, { label: 'Đối chiếu sổ tài sản và sổ cái', screen: 'assets' }],
    catalogs: [CAT_SETUP],
  },
  {
    key: 'payroll', label: 'Tiền lương', icon: '👥', screens: ['payroll'], title: 'NGHIỆP VỤ TIỀN LƯƠNG',
    flow: [
      { label: 'Nhận bảng lương', icon: '📥', screen: 'payroll' }, { label: 'Kiểm tra gói dữ liệu', icon: '🔍', screen: 'payroll' }, { label: 'Chứng từ lương', icon: '🗂️', screen: 'vouchers' },
      { label: 'Hàng chờ trả lương', icon: '⏳', screen: 'payroll' }, { label: 'Trả lương qua ngân hàng', icon: '🏦', screen: 'bank' },
    ],
    reports: [{ label: 'Tổng hợp lương theo nhân viên', screen: 'payroll' }, { label: 'Các bảng lương đã nhập', screen: 'payroll' }],
    catalogs: [{ label: 'Nhân viên', screen: 'catalog' }, CAT_SETUP],
  },
  {
    key: 'tax', label: 'Thuế', icon: '🧮', screens: ['vat'], title: 'NGHIỆP VỤ THUẾ GTGT',
    flow: [{ label: 'Hóa đơn đầu vào, đầu ra', icon: '📄', screen: 'vat' }, { label: 'Bảng kê', icon: '📋', screen: 'vat' }, { label: 'Tờ khai thuế GTGT', icon: '🧾', screen: 'vat' }],
    reports: [{ label: 'Bảng kê hóa đơn mua vào, bán ra', screen: 'vat' }, { label: 'Tờ khai thuế GTGT', screen: 'vat' }],
    catalogs: [CAT_SETUP],
  },
  {
    key: 'ledger', label: 'Tổng hợp', icon: '📚', screens: ['vouchers', 'journal', 'ledger', 'tb', 'reports', 'periods'], title: 'NGHIỆP VỤ KẾ TOÁN TỔNG HỢP',
    flow: [
      { label: 'Chứng từ kế toán', icon: '🗂️', screen: 'vouchers' }, { label: 'Sổ Nhật ký chung', icon: '📒', screen: 'journal' }, { label: 'Sổ chi tiết, sổ cái', icon: '📚', screen: 'ledger' },
      { label: 'Cân đối phát sinh', icon: '⚖️', screen: 'tb' }, { label: 'Khóa kỳ, kết chuyển', icon: '🔒', screen: 'periods' }, { label: 'Báo cáo tài chính', icon: '📊', screen: 'reports' },
    ],
    reports: [{ label: 'Sổ Nhật ký chung', screen: 'journal' }, { label: 'Sổ chi tiết tài khoản, sổ quỹ', screen: 'ledger' }, { label: 'Bảng cân đối phát sinh', screen: 'tb' }, { label: 'Báo cáo theo mẫu (Bảng cân đối, kết quả kinh doanh, lưu chuyển tiền tệ)', screen: 'reports' }],
    catalogs: [{ label: 'Hệ thống tài khoản', screen: 'setup' }, CAT_SETUP],
  },
  {
    key: 'analysis', label: 'Phân tích tài chính', icon: '📈', screens: ['analysis', 'reports'], title: 'PHÂN TÍCH TÀI CHÍNH',
    flow: [{ label: 'Cơ cấu tài chính', icon: '🧱', screen: 'analysis' }, { label: 'Khả năng thanh toán', icon: '💧', screen: 'analysis' }, { label: 'Khả năng hoạt động', icon: '🔄', screen: 'analysis' }, { label: 'Khả năng sinh lời', icon: '📈', screen: 'analysis' }, { label: 'Báo cáo tài chính', icon: '📊', screen: 'reports' }],
    reports: [{ label: 'Các chỉ số phân tích tài chính cơ bản', screen: 'analysis' }, { label: 'Báo cáo theo mẫu (B01, B02, B03)', screen: 'reports' }], catalogs: [],
  },
  {
    key: 'opening', label: 'Số dư ban đầu', icon: '📥', screens: ['opening'], title: 'NHẬP SỐ DƯ BAN ĐẦU',
    flow: [{ label: 'Hệ thống tài khoản', icon: '📒', screen: 'opening' }, { label: 'Đối tượng, vật tư', icon: '👤', screen: 'opening' }, { label: 'Số dư tài khoản, công nợ', icon: '⚖️', screen: 'opening' }, { label: 'Tồn kho đầu kỳ', icon: '🏬', screen: 'opening' }, { label: 'TSCĐ, CCDC đầu kỳ', icon: '🏗️', screen: 'opening' }],
    reports: [{ label: 'Cân đối phát sinh sau khi nhập', screen: 'tb' }], catalogs: [],
  },
  {
    key: 'system', label: 'Danh mục, hệ thống', icon: '⚙️', screens: ['catalog', 'setup', 'admin'], title: 'DANH MỤC VÀ HỆ THỐNG',
    flow: [{ label: 'Đối tượng', icon: '👤', screen: 'catalog' }, { label: 'Thiết lập', icon: '⚙️', screen: 'setup' }, { label: 'Người dùng, quyền, sao lưu', icon: '🔐', screen: 'admin' }],
    reports: [{ label: 'Nhật ký kiểm toán', screen: 'admin' }],
    catalogs: [],
  },
]

export const moduleOfScreen = (screen: string) => MODULES.find(m => m.screens.includes(screen))

/** Tab "Quy trình": các nghiệp vụ nối nhau trên một trục, xen kẽ trên và dưới (như mẫu), báo cáo bên phải, lối tắt danh mục bên dưới. */
export function FlowTab({ module: m, labels, counts }: { module: AppModule; labels: Record<string, string>; counts: Record<string, number> }) {
  const go = (screen: string) => navStore.go?.(screen)
  const n = m.flow.length
  return (
    <div className="flow-tab">
      <div className="flow-main">
        <div className="flow-card">
          <div className="flow-title">{m.title}</div>
          <div className="flow-diagram" style={{ gridTemplateColumns: `repeat(${n}, minmax(90px, 1fr))` }}>
            <div className="flow-line" style={{ gridColumn: `1 / ${n + 1}` }} />
            {m.flow.map((node, i) => (
              <button key={i} className={`flow-node ${i % 2 === 0 ? 'up' : 'down'}`} style={{ gridColumn: i + 1, gridRow: i % 2 === 0 ? 1 : 3 }} onClick={() => go(node.screen)} title={labels[node.screen]}>
                <span className="flow-icon" aria-hidden>{node.icon}</span>
                <span className="flow-label">{node.label}</span>
              </button>))}
          </div>
        </div>
        <div className="flow-card flow-report">
          <div className="flow-title">BÁO CÁO</div>
          <ul>{m.reports.map(r => <li key={r.label}><button className="link" onClick={() => go(r.screen)}>{r.label}</button></li>)}</ul>
        </div>
      </div>
      {m.catalogs.length > 0 && <div className="flow-catalogs">{m.catalogs.map(c => <button key={c.label} className="link" onClick={() => go(c.screen)}>{c.label}</button>)}</div>}
      {Object.keys(counts).length > 0 && <div className="flow-hints">{Object.entries(counts).map(([k, v]) => <span key={k} className="muted">{labels[k]}: <b>{v}</b> việc đang chờ</span>)}</div>}
    </div>
  )
}
