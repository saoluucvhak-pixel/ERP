import { useCallback, useEffect, useState } from 'react'
import { get, post } from './api'
import { AccountSelect, ErrorBox, ExportButton, FlowStrip, Modal, PartnerSelect, useAction, useCatalog, navStore } from './common'
import { addMoney, formatDate, formatMoney, today } from './money'
import { matchesQuery } from './filter'
import { SmartFilter, useFilterState } from './SmartFilter'
import { PaymentDiscountReports } from './PaymentDiscountReports'

interface OpenRow { journalLineId: string; voucherNumber: string; postingDate: string; open: string; description: string | null }
interface Cand { invoiceId: string; invoiceNumber: string; invoiceSeries: string; invoiceNo: string; invoiceDate: string; partnerCode: string; partnerName: string; percent: string; days: number; deadline: string; daysLeft: number
  expired: boolean; open: string; discount: string; cashToPay: string; voucherNumber: string | null; voucherPending: boolean; partnerId?: string; termsText?: string | null }

/** Chiết khấu thanh toán: hóa đơn còn trong hạn hưởng, số chiết khấu và số tiền thực trả / thực thu; lập chứng từ nháp thanh toán có chiết khấu. */
export function PaymentDiscountView() {
  const { accounts, partners } = useCatalog()
  const [kind] = useState<'Purchase' | 'Sales'>(() => navStore.kind ?? 'Purchase')   // chiều do phân hệ đang mở quyết định
  const [asOf, setAsOf] = useState(today())
  const [rows, setRows] = useState<Cand[]>([])
  const [f, pf, cf] = useFilterState('paydiscount')
  const [dlg, setDlg] = useState<{ c: Cand; cash: string; date: string } | null>(null)
  const [info, setInfo] = useState('')
  const [showReports, setShowReports] = useState(false)
  const [picked, setPicked] = useState<string[]>([])                       // hóa đơn chọn để thanh toán gộp một chứng từ
  const [multi, setMulti] = useState<{ ids: string[]; partner: string; cash: string; date: string } | null>(null)
  // Chiết khấu thanh toán độc lập: không xuất hóa đơn nên không ghép với hóa đơn — chọn khoản công nợ còn mở của đối tượng.
  const [solo, setSolo] = useState<{ partner: string; items: OpenRow[]; item: string; cash: string; date: string; discount: string } | null>(null)
  const { busy, error, run } = useAction()
  const load = useCallback(() => run(async () => setRows(await get<Cand[]>(`/payment-discounts?kind=${kind}&asOf=${asOf}`))), [run, kind, asOf])
  useEffect(() => { load() }, [load])
  const shown = rows.filter(r => (!f.chip || (f.chip === 'open' ? !r.expired : f.chip === 'soon' ? !r.expired && r.daysLeft <= 3 : r.expired)) && matchesQuery(`${r.invoiceNumber} ${r.invoiceSeries}/${r.invoiceNo} ${r.partnerCode} ${r.partnerName}`, Number(r.open), f.q))
  const purchase = kind === 'Purchase'
  return (
    <>
      <div className="toolbar"><h2>{purchase ? 'Chiết khấu thanh toán được hưởng (hóa đơn mua)' : 'Chiết khấu thanh toán cho khách (hóa đơn bán)'}</h2>
        <label>Tính đến ngày<input type="date" value={asOf} onChange={e => setAsOf(e.target.value)} /></label>
        <button onClick={() => setShowReports(x => !x)}>{showReports ? 'Ẩn báo cáo' : 'Báo cáo'}</button>
        <button disabled={picked.length < 2} title="Chọn từ 2 hóa đơn của cùng một đối tượng; chiết khấu tính riêng từng hóa đơn" onClick={() => {
          const sel = rows.filter(r => picked.includes(r.invoiceId))
          if (new Set(sel.map(r => r.partnerCode)).size !== 1) { setInfo('Chỉ gộp các hóa đơn của cùng một đối tượng.'); return }
          setMulti({ ids: picked, partner: sel[0].partnerId ?? '', cash: '', date: asOf })
        }}>Thanh toán gộp ({picked.length})…</button>
        <button onClick={() => setSolo({ partner: '', items: [], item: '', cash: '', date: asOf, discount: '' })}>Chiết khấu không theo hóa đơn…</button>
        <ExportButton fileName={`Chiet_khau_thanh_toan_${kind}_${asOf}`} sheet="Chiết khấu thanh toán" disabled={shown.length === 0}
          columns={[{ header: 'Hóa đơn', width: 18 }, { header: 'Ngày HĐ', width: 12 }, { header: 'Đối tượng', width: 30 }, { header: 'Hạn hưởng', width: 12 }, { header: 'Còn mở', numeric: true, width: 16 }, { header: 'Chiết khấu', numeric: true, width: 14 }, { header: purchase ? 'Thực trả' : 'Thực thu', numeric: true, width: 16 }]}
          rows={shown.map(r => [`${r.invoiceSeries}/${r.invoiceNo}`, r.invoiceDate, `${r.partnerCode} – ${r.partnerName}`, r.deadline, r.open, r.discount, r.cashToPay])} /></div>
      <FlowStrip steps={[
        { label: 'Điều khoản ghi trên hóa đơn (hoặc theo đối tượng)', journal: false, note: 'P% nếu trả đủ trong N ngày', count: `${rows.length} hóa đơn có điều khoản còn mở` },
        { label: 'Còn trong hạn hưởng', journal: false, count: `${rows.filter(r => !r.expired).length} hóa đơn · chiết khấu ${formatMoney(addMoney(rows.filter(r => !r.expired).map(r => r.discount)))}` },
        { label: purchase ? 'Chứng từ trả tiền có chiết khấu' : 'Chứng từ thu tiền có chiết khấu', journal: true, screen: 'vouchers', count: `${rows.filter(r => r.voucherPending).length} chờ ghi sổ` },
        { label: 'Quá hạn: thanh toán như thường', journal: false, count: `${rows.filter(r => r.expired).length} hóa đơn` },
      ]} />
      {showReports && <PaymentDiscountReports kind={kind} />}
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      <SmartFilter label="Chiết khấu thanh toán" state={f} patch={pf} clear={cf} shown={shown.length} total={rows.length} placeholder="Tìm hóa đơn, đối tượng, số tiền"
        chips={[{ key: 'open', label: 'Còn hạn', count: rows.filter(r => !r.expired).length }, { key: 'soon', label: 'Sắp hết hạn (≤ 3 ngày)', count: rows.filter(r => !r.expired && r.daysLeft <= 3).length }, { key: 'expired', label: 'Quá hạn', count: rows.filter(r => r.expired).length }]} />
      <div className="scroll"><table className="grid">
        <thead><tr><th /><th>Hóa đơn</th><th>Đối tượng</th><th className="num">CK</th><th>Hạn hưởng</th><th className="num">Còn mở</th><th className="num">Chiết khấu</th><th className="num">{purchase ? 'Thực trả' : 'Thực thu'}</th><th /></tr></thead>
        <tbody>{shown.map(r => <tr key={r.invoiceId} style={{ opacity: r.expired ? 0.6 : undefined }}>
          <td><input type="checkbox" aria-label="Chọn thanh toán gộp" disabled={!!r.voucherNumber} checked={picked.includes(r.invoiceId)} onChange={e => setPicked(p => e.target.checked ? [...p, r.invoiceId] : p.filter(x => x !== r.invoiceId))} /></td>
          <td>{r.invoiceSeries}/{r.invoiceNo}<div className="muted">{r.invoiceNumber} · {formatDate(r.invoiceDate)}</div></td><td>{r.partnerCode} – {r.partnerName}</td>
          <td className="num" title={r.termsText ?? undefined}>{formatMoney(r.percent)}% / {r.days} ngày{r.termsText && r.termsText.includes(',') ? <div className="muted">{r.termsText}</div> : null}</td>
          <td>{formatDate(r.deadline)}<div className="muted">{r.expired ? 'đã quá hạn' : r.daysLeft === 0 ? 'hôm nay là hạn cuối' : `còn ${r.daysLeft} ngày`}</div></td>
          <td className="num">{formatMoney(r.open)}</td><td className="num"><b>{formatMoney(r.discount)}</b></td><td className="num">{formatMoney(r.cashToPay)}</td>
          <td>{r.voucherNumber ? `${r.voucherNumber}${r.voucherPending ? ' (chờ ghi sổ)' : ''}` : !r.expired && <button className="primary" disabled={busy} onClick={() => setDlg({ c: r, cash: '', date: asOf })}>{purchase ? 'Trả có chiết khấu' : 'Thu có chiết khấu'}</button>}</td></tr>)}
          {shown.length === 0 && <tr><td colSpan={9} className="muted">Không có hóa đơn có chiết khấu thanh toán đang mở. Điều khoản ghi ở hóa đơn (CK thanh toán % / trong N ngày) hoặc điền sẵn theo đối tượng ở Danh mục.</td></tr>}</tbody>
      </table></div>
      <p className="muted">Chiết khấu = P% × tiền hàng chưa thuế (theo phần còn mở); thuế GTGT giữ nguyên số trên hóa đơn [CẦN XÁC MINH cách xử lý theo quy định thuế]. Cần ánh xạ vai trò PAYMENT_DISCOUNT_INCOME (mua) / PAYMENT_DISCOUNT_EXPENSE (bán).</p>

      {multi && <Modal title={`${purchase ? 'Trả' : 'Thu'} tiền gộp ${multi.ids.length} hóa đơn`} onClose={() => setMulti(null)} busy={busy}
        footer={<><button onClick={() => setMulti(null)}>Đóng</button><button className="primary" disabled={busy || !multi.cash} onClick={() => run(async () => {
          const v = await post<{ number: string }>('/payment-discounts/multi', { kind, partnerId: multi.partner, invoiceIds: multi.ids, cashAccountId: multi.cash, date: multi.date })
          setMulti(null); setPicked([]); setInfo(`Đã lập chứng từ ${v.number} (nháp) — duyệt và ghi sổ ở màn Chứng từ.`); await load()
        })}>Lập chứng từ</button></>}>
        <p>Một chứng từ cho {multi.ids.length} hóa đơn: mỗi hóa đơn bù trừ trọn khoản còn mở, chiết khấu tính riêng theo điều khoản và hạn của từng hóa đơn (hóa đơn quá hạn chỉ bù trừ).</p>
        <div className="form-row"><label>Tài khoản tiền<AccountSelect accounts={accounts} value={multi.cash} onChange={id => setMulti({ ...multi, cash: id })} filter={a => a.cashKind === 'Cash' || a.cashKind === 'Bank'} /></label>
          <label>Ngày tiền về ngân hàng<input type="date" value={multi.date} onChange={e => setMulti({ ...multi, date: e.target.value })} /></label></div>
      </Modal>}

      {solo && <Modal title={`${purchase ? 'Trả' : 'Thu'} tiền có chiết khấu — không theo hóa đơn`} onClose={() => setSolo(null)} busy={busy}
        footer={<><button onClick={() => setSolo(null)}>Đóng</button><button className="primary" disabled={busy || !solo.item || !solo.cash || !solo.discount} onClick={() => run(async () => {
          const v = await post<{ number: string }>('/payment-discounts/standalone', { kind, partnerId: solo.partner, settleJournalLineId: solo.item, cashAccountId: solo.cash, date: solo.date, discount: solo.discount })
          setSolo(null); setInfo(`Đã lập chứng từ ${v.number} (nháp) — duyệt và ghi sổ ở màn Chứng từ.`)
        })}>Lập chứng từ</button></>}>
        <p>Chiết khấu thanh toán không xuất hóa đơn nên không ghép với hóa đơn: chỉ là một dòng trong bút toán thanh toán riêng, bù trừ khoản công nợ còn mở bạn chọn.</p>
        <div className="form-row"><label>Đối tượng<PartnerSelect partners={partners} value={solo.partner} onChange={id => run(async () => {
          const items = id ? await get<OpenRow[]>(`/payment-discounts/open-items?kind=${kind}&partnerId=${id}&asOf=${solo.date}`) : []
          setSolo({ ...solo, partner: id, items, item: '' })
        })} /></label></div>
        <div className="form-row"><label>Khoản công nợ còn mở<select value={solo.item} onChange={e => setSolo({ ...solo, item: e.target.value })}><option value="">— chọn —</option>
          {solo.items.map(o => <option key={o.journalLineId} value={o.journalLineId}>{o.voucherNumber} · {formatDate(o.postingDate)} · còn {formatMoney(o.open)}</option>)}</select></label>
          <label>Chiết khấu (đồng)<input inputMode="numeric" value={solo.discount} onChange={e => setSolo({ ...solo, discount: e.target.value.replace(/[^0-9]/g, '') })} /></label></div>
        <div className="form-row"><label>Tài khoản tiền<AccountSelect accounts={accounts} value={solo.cash} onChange={id => setSolo({ ...solo, cash: id })} filter={a => a.cashKind === 'Cash' || a.cashKind === 'Bank'} /></label>
          <label>Ngày<input type="date" value={solo.date} onChange={e => setSolo({ ...solo, date: e.target.value })} /></label></div>
      </Modal>}

      {dlg && <Modal title={`${purchase ? 'Trả' : 'Thu'} tiền có chiết khấu — ${dlg.c.invoiceSeries}/${dlg.c.invoiceNo}`} onClose={() => setDlg(null)} busy={busy}
        footer={<><button onClick={() => setDlg(null)}>Đóng</button><button className="primary" disabled={busy || !dlg.cash} onClick={() => run(async () => {
          const v = await post<{ number: string }>(`/payment-discounts/${dlg.c.invoiceId}/voucher`, { cashAccountId: dlg.cash, date: dlg.date })
          setDlg(null); setInfo(`Đã lập chứng từ ${v.number} (nháp) — duyệt và ghi sổ ở màn Chứng từ.`); await load()
        })}>Lập chứng từ</button></>}>
        <p>{dlg.c.partnerName}: còn mở <b>{formatMoney(dlg.c.open)}</b>, chiết khấu <b>{formatMoney(dlg.c.discount)}</b> → {purchase ? 'trả' : 'thu'} <b>{formatMoney(dlg.c.cashToPay)}</b>. Hạn hưởng {formatDate(dlg.c.deadline)}.</p>
        <div className="form-row"><label>Tài khoản tiền<AccountSelect accounts={accounts} value={dlg.cash} onChange={id => setDlg({ ...dlg, cash: id })} filter={a => a.cashKind === 'Cash' || a.cashKind === 'Bank'} /></label>
          <label>Ngày tiền {purchase ? 'chuyển đi' : 'về'} ngân hàng<input type="date" value={dlg.date} onChange={e => setDlg({ ...dlg, date: e.target.value })} /></label></div>
      </Modal>}
    </>
  )
}
