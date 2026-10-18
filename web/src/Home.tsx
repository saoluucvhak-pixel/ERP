import type { ReactNode } from 'react'

export interface Tile { label: string; screen: string; icon: string }
export interface ProcessSpec { title: string; tiles: Tile[]; reports: { label: string; screen: string }[]; shortcuts: { label: string; screen: string; icon: string }[] }

const ICONS: Record<string, string> = {
  grid: 'M4 4h6v6H4zM14 4h6v6h-6zM4 14h6v6H4zM14 14h6v6h-6z',
  cash: 'M3 7h18v10H3zM12 9.5a2.5 2.5 0 1 0 0 5 2.5 2.5 0 0 0 0-5z',
  bank: 'M3 10l9-6 9 6M5 10v8M9.5 10v8M14.5 10v8M19 10v8M3 20h18',
  cart: 'M5 7h14l-1.5 11h-11zM9 7V5a3 3 0 0 1 6 0v2',
  stock: 'M3 9l9-6 9 6v11H3zM8 20v-6h8v6',
  asset: 'M3 17h13l5-4v4M6 17a2 2 0 1 0 4 0M13 17a2 2 0 1 0 4 0M5 13V8h8v5',
  money: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM12 7v10M9.5 9.5c0-1 1-1.5 2.5-1.5s2.5.5 2.5 1.5-1 1.3-2.5 1.5-2.5.6-2.5 1.5 1 1.5 2.5 1.5 2.5-.5 2.5-1.5',
  tax: 'M6 3h9l4 4v14H6zM9 12h6M9 16h6M15 3v4h4',
  book: 'M5 4h12a2 2 0 0 1 2 2v14H7a2 2 0 0 1-2-2zM5 18a2 2 0 0 1 2-2h12',
  chart: 'M4 20V4M4 20h16M8 16v-4M12 16V8M16 16v-6',
  list: 'M8 6h12M8 12h12M8 18h12M4 6h.01M4 12h.01M4 18h.01',
  gear: 'M12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6zM19 12l2-1-2-4-2 .7-1.7-1L15 4h-4l-.3 2.7-1.7 1-2-.7-2 4 2 1v2l-2 1 2 4 2-.7 1.7 1L11 20h4l.3-2.7 1.7-1 2 .7 2-4-2-1z',
  doc: 'M6 3h9l4 4v14H6zM15 3v4h4M9 12h6M9 16h4',
  swap: 'M7 7h13l-3-3M17 17H4l3 3',
  plus: 'M12 5v14M5 12h14',
}
export function Icon({ name, size = 20 }: { name: string; size?: number }) {
  return <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden><path d={ICONS[name] ?? ICONS.doc} /></svg>
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
                <span className="tile-icon"><Icon name={t.icon} size={30} /></span>
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
export function OverviewView({ company, modules, go, children }: { company: string; modules: { key: string; label: string; icon: string; items: string[] }[]; go: (module: string) => void; children?: ReactNode }) {
  const hints: Record<string, string> = {
    general: 'Chứng từ, kỳ kế toán, sổ nhật ký', cash: 'Phiếu thu, phiếu chi, sổ quỹ', bank: 'Sao kê, lệnh thanh toán, vay, ngoại tệ',
    trade: 'Đơn hàng, hóa đơn, công nợ', stock: 'Nhập xuất tồn, tính giá xuất, giá thành', assets: 'Thẻ tài sản, khấu hao, phân bổ',
    payroll: 'Nhận bảng lương, định khoản', tax: 'Thuế GTGT, đối chiếu 3 nguồn', reports: 'Cân đối phát sinh, sổ sách, BCTC theo mẫu',
    catalog: 'Đối tượng, tài khoản, thiết lập', system: 'Người dùng, vai trò, sao lưu, nhật ký',
  }
  return (
    <div className="overview">
      <section className="card welcome"><h3>Chào mừng đến {company}</h3><p className="muted">Chọn phân hệ ở thanh bên trái hoặc đi nhanh tới các nghiệp vụ thường dùng.</p></section>
      <h2 className="page-title">Tổng quan</h2>
      <div className="overview-grid">
        {modules.map(m => (
          <button key={m.key} className="card ov-card" onClick={() => go(m.key)}>
            <span className="tile-icon"><Icon name={m.icon} size={30} /></span>
            <b>{m.label}</b><span className="muted">{hints[m.key] ?? ''}</span>
          </button>
        ))}
      </div>
      {children}
    </div>
  )
}
