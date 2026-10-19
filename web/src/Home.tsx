import type { ReactNode } from 'react'
import { Bank, BookOpenText, Calculator, CalendarCheck, ChartBar, Check, CircleHalf, Coins, CurrencyCircleDollar, Desktop, FileText, GearSix, HandCoins, ListBullets, Money, Moon, Palette, Plus, Receipt, ShieldCheck, ShoppingCart, SquaresFour, Sun, UsersThree, Warehouse } from '@phosphor-icons/react'
import type { Icon as PhosphorIcon, IconWeight } from '@phosphor-icons/react'

export interface Tile { label: string; screen: string; icon: string }
export interface ProcessSpec { title: string; tiles: Tile[]; reports: { label: string; screen: string }[]; shortcuts: { label: string; screen: string; icon: string }[] }

/** Biểu tượng hai tông (duotone) của Phosphor Icons (MIT), đóng gói sẵn trong bản cài — không cần mạng. Tên cũ giữ nguyên để các màn không phải đổi. */
const ICONS: Record<string, { C: PhosphorIcon; weight?: IconWeight }> = {
  grid: { C: SquaresFour }, cash: { C: Money }, bank: { C: Bank }, cart: { C: ShoppingCart }, stock: { C: Warehouse }, calc: { C: Calculator },
  asset: { C: Desktop }, money: { C: Coins }, tax: { C: Receipt }, book: { C: BookOpenText }, chart: { C: ChartBar }, list: { C: ListBullets },
  gear: { C: GearSix }, doc: { C: FileText }, swap: { C: CurrencyCircleDollar }, loan: { C: HandCoins }, people: { C: UsersThree },
  calendar: { C: CalendarCheck }, shield: { C: ShieldCheck }, plus: { C: Plus, weight: 'bold' }, sun: { C: Sun }, moon: { C: Moon },
  auto: { C: CircleHalf }, palette: { C: Palette }, check: { C: Check, weight: 'bold' },
}
export function Icon({ name, size = 20 }: { name: string; size?: number }) {
  const { C, weight = 'duotone' } = ICONS[name] ?? ICONS.doc
  return <C size={size} weight={weight} aria-hidden className={`ic ic-${name}`} />
}

/** MISA-style "Quy trình" page: process tiles on the left, report list on the right, shortcut strip underneath. */
export function ProcessView({ spec, go }: { spec: ProcessSpec; go: (screen: string) => void }) {
  return (
    <div className="process">
      <div className={spec.reports.length > 0 ? 'process-top' : 'process-top solo'}>
        <section className="card">
          <h3 className="card-title">{spec.title}</h3>
          <div className="tiles">
            {spec.tiles.map(t => (
              <button key={t.screen} className="tile" onClick={() => go(t.screen)}>
                <span className={`tile-icon ic-${t.icon}`}><Icon name={t.icon} size={36} /></span>
                <span>{t.label}</span>
              </button>
            ))}
          </div>
        </section>
        {spec.reports.length > 0 && <section className="card report-card">
          <h3 className="card-title">BÁO CÁO</h3>
          <ul className="report-list">
            {spec.reports.map(r => <li key={r.screen + r.label}><button className="link" onClick={() => go(r.screen)}>{r.label}</button></li>)}
          </ul>
          <button className="link all-reports" onClick={() => go('reports')}>Tất cả báo cáo</button>
        </section>}
      </div>
      {spec.shortcuts.length > 0 && <section className="card shortcuts">
        {spec.shortcuts.map(s => (
          <button key={s.label} className="shortcut" onClick={() => go(s.screen)}><Icon name={s.icon} size={18} /><span>{s.label}</span></button>
        ))}
      </section>}
    </div>
  )
}

/** "Tổng quan": entry points only. Figures need server endpoints that do not exist yet, so none are invented here. */
export function OverviewView({ company, modules, go, hidden = [], roles = [], children }: { company: string; modules: { key: string; label: string; icon: string; items: string[] }[]; go: (module: string) => void;
  hidden?: { label: string; needs: string[] }[]; roles?: string[]; children?: ReactNode }) {
  const hints: Record<string, string> = {
    general: 'Chứng từ, kỳ kế toán, sổ nhật ký', cash: 'Phiếu thu, phiếu chi, sổ quỹ', bank: 'Sao kê, lệnh thanh toán, vay, ngoại tệ',
    trade: 'Đơn hàng, hóa đơn, công nợ', stock: 'Nhập xuất tồn, tính giá xuất, giá thành', assets: 'Thẻ tài sản, khấu hao, phân bổ',
    payroll: 'Nhận bảng lương, định khoản', cost: 'Đối tượng giá thành, tính giá thành kỳ', tax: 'Thuế GTGT, đối chiếu 3 nguồn', reports: 'Cân đối phát sinh, sổ sách, BCTC theo mẫu',
    catalog: 'Đối tượng, tài khoản, thiết lập', system: 'Người dùng, vai trò, sao lưu, nhật ký',
  }
  return (
    <div className="overview">
      <section className="card welcome"><h3>Chào mừng đến {company}</h3><p className="muted">Chọn phân hệ ở thanh bên trái hoặc đi nhanh tới các nghiệp vụ thường dùng.</p></section>
      <h2 className="page-title">Tổng quan</h2>
      <div className="overview-grid">
        {modules.map(m => (
          <button key={m.key} className="card ov-card" onClick={() => go(m.key)}>
            <span className={`tile-icon ic-${m.icon}`}><Icon name={m.icon} size={36} /></span>
            <b>{m.label}</b><span className="muted">{hints[m.key] ?? ''}</span>
          </button>
        ))}
      </div>
      {hidden.length > 0 && (
        <section className="card" style={{ marginTop: 16 }}>
          <h3 style={{ margin: '0 0 6px' }}>Phân hệ chưa hiện</h3>
          <p className="muted" style={{ margin: 0 }}>Tài khoản đang giữ vai trò <b>{roles.join(', ') || '—'}</b> nên chưa thấy: {hidden.map(h => h.label).join(', ')}.
            Muốn dùng, nhờ quản trị viên cấp thêm chức năng (Hệ thống → Quản trị → Vai trò & chức năng, rồi cấp vai trò tại công ty).</p>
        </section>
      )}
      {children}
    </div>
  )
}
