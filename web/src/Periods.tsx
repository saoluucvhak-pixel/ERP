import { useCallback, useEffect, useState } from 'react'
import { formatDate, formatMoney } from './money'
import { get, post, type Period, type Voucher } from './api'
import { ErrorBox, navStore, useAction } from './common'

interface CheckItem { code: string; severity: string; title: string; count: number; amount: string | null; detail: string; screen: string }
interface Checklist { periodNo: number; canLock: boolean; items: CheckItem[] }
const SEV_VI: Record<string, string> = { Blocking: 'Lỗi chặn', Confirm: 'Cần xác nhận', CarryOver: 'Chuyển kỳ hợp lệ', Ok: 'Đạt' }
const SEV_COLOR: Record<string, string> = { Blocking: 'var(--danger)', Confirm: 'var(--warn, #b7791f)', CarryOver: 'var(--muted, #666)', Ok: 'var(--ok, #2f855a)' }

export function PeriodsView() {
  const [periods, setPeriods] = useState<Period[]>([])
  const [info, setInfo] = useState('')
  const [check, setCheck] = useState<Checklist | null>(null)
  const { busy, error, run } = useAction()
  const load = useCallback(() => run(async () => setPeriods(await get<Period[]>('/periods'))), [run])
  useEffect(() => { load() }, [load])

  const act = (fn: () => Promise<unknown>, msg: string) => run(async () => { await fn(); setInfo(msg); await load() })

  return (
    <>
      <div className="toolbar"><h2>Kỳ kế toán, khóa sổ và kết chuyển</h2><button onClick={() => load()}>Tải lại</button></div>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      {check && (
        <div className="panel">
          <div className="toolbar"><strong>Bảng kiểm đóng kỳ {check.periodNo}</strong>
            <span style={{ color: check.canLock ? 'var(--ok, #2f855a)' : 'var(--danger)' }}>{check.canLock ? 'Không còn lỗi chặn — khóa được' : 'Còn lỗi chặn — chưa khóa được'}</span>
            <button onClick={() => setCheck(null)}>Đóng</button></div>
          <table className="grid">
            <thead><tr><th>Mức</th><th>Mục kiểm</th><th className="num">Số</th><th className="num">Giá trị</th><th>Chi tiết</th><th /></tr></thead>
            <tbody>{[...check.items].sort((a, b) => ['Blocking', 'Confirm', 'CarryOver', 'Ok'].indexOf(a.severity) - ['Blocking', 'Confirm', 'CarryOver', 'Ok'].indexOf(b.severity)).map(i => (
              <tr key={i.code}><td style={{ color: SEV_COLOR[i.severity], fontWeight: 600 }}>{SEV_VI[i.severity] ?? i.severity}</td><td>{i.title}</td>
                <td className="num">{i.count}</td><td className="num">{i.amount != null ? formatMoney(i.amount) : ''}</td><td>{i.detail}</td>
                <td>{i.severity !== 'Ok' && <button onClick={() => navStore.go?.(i.screen)}>Mở</button>}</td></tr>))}
            </tbody>
          </table>
        </div>)}
      <table className="grid">
        <thead><tr><th>Kỳ</th><th>Từ</th><th>Đến</th><th>Trạng thái</th><th>Thao tác</th></tr></thead>
        <tbody>{periods.map(p => (
          <tr key={p.id}>
            <td>{p.periodNo}</td><td>{formatDate(p.startDate)}</td><td>{formatDate(p.endDate)}</td>
            <td><span className={`badge ${p.status}`}>{p.status === 'Open' ? 'Mở' : 'Đã khóa'}</span></td>
            <td style={{ display: 'flex', gap: 6 }}>
              {p.status === 'Open' && <>
                <button disabled={busy} onClick={() => act(async () => {
                  const v = await post<Voucher>('/closing/generate', { periodId: p.id })
                  setInfo(`Đã tạo chứng từ kết chuyển ${v.number} (Nháp) — duyệt và ghi sổ ở màn hình Chứng từ.`)
                }, 'Đã tạo chứng từ kết chuyển (Nháp).')}>Tạo bút toán kết chuyển</button>
                <button disabled={busy} onClick={() => run(async () => setCheck(await get<Checklist>(`/periods/${p.id}/close-checklist`)))}>Bảng kiểm đóng kỳ</button>
                <button disabled={busy} onClick={() => confirm(`Khóa sổ kỳ ${p.periodNo} theo bảng kiểm? (từ chối nếu còn lỗi chặn)`) && act(async () => { await post(`/periods/${p.id}/lock-checked`); setCheck(null) }, `Đã khóa kỳ ${p.periodNo}.`)}>Khóa sổ</button>
              </>}
              {p.status === 'Locked' && <button disabled={busy} onClick={() => {
                const reason = prompt('Lý do mở lại kỳ (bắt buộc, được ghi nhật ký):')
                if (reason) act(() => post(`/periods/${p.id}/reopen`, { reason }), `Đã mở lại kỳ ${p.periodNo}.`)
              }}>Mở lại</button>}
            </td>
          </tr>))}
        </tbody>
      </table>
    </>
  )
}
