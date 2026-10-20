import { useCallback, useEffect, useState } from 'react'
import { get, post, put, type Partner } from './api'
import { AccountSelect, ErrorBox, Modal, useAction, useCatalog } from './common'

export interface PartnerBank { id: string; partnerId: string; partnerCode: string; partnerName: string; bankName: string; branch: string | null; accountNo: string; accountName: string; isDefault: boolean; isActive: boolean }
interface CompanyBank { id: string; accountId: string; accountCode: string; bankName: string; branch: string | null; accountNo: string; accountName: string; isActive: boolean }

/** Số tài khoản ngân hàng của một đối tượng (nhận tiền): thêm, đặt mặc định, ngừng dùng. */
export function PartnerBankModal({ partner, onClose }: { partner: Partner; onClose: () => void }) {
  const [list, setList] = useState<PartnerBank[]>([])
  const [f, setF] = useState({ bankName: '', branch: '', accountNo: '', accountName: partner.name.toUpperCase() })
  const { busy, error, run } = useAction()
  const load = useCallback(() => run(async () => setList(await get<PartnerBank[]>(`/bank-accounts/partners?partnerId=${partner.id}`))), [run, partner.id])
  useEffect(() => { load() }, [load])
  return (
    <Modal title={`Tài khoản ngân hàng — ${partner.code} ${partner.name}`} onClose={onClose} busy={busy}
      footer={<><button onClick={onClose}>Đóng</button><button className="primary" disabled={busy || !f.bankName || !f.accountNo || !f.accountName} onClick={() => run(async () => {
        await post(`/bank-accounts/partners/${partner.id}`, { bankName: f.bankName, branch: f.branch || null, accountNo: f.accountNo, accountName: f.accountName, isDefault: false })
        setF({ ...f, bankName: '', branch: '', accountNo: '' }); await load()
      })}>Thêm tài khoản</button></>}>
      <ErrorBox error={error} />
      <div className="scroll"><table className="grid">
        <thead><tr><th>Ngân hàng</th><th>Số tài khoản</th><th>Chủ tài khoản</th><th>Mặc định</th><th /></tr></thead>
        <tbody>{list.map(a => <tr key={a.id} style={{ opacity: a.isActive ? undefined : 0.55 }}>
          <td>{a.bankName}{a.branch ? ` – ${a.branch}` : ''}</td><td>{a.accountNo}</td><td>{a.accountName}</td>
          <td>{a.isDefault ? '✔' : a.isActive && <button disabled={busy} onClick={() => run(async () => { await put(`/bank-accounts/partners/${a.id}`, { isDefault: true }); await load() })}>Đặt mặc định</button>}</td>
          <td><button disabled={busy} onClick={() => run(async () => { await put(`/bank-accounts/partners/${a.id}`, { isActive: !a.isActive }); await load() })}>{a.isActive ? 'Ngừng dùng' : 'Dùng lại'}</button></td></tr>)}
          {list.length === 0 && <tr><td colSpan={5} className="muted">Chưa có tài khoản ngân hàng.</td></tr>}</tbody>
      </table></div>
      <div className="form-row">
        <label>Ngân hàng<input value={f.bankName} onChange={e => setF({ ...f, bankName: e.target.value })} /></label>
        <label>Chi nhánh<input value={f.branch} onChange={e => setF({ ...f, branch: e.target.value })} /></label>
        <label>Số tài khoản<input inputMode="numeric" value={f.accountNo} onChange={e => setF({ ...f, accountNo: e.target.value })} /></label>
        <label>Chủ tài khoản<input value={f.accountName} onChange={e => setF({ ...f, accountName: e.target.value })} /></label>
      </div>
    </Modal>
  )
}

/** Số tài khoản ngân hàng của công ty, gắn với TK tiền gửi trên sổ cái (dùng khi in ủy nhiệm chi, file chuyển tiền). */
export function CompanyBanks() {
  const { accounts } = useCatalog()
  const [list, setList] = useState<CompanyBank[]>([])
  const [f, setF] = useState({ accountId: '', bankName: '', branch: '', accountNo: '', accountName: '' })
  const [info, setInfo] = useState('')
  const { busy, error, run } = useAction()
  const load = useCallback(() => run(async () => setList(await get<CompanyBank[]>('/bank-accounts/company'))), [run])
  useEffect(() => { load() }, [load])
  return (
    <>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      <div className="panel form-row">
        <label>TK tiền gửi (sổ cái)<AccountSelect accounts={accounts} value={f.accountId} onChange={id => setF({ ...f, accountId: id })} filter={a => a.cashKind === 'Bank'} /></label>
        <label>Ngân hàng<input value={f.bankName} onChange={e => setF({ ...f, bankName: e.target.value })} /></label>
        <label>Chi nhánh<input value={f.branch} onChange={e => setF({ ...f, branch: e.target.value })} /></label>
        <label>Số tài khoản<input inputMode="numeric" value={f.accountNo} onChange={e => setF({ ...f, accountNo: e.target.value })} /></label>
        <label>Chủ tài khoản<input value={f.accountName} onChange={e => setF({ ...f, accountName: e.target.value })} /></label>
        <button className="primary" style={{ alignSelf: 'flex-end' }} disabled={busy || !f.accountId || !f.bankName || !f.accountNo || !f.accountName} onClick={() => run(async () => {
          await post('/bank-accounts/company', { accountId: f.accountId, bankName: f.bankName, branch: f.branch || null, accountNo: f.accountNo, accountName: f.accountName })
          setInfo('Đã thêm tài khoản ngân hàng.'); setF({ accountId: '', bankName: '', branch: '', accountNo: '', accountName: '' }); await load()
        })}>Thêm</button>
      </div>
      <div className="scroll"><table className="grid">
        <thead><tr><th>TK sổ cái</th><th>Ngân hàng</th><th>Số tài khoản</th><th>Chủ tài khoản</th><th>Đang dùng</th></tr></thead>
        <tbody>{list.map(a => <tr key={a.id}><td>{a.accountCode}</td><td>{a.bankName}{a.branch ? ` – ${a.branch}` : ''}</td><td>{a.accountNo}</td><td>{a.accountName}</td>
          <td><input type="checkbox" aria-label="Đang dùng" checked={a.isActive} disabled={busy} onChange={e => run(async () => { await put(`/bank-accounts/company/${a.id}`, { active: e.target.checked }); await load() })} /></td></tr>)}
          {list.length === 0 && <tr><td colSpan={5} className="muted">Chưa khai báo số tài khoản ngân hàng của công ty.</td></tr>}</tbody>
      </table></div>
    </>
  )
}
