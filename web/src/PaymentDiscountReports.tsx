import { useCallback, useEffect, useState } from 'react'
import { get } from './api'
import { ErrorBox, useAction } from './common'
import { formatDate, formatMoney, today } from './money'

interface Missed { invoiceId: string; invoiceNumber: string; invoiceNo: string; invoiceDate: string; partnerCode: string; partnerName: string; terms: string; lastDeadline: string; daysOver: number; open: string; lostDiscount: string }
interface Suspect { invoiceId: string; invoiceNumber: string; invoiceNo: string; partnerCode: string; partnerName: string; originalAmount: string; allocated: string; open: string; openPercent: string }
interface Days { month: string; closingBalance: string; turnover: string; daysInMonth: number; days: string }
interface Effect { rows: { voucherNumber: string; postingDate: string; partnerCode: string; partnerName: string; settled: string; discount: string; cash: string; daysEarly: number | null; annualRatePercent: string | null }[]; totalDiscount: string; totalCash: string; weightedAnnualRatePercent: string | null }

/** Báo cáo chiết khấu thanh toán: đã mất, khoản dư nghi khấu trừ sai, DSO/DPO theo tháng, hiệu quả dòng tiền. */
export function PaymentDiscountReports({ kind }: { kind: 'Purchase' | 'Sales' }) {
  const purchase = kind === 'Purchase'
  const [asOf, setAsOf] = useState(today())
  const [from, setFrom] = useState(`${today().slice(0, 4)}-01-01`)
  const [to, setTo] = useState(today())
  const [missed, setMissed] = useState<Missed[]>([])
  const [suspect, setSuspect] = useState<Suspect[]>([])
  const [days, setDays] = useState<Days[]>([])
  const [effect, setEffect] = useState<Effect | null>(null)
  const { error, run } = useAction()
  const load = useCallback(() => run(async () => {
    setMissed(await get<Missed[]>(`/payment-discounts/reports/missed?kind=${kind}&asOf=${asOf}`))
    setSuspect(await get<Suspect[]>(`/payment-discounts/reports/suspect?kind=${kind}&asOf=${asOf}`))
    setDays(await get<Days[]>(`/payment-discounts/reports/days?kind=${kind}&from=${from}&to=${to}`))
    setEffect(await get<Effect>(`/payment-discounts/reports/effect?kind=${kind}&from=${from}&to=${to}`))
  }), [run, kind, asOf, from, to])
  useEffect(() => { load() }, [load])
  return (
    <section>
      <div className="toolbar"><h3>Báo cáo chiết khấu thanh toán</h3>
        <label>Tính đến ngày<input type="date" value={asOf} onChange={e => setAsOf(e.target.value)} /></label>
        <label>Từ<input type="date" value={from} onChange={e => setFrom(e.target.value)} /></label>
        <label>Đến<input type="date" value={to} onChange={e => setTo(e.target.value)} /></label></div>
      <ErrorBox error={error} />
      <h4>{purchase ? 'Chiết khấu được hưởng đã mất (quá hạn, còn nợ nhà cung cấp)' : 'Chiết khấu khách đã mất quyền hưởng (quá hạn, còn phải thu)'}</h4>
      <div className="scroll"><table className="grid"><thead><tr><th>Hóa đơn</th><th>Đối tượng</th><th>Điều khoản</th><th>Hạn cuối</th><th className="num">Quá (ngày)</th><th className="num">Còn nợ</th><th className="num">Chiết khấu đã mất</th></tr></thead>
        <tbody>{missed.map(r => <tr key={r.invoiceId}><td>{r.invoiceNo}<div className="muted">{r.invoiceNumber} · {formatDate(r.invoiceDate)}</div></td><td>{r.partnerCode} – {r.partnerName}</td><td>{r.terms}</td><td>{formatDate(r.lastDeadline)}</td><td className="num">{r.daysOver}</td><td className="num">{formatMoney(r.open)}</td><td className="num"><b>{formatMoney(r.lostDiscount)}</b></td></tr>)}
          {missed.length === 0 && <tr><td colSpan={7} className="muted">Không có.</td></tr>}</tbody></table></div>
      <h4>{purchase ? 'Khoản trả thiếu còn dư nhỏ cho nhà cung cấp' : 'Khách trả thiếu còn dư nhỏ — nghi tự khấu trừ'} (≤ 5% giá trị) <span className="muted">— chỉ gợi ý đối chiếu, không tự coi là chiết khấu</span></h4>
      <div className="scroll"><table className="grid"><thead><tr><th>Hóa đơn</th><th>Đối tượng</th><th className="num">Giá trị</th><th className="num">Đã thanh toán</th><th className="num">Còn dư</th><th className="num">%</th></tr></thead>
        <tbody>{suspect.map(r => <tr key={r.invoiceId}><td>{r.invoiceNo}<div className="muted">{r.invoiceNumber}</div></td><td>{r.partnerCode} – {r.partnerName}</td><td className="num">{formatMoney(r.originalAmount)}</td><td className="num">{formatMoney(r.allocated)}</td><td className="num"><b>{formatMoney(r.open)}</b></td><td className="num">{formatMoney(r.openPercent)}%</td></tr>)}
          {suspect.length === 0 && <tr><td colSpan={6} className="muted">Không có.</td></tr>}</tbody></table></div>
      <h4>{purchase ? 'DPO — số ngày phải trả theo tháng' : 'DSO — số ngày phải thu theo tháng'}</h4>
      <div className="scroll"><table className="grid"><thead><tr><th>Tháng</th><th className="num">Số dư cuối tháng</th><th className="num">Ghi hóa đơn trong tháng (gồm thuế)</th><th className="num">{purchase ? 'DPO' : 'DSO'} (ngày)</th></tr></thead>
        <tbody>{days.map(r => <tr key={r.month}><td>{r.month}</td><td className="num">{formatMoney(r.closingBalance)}</td><td className="num">{formatMoney(r.turnover)}</td><td className="num"><b>{r.days}</b></td></tr>)}</tbody></table></div>
      <h4>Hiệu quả dòng tiền của chiết khấu đã ghi sổ</h4>
      {effect && <><div className="scroll"><table className="grid"><thead><tr><th>Chứng từ</th><th>Ngày</th><th>Đối tượng</th><th className="num">Công nợ tất toán</th><th className="num">Chiết khấu</th><th className="num">Tiền thực</th><th className="num">Sớm hơn hạn (ngày)</th><th className="num">Chi phí tín dụng quy năm</th></tr></thead>
        <tbody>{effect.rows.map(r => <tr key={r.voucherNumber}><td>{r.voucherNumber}</td><td>{formatDate(r.postingDate)}</td><td>{r.partnerCode} {r.partnerName}</td><td className="num">{formatMoney(r.settled)}</td><td className="num">{formatMoney(r.discount)}</td><td className="num">{formatMoney(r.cash)}</td><td className="num">{r.daysEarly ?? ''}</td><td className="num">{r.annualRatePercent ? `${formatMoney(r.annualRatePercent)}%` : ''}</td></tr>)}
          {effect.rows.length === 0 && <tr><td colSpan={8} className="muted">Chưa có chiết khấu ghi sổ trong kỳ.</td></tr>}</tbody></table></div>
        <p className="muted">Tổng chiết khấu {formatMoney(effect.totalDiscount)} trên tiền thực {formatMoney(effect.totalCash)}{effect.weightedAnnualRatePercent ? `; chi phí tín dụng quy năm bình quân gia quyền ${formatMoney(effect.weightedAnnualRatePercent)}% (so với lãi vay của doanh nghiệp)` : ''}. Hạn thanh toán thông thường lấy từ ngày đáo hạn của hóa đơn.</p></>}
    </section>
  )
}
