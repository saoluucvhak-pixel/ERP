import { useEffect, useState } from 'react'
import { get } from './api'
import { navStore } from './common'

interface WorkItem { screen: string; count: number; hint: string }
/** `screen` và `kind`: ô riêng cho một chiều của màn dùng chung (đơn / hóa đơn bán và mua); không có thì mở màn mang khóa `key`. */
export interface HomeScreen { key: string; group: string; label: string; screen?: string; kind?: 'Sales' | 'Purchase' }

/** Biểu tượng và mô tả ngắn của từng phân hệ trên màn hình chính (kiểu ô ứng dụng). Phân hệ không có trong bảng vẫn hiện, chỉ không có biểu tượng riêng. */
const TILE: Record<string, { icon: string; color: string; desc: string }> = {
  orders: { icon: '📝', color: '#0b5cad', desc: 'Đơn bán, đơn mua; kiểm tra tín dụng và tồn kho' },
  revenue: { icon: '🚚', color: '#1a7f37', desc: 'Lệnh bán, giao hàng, hóa đơn bán, thu tiền' },
  arap: { icon: '🧾', color: '#9a6700', desc: 'Công nợ phải thu, phải trả, phân bổ, đối chiếu' },
  procurement: { icon: '🛒', color: '#7a3db8', desc: 'Cần đặt, nhận hàng, kiểm nghiệm, chấp nhận thanh toán' },
  trade: { icon: '📄', color: '#0b7a8a', desc: 'Hóa đơn mua, hóa đơn bán, hóa đơn điện tử' },
  'orders:Sales': { icon: '📝', color: '#0b5cad', desc: 'Đơn bán hàng; kiểm tra tín dụng, giữ hàng theo kho' },
  'orders:Purchase': { icon: '📝', color: '#7a4b0b', desc: 'Đơn mua hàng; theo dõi nhận hàng và kiểm nghiệm' },
  'trade:Sales': { icon: '🧾', color: '#0b5cad', desc: 'Hóa đơn bán hàng, hóa đơn điều chỉnh' },
  'trade:Purchase': { icon: '🧾', color: '#7a4b0b', desc: 'Hóa đơn mua hàng; đối chiếu nhiều bên' },
  stock: { icon: '📦', color: '#b4531a', desc: 'Phiếu nhập, xuất, chuyển kho, kiểm kê, giá xuất' },
  production: { icon: '🏭', color: '#6b4f3a', desc: 'Định mức, lệnh sản xuất, báo công, giá thành' },
  payroll: { icon: '👥', color: '#c23b6b', desc: 'Nhận bảng lương, hàng chờ trả lương' },
  cash: { icon: '💵', color: '#1a7f37', desc: 'Phiếu thu, phiếu chi, báo có, báo nợ' },
  advances: { icon: '🧳', color: '#1a7f37', desc: 'Đề nghị, chi, quyết toán, hoàn ứng, tuổi nợ' },
  cashcount: { icon: '🪙', color: '#1a7f37', desc: 'Đếm quỹ theo mệnh giá, chênh lệch, biên bản' },
  groups: { icon: '🗂️', color: '#5f6b7a', desc: 'Nhóm đối tượng, vật tư, tài sản, kho nhiều cấp; báo cáo theo nhóm' },
  paydiscount: { icon: '🏷️', color: '#9a6700', desc: 'Trả / thu tiền trong hạn hưởng chiết khấu' },
  specialjournals: { icon: '📒', color: '#16263d', desc: 'Nhật ký bán hàng, mua hàng, thu tiền, chi tiền; xử lý bút toán trùng' },
  services: { icon: '🧾', color: '#9a6700', desc: 'Dịch vụ mua, bán: nghiệm thu rồi lập hóa đơn' },
  salesapproval: { icon: '✅', color: '#9a6700', desc: 'Duyệt trả hàng, giảm giá, xóa nợ khó đòi' },
  equity: { icon: '🏛️', color: '#16263d', desc: 'Chủ sở hữu, góp / rút vốn, chia cổ tức, thuế TNCN' },
  bank: { icon: '🏦', color: '#0b5cad', desc: 'Lệnh thanh toán, sao kê, đối chiếu ngân hàng' },
  fx: { icon: '💱', color: '#0b7a8a', desc: 'Tỷ giá, đánh giá lại, thu chi ngoại tệ' },
  loans: { icon: '🏷️', color: '#9a6700', desc: 'Khoản vay, lịch trả nợ, lãi vay' },
  vat: { icon: '🧮', color: '#7a3db8', desc: 'Bảng kê, tờ khai thuế GTGT' },
  assets: { icon: '🏗️', color: '#6b4f3a', desc: 'Mua sắm, bàn giao, khấu hao, kiểm kê, ghi giảm' },
  vouchers: { icon: '🗂️', color: '#16263d', desc: 'Chứng từ kế toán, duyệt, ghi sổ, đảo' },
  journal: { icon: '📒', color: '#16263d', desc: 'Sổ ghi theo thời gian' },
  ledger: { icon: '📚', color: '#16263d', desc: 'Sổ cái, sổ chi tiết, sổ quỹ' },
  tb: { icon: '⚖️', color: '#16263d', desc: 'Số dư đầu kỳ, phát sinh, số dư cuối kỳ' },
  reports: { icon: '📊', color: '#0b5cad', desc: 'Bảng cân đối, kết quả kinh doanh, lưu chuyển tiền tệ' },
  catalog: { icon: '👤', color: '#5f6b7a', desc: 'Khách hàng, nhà cung cấp, nhân viên' },
  setup: { icon: '⚙️', color: '#5f6b7a', desc: 'Tài khoản, vật tư, kho, năm tài chính' },
  periods: { icon: '📅', color: '#5f6b7a', desc: 'Khóa kỳ, kết chuyển cuối kỳ' },
  admin: { icon: '🔐', color: '#5f6b7a', desc: 'Người dùng, quyền, sao lưu, nhật ký kiểm toán' },
}

/** Màn hình chính dạng ô ứng dụng: mỗi chu trình một khối, mỗi phân hệ một ô kèm số việc đang chờ (không lưu, tính từ chứng từ nguồn). */
export function HomeLauncher({ screens }: { screens: readonly HomeScreen[] }) {
  const [work, setWork] = useState<Record<string, WorkItem>>({})
  useEffect(() => {
    get<WorkItem[]>('/cycles/home/work-queue').then(w => setWork(Object.fromEntries(w.map(x => [x.screen, x])))).catch(() => setWork({}))
  }, [])
  const groups = [...new Set(screens.filter(s => s.group).map(s => s.group))]
  const total = Object.values(work).reduce((n, w) => n + w.count, 0)
  return (
    <section aria-label="Ứng dụng">
      <h2 style={{ marginTop: 0 }}>Việc cần làm {total > 0 && <span className="badge-count">{total}</span>}</h2>
      {total === 0 && <p className="muted">Không có việc nào đang chờ ở các phân hệ bạn được xem.</p>}
      {groups.map(g => (
        <div key={g} className="app-group">
          <div className="app-group-title">{g}</div>
          <div className="app-grid">
            {screens.filter(s => s.group === g).map(s => {
              const target = s.screen ?? s.key
              const t = TILE[s.key] ?? TILE[target]; const w = s.kind ? undefined : work[target]
              return (
                <button key={s.key} className="app-tile" onClick={() => navStore.go?.(target, s.kind)} title={w?.hint}>
                  <span className="app-icon" style={{ background: t?.color ?? '#5f6b7a' }} aria-hidden>{t?.icon ?? '▪'}</span>
                  <span className="app-body">
                    <span className="app-name">{s.label}</span>
                    <span className="app-desc">{t?.desc ?? ''}</span>
                    {w && <span className="app-hint">{w.hint}</span>}
                  </span>
                  {w && <span className="badge-count" aria-label={`${w.count} việc đang chờ`}>{w.count}</span>}
                </button>
              )
            })}
          </div>
        </div>))}
    </section>
  )
}
