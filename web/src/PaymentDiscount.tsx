import { useCallback, useEffect, useState } from 'react'
import { get, post } from './api'
import { AccountSelect, ErrorBox, ExportButton, FlowStrip, Modal, useAction, useCatalog, navStore } from './common'
import { addMoney, formatDate, formatMoney, today } from './money'
import { matchesQuery } from './filter'
import { SmartFilter, useFilterState } from './SmartFilter'

interface Cand { invoiceId: string; invoiceNumber: string; invoiceSeries: string; invoiceNo: string; invoiceDate: string; partnerCode: string; partnerName: string; percent: string; days: number; deadline: string; daysLeft: number
  expired: boolean; open: string; discount: string; cashToPay: string; voucherNumber: string | null; voucherPending: boolean }

/** Chiết khấu thanh toán: hóa đơn còn trong hạn hưởng, số chiết khấu và số tiền thực trả / thực thu; lập chứng từ nháp thanh toán có chiết khấu. */
export function PaymentDiscountView() {
  const { accounts } = useCatalog()
  const [kind, setKind] = useState<'Purchase' | 'Sales'>(() => navStore.kind ?? 'Purchase')
  const [asOf, setAsOf] = useState(today())
  const [rows, setRows] = useState<Cand[]>([])
  const [f, pf, cf] = useFilterState('paydiscount')
  const [dlg, setDlg] = useState<{ c: Cand; cash: string; date: string } | null>(null)
  const [info, setInfo] = useState('')
  const { busy, error, run } = useAction()
  const load = useCallback(() => run(async () => setRows(await get<Cand[]>(`/payment-discounts?kind=${kind}&asOf=${asOf}`))), [run, kind, asOf])
  useEffect(() => { load() }, [load])
  const shown = rows.filter(r => (!f.chip || (f.chip === 'open' ? !r.expired : f.chip === 'soon' ? !r.expired && r.daysLeft <= 3 : r.expired)) && matchesQuery(`${r.invoiceNumber} ${r.invoiceSeries}/${r.invoiceNo} ${r.partnerCode} ${r.partnerName}`, Number(r.open), f.q))
  const purchase = kind === 'Purchase'
  return (
    <>
      <div className="toolbar"><h2>Chiết khấu thanh toán</h2>
        <button className={purchase ? 'primary' : ''} onClick={() => setKind('Purchase')}>Hóa đơn mua (được hưởng)</button>
        <button className={!purchase ? 'primary' : ''} onClick={() => setKind('Sales')}>Hóa đơn bán (cho khách)</button>
        <label>Tính đến ngày<input type="date" value={asOf} onChange={e => setAsOf(e.target.value)} /></label>
        <ExportButton fileName={`Chiet_khau_thanh_toan_${kind}_${asOf}`} sheet="Chiết khấu thanh toán" disabled={shown.length === 0}
          columns={[{ header: 'Hóa đơn', width: 18 }, { header: 'Ngày HĐ', width: 12 }, { header: 'Đối tượng', width: 30 }, { header: 'Hạn hưởng', width: 12 }, { header: 'Còn mở', numeric: true, width: 16 }, { header: 'Chiết khấu', numeric: true, width: 14 }, { header: purchase ? 'Thực trả' : 'Thực thu', numeric: true, width: 16 }]}
          rows={shown.map(r => [`${r.invoiceSeries}/${r.invoiceNo}`, r.invoiceDate, `${r.partnerCode} – ${r.partnerName}`, r.deadline, r.open, r.discount, r.cashToPay])} /></div>
      <FlowStrip steps={[
        { label: 'Điều khoản ghi trên hóa đơn (hoặc theo đối tượng)', journal: false, note: 'P% nếu trả đủ trong N ngày', count: `${rows.length} hóa đơn có điều khoản còn mở` },
        { label: 'Còn trong hạn hưởng', journal: false, count: `${rows.filter(r => !r.expired).length} hóa đơn · chiết khấu ${formatMoney(addMoney(rows.filter(r => !r.expired).map(r => r.discount)))}` },
        { label: purchase ? 'Chứng từ trả tiền có chiết khấu' : 'Chứng từ thu tiền có chiết khấu', journal: true, screen: 'vouchers', count: `${rows.filter(r => r.voucherPending).length} chờ ghi sổ` },
        { label: 'Quá hạn: thanh toán như thường', journal: false, count: `${rows.filter(r => r.expired).length} hóa đơn` },
      ]} />
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      <SmartFilter label="Chiết khấu thanh toán" state={f} patch={pf} clear={cf} shown={shown.length} total={rows.length} placeholder="Tìm hóa đơn, đối tượng, số tiền"
        chips={[{ key: 'open', label: 'Còn hạn', count: rows.filter(r => !r.expired).length }, { key: 'soon', label: 'Sắp hết hạn (≤ 3 ngày)', count: rows.filter(r => !r.expired && r.daysLeft <= 3).length }, { key: 'expired', label: 'Quá hạn', count: rows.filter(r => r.expired).length }]} />
      <div className="scroll"><table className="grid">
        <thead><tr><th>Hóa đơn</th><th>Đối tượng</th><th className="num">CK</th><th>Hạn hưởng</th><th className="num">Còn mở</th><th className="num">Chiết khấu</th><th className="num">{purchase ? 'Thực trả' : 'Thực thu'}</th><th /></tr></thead>
        <tbody>{shown.map(r => <tr key={r.invoiceId} style={{ opacity: r.expired ? 0.6 : undefined }}>
          <td>{r.invoiceSeries}/{r.invoiceNo}<div className="muted">{r.invoiceNumber} · {formatDate(r.invoiceDate)}</div></td><td>{r.partnerCode} – {r.partnerName}</td>
          <td className="num">{formatMoney(r.percent)}% / {r.days} ngày</td>
          <td>{formatDate(r.deadline)}<div className="muted">{r.expired ? 'đã quá hạn' : r.daysLeft === 0 ? 'hôm nay là hạn cuối' : `còn ${r.daysLeft} ngày`}</div></td>
          <td className="num">{formatMoney(r.open)}</td><td className="num"><b>{formatMoney(r.discount)}</b></td><td className="num">{formatMoney(r.cashToPay)}</td>
          <td>{r.voucherNumber ? `${r.voucherNumber}${r.voucherPending ? ' (chờ ghi sổ)' : ''}` : !r.expired && <button className="primary" disabled={busy} onClick={() => setDlg({ c: r, cash: '', date: asOf })}>{purchase ? 'Trả có chiết khấu' : 'Thu có chiết khấu'}</button>}</td></tr>)}
          {shown.length === 0 && <tr><td colSpan={8} className="muted">Không có hóa đơn có chiết khấu thanh toán đang mở. Điều khoản ghi ở hóa đơn (CK thanh toán % / trong N ngày) hoặc điền sẵn theo đối tượng ở Danh mục.</td></tr>}</tbody>
      </table></div>
      <p className="muted">Chiết khấu = P% × tiền hàng chưa thuế (theo phần còn mở); thuế GTGT giữ nguyên số trên hóa đơn [CẦN XÁC MINH cách xử lý theo quy định thuế]. Cần ánh xạ vai trò PAYMENT_DISCOUNT_INCOME (mua) / PAYMENT_DISCOUNT_EXPENSE (bán).</p>

      {dlg && <Modal title={`${purchase ? 'Trả' : 'Thu'} tiền có chiết khấu — ${dlg.c.invoiceSeries}/${dlg.c.invoiceNo}`} onClose={() => setDlg(null)} busy={busy}
        footer={<><button onClick={() => setDlg(null)}>Đóng</button><button className="primary" disabled={busy || !dlg.cash} onClick={() => run(async () => {
          const v = await post<{ number: string }>(`/payment-discounts/${dlg.c.invoiceId}/voucher`, { cashAccountId: dlg.cash, date: dlg.date })
          setDlg(null); setInfo(`Đã lập chứng từ ${v.number} (nháp) — duyệt và ghi sổ ở màn Chứng từ.`); await load()
        })}>Lập chứng từ</button></>}>
        <p>{dlg.c.partnerName}: còn mở <b>{formatMoney(dlg.c.open)}</b>, chiết khấu <b>{formatMoney(dlg.c.discount)}</b> → {purchase ? 'trả' : 'thu'} <b>{formatMoney(dlg.c.cashToPay)}</b>. Hạn hưởng {formatDate(dlg.c.deadline)}.</p>
        <div className="form-row"><label>Tài khoản tiền<AccountSelect accounts={accounts} value={dlg.cash} onChange={id => setDlg({ ...dlg, cash: id })} filter={a => a.cashKind === 'Cash' || a.cashKind === 'Bank'} /></label>
          <label>Ngày {purchase ? 'trả' : 'thu'}<input type="date" value={dlg.date} onChange={e => setDlg({ ...dlg, date: e.target.value })} /></label></div>
      </Modal>}
    </>
  )
}
