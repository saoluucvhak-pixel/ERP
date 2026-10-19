import type { ReactNode } from 'react'

export interface Tile { label: string; screen: string; icon: string }
export interface ProcessSpec { title: string; tiles: Tile[]; reports: { label: string; screen: string }[]; shortcuts: { label: string; screen: string; icon: string }[] }

/** Biểu tượng hai lớp kiểu MISA AMIS: nền màu nhạt (fill) + nét viền bo tròn (line). Mỗi biểu tượng vẽ trên lưới 24×24. */
const ICONS: Record<string, { fill?: string; line: string }> = {
  grid: { fill: 'M4 4h6.5v6.5H4zM13.5 4H20v6.5h-6.5zM4 13.5h6.5V20H4zM13.5 13.5H20V20h-6.5z', line: 'M4 4h6.5v6.5H4zM13.5 4H20v6.5h-6.5zM4 13.5h6.5V20H4zM13.5 13.5H20V20h-6.5z' },
  cash: { fill: 'M2.5 6.5h19v11h-19z', line: 'M2.5 6.5h19v11h-19zM12 9.5a2.5 2.5 0 1 0 0 5 2.5 2.5 0 0 0 0-5zM5.5 9.5v.01M18.5 14.5v.01' },
  bank: { fill: 'M3 9.5L12 4l9 5.5z', line: 'M3 9.5L12 4l9 5.5H3zM5.5 12v6M9.5 12v6M14.5 12v6M18.5 12v6M3 20.5h18' },
  cart: { fill: 'M6 8h14l-1.6 8H7.4z', line: 'M2.5 4h2.6l2.2 11h10.4l1.8-8H6M9.5 19.5v.01M17 19.5v.01' },
  stock: { fill: 'M3 8.5L12 4l9 4.5V20H3z', line: 'M3 8.5L12 4l9 4.5V20H3zM8 20v-6h8v6M8 17h8' },
  calc: { fill: 'M5 3h14v18H5z', line: 'M5 3h14v18H5zM8 6.5h8v3.5H8zM8 13.5v.01M12 13.5v.01M16 13.5v.01M8 17v.01M12 17v.01M16 17v.01' },
  asset: { fill: 'M3 5h18v11H3z', line: 'M3 5h18v11H3zM8 20h8M12 16v4' },
  money: { fill: 'M12 3.5a8.5 8.5 0 1 0 0 17 8.5 8.5 0 0 0 0-17z', line: 'M12 3.5a8.5 8.5 0 1 0 0 17 8.5 8.5 0 0 0 0-17zM14.8 9.2c-.4-.9-1.4-1.4-2.8-1.4-1.7 0-2.8.8-2.8 2 0 3 5.8 1.3 5.8 4.3 0 1.2-1.1 2-2.9 2-1.4 0-2.5-.5-2.9-1.5M12 6.3v1.5M12 16.2v1.5' },
  tax: { fill: 'M6 3h12v18l-3-2-3 2-3-2-3 2z', line: 'M6 3h12v18l-3-2-3 2-3-2-3 2zM9.5 14.5l5-5M9.7 9.7v.01M14.3 14.3v.01' },
  book: { fill: 'M5 4h12a2 2 0 0 1 2 2v14H7a2 2 0 0 1-2-2z', line: 'M5 4h12a2 2 0 0 1 2 2v14H7a2 2 0 0 1-2-2zM5 18a2 2 0 0 1 2-2h12M9 8h6' },
  chart: { fill: 'M4 12h4v8H4zM10 6h4v14h-4zM16 9h4v11h-4z', line: 'M4 12h4v8H4zM10 6h4v14h-4zM16 9h4v11h-4zM3 20.5h18' },
  list: { fill: 'M3.5 4.5h17v15h-17z', line: 'M3.5 4.5h17v15h-17zM8 9h9M8 12.5h9M8 16h5' },
  gear: { fill: 'M12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6z', line: 'M12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6zM19 12l2-1-2-4-2 .7-1.7-1L15 4h-4l-.3 2.7-1.7 1-2-.7-2 4 2 1v2l-2 1 2 4 2-.7 1.7 1L11 20h4l.3-2.7 1.7-1 2 .7 2-4-2-1z' },
  doc: { fill: 'M6 3h8l5 5v13H6z', line: 'M6 3h8l5 5v13H6zM14 3v5h5M9 13h7M9 17h5' },
  swap: { fill: 'M12 3.5a8.5 8.5 0 1 0 0 17 8.5 8.5 0 0 0 0-17z', line: 'M12 3.5a8.5 8.5 0 1 0 0 17 8.5 8.5 0 0 0 0-17zM3.5 12h17M12 3.5c2.5 2.3 3.8 5.2 3.8 8.5s-1.3 6.2-3.8 8.5c-2.5-2.3-3.8-5.2-3.8-8.5S9.5 5.8 12 3.5z' },
  loan: { fill: 'M4 15h16v5H4z', line: 'M12 3v10M8 9l4 4 4-4M4 15v5h16v-5' },
  people: { fill: 'M9 4a3.5 3.5 0 1 0 0 7 3.5 3.5 0 0 0 0-7z', line: 'M9 4a3.5 3.5 0 1 0 0 7 3.5 3.5 0 0 0 0-7zM2.5 20c0-3.6 2.9-6 6.5-6s6.5 2.4 6.5 6M16 4.5a3.2 3.2 0 0 1 0 6.2M18 14.3c2 .8 3.5 2.6 3.5 5.7' },
  calendar: { fill: 'M3.5 6h17v14.5h-17z', line: 'M3.5 6h17v14.5h-17zM3.5 10.5h17M8 3.5v4M16 3.5v4' },
  shield: { fill: 'M12 3l7.5 3v5.5c0 4.6-3.2 8.3-7.5 9.5-4.3-1.2-7.5-4.9-7.5-9.5V6z', line: 'M12 3l7.5 3v5.5c0 4.6-3.2 8.3-7.5 9.5-4.3-1.2-7.5-4.9-7.5-9.5V6zM8.8 12l2.3 2.3 4.2-4.6' },
  plus: { line: 'M12 5v14M5 12h14' },
  sun: { fill: 'M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8z', line: 'M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8zM12 2.5v2M12 19.5v2M2.5 12h2M19.5 12h2M5.3 5.3l1.4 1.4M17.3 17.3l1.4 1.4M5.3 18.7l1.4-1.4M17.3 6.7l1.4-1.4' },
  moon: { fill: 'M20 14.5A8.5 8.5 0 0 1 9.5 4a8.5 8.5 0 1 0 10.5 10.5z', line: 'M20 14.5A8.5 8.5 0 0 1 9.5 4a8.5 8.5 0 1 0 10.5 10.5z' },
  auto: { fill: 'M12 3.5a8.5 8.5 0 0 0 0 17z', line: 'M12 3.5a8.5 8.5 0 1 0 0 17 8.5 8.5 0 0 0 0-17zM12 3.5v17' },
  palette: { fill: 'M12 3.5a8.5 8.5 0 1 0 0 17c1.4 0 2-.9 1.6-2-.4-1.2.3-2.2 1.6-2.2H17a3.5 3.5 0 0 0 3.5-3.5C20.5 7 16.7 3.5 12 3.5z', line: 'M12 3.5a8.5 8.5 0 1 0 0 17c1.4 0 2-.9 1.6-2-.4-1.2.3-2.2 1.6-2.2H17a3.5 3.5 0 0 0 3.5-3.5C20.5 7 16.7 3.5 12 3.5zM7.5 11.5v.01M10 7.8v.01M14.2 7.8v.01' },
  check: { line: 'M5 12.5l4.5 4.5L19 7.5' },
}
export function Icon({ name, size = 20 }: { name: string; size?: number }) {
  const ic = ICONS[name] ?? ICONS.doc
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" aria-hidden className={`ic ic-${name}`}>
      {ic.fill && <path d={ic.fill} fill="currentColor" fillOpacity=".18" stroke="none" />}
      <path d={ic.line} stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
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
                <span className={`tile-icon ic-${t.icon}`}><Icon name={t.icon} size={30} /></span>
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
            <span className={`tile-icon ic-${m.icon}`}><Icon name={m.icon} size={30} /></span>
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
