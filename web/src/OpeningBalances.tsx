import { useState } from 'react'
import { ImportData } from './ImportData'

const TILES: { kind: string; icon: string; title: string; desc: string }[] = [
  { kind: 'accounts', icon: '📒', title: 'Hệ thống tài khoản', desc: 'Nhập danh mục tài khoản từ Excel' },
  { kind: 'partners', icon: '👤', title: 'Khách hàng, nhà cung cấp, nhân viên', desc: 'Đối tượng và công nợ theo dõi' },
  { kind: 'items', icon: '📦', title: 'Vật tư, hàng hóa', desc: 'Danh mục vật tư' },
  { kind: 'opening', icon: '⚖️', title: 'Số dư tài khoản, công nợ', desc: 'Số dư đầu kỳ theo tài khoản và đối tượng' },
  { kind: 'stock', icon: '🏬', title: 'Tồn kho vật tư, hàng hóa', desc: 'Tồn kho đầu kỳ theo kho' },
  { kind: 'assets', icon: '🏗️', title: 'TSCĐ, CCDC đang sử dụng đầu kỳ', desc: 'Thẻ tài sản chuyển từ hệ thống cũ' },
  { kind: 'vouchers', icon: '🗂️', title: 'Chứng từ chuyển dữ liệu', desc: 'Chuyển chứng từ từ phần mềm cũ' },
]

/** Nhập số dư ban đầu: lưới thẻ theo từng loại dữ liệu, bấm vào thẻ để đọc tệp Excel/CSV, kiểm tra rồi nhập. */
export function OpeningBalancesView() {
  const [kind, setKind] = useState<string | null>(null)
  if (kind) return (
    <>
      <div className="toolbar"><button onClick={() => setKind(null)}>← Số dư ban đầu</button><h2>{TILES.find(t => t.kind === kind)?.title}</h2></div>
      <ImportData key={kind} initialKind={kind} />
    </>
  )
  return (
    <>
      <h2 style={{ marginTop: 0 }}>Nhập số dư ban đầu</h2>
      <div className="card-grid">
        {TILES.map(t => <button key={t.kind} className="card-tile" onClick={() => setKind(t.kind)}>
          <span className="flow-icon" aria-hidden>{t.icon}</span><span className="card-title">{t.title}</span><span className="muted">{t.desc}</span></button>)}
      </div>
    </>
  )
}
