import { useState } from 'react'
import { get } from './api'
import { ErrorBox, ExportButton, useAction } from './common'
import { formatDate, formatMoney, monthStart, today } from './money'

interface Line { lineNo: number; accountCode: string; partnerCode: string | null; debit: string; credit: string; description: string | null }
interface Entry { postingDate: string; voucherType: string; voucherNumber: string; description: string; amount: string; duplicate: boolean; mainJournal: string; mainJournalName: string; alsoIn: string[]; lines: Line[] }
interface Book { journal: string; name: string; entries: Entry[]; total: string; count: number; duplicateCount: number }
interface Report { fromDate: string; toDate: string; books: Book[]; journalTotal: string; booksTotal: string; balanced: boolean; duplicateEntries: number }
const J_VI: Record<string, string> = { Sales: 'Bán hàng', Purchases: 'Mua hàng', CashReceipts: 'Thu tiền', CashPayments: 'Chi tiền', General: 'Nhật ký chung' }

/**
 * Sổ nhật ký đặc biệt (bán hàng, mua hàng, thu tiền, chi tiền) và Nhật ký chung của nghiệp vụ còn lại, lập từ cùng một Nhật ký. Bút toán trùng (thuộc nhiều sổ, ví dụ rút tiền gửi nhập quỹ,
 * hoàn ứng mua vật tư kèm hoàn tiền) chỉ cộng một lần ở sổ chính; ở sổ khác hiện kèm dấu "trùng" và không cộng.
 */
export function SpecialJournalsView() {
  const [range, setRange] = useState({ from: monthStart(), to: today() })
  const [report, setReport] = useState<Report | null>(null)
  const [tab, setTab] = useState('CashReceipts')
  const { busy, error, run } = useAction()
  const book = report?.books.find(b => b.journal === tab)
  return (
    <>
      <div className="toolbar"><h2>Sổ nhật ký đặc biệt</h2>
        <label>Từ ngày<input type="date" value={range.from} onChange={e => setRange(r => ({ ...r, from: e.target.value }))} /></label>
        <label>Đến ngày<input type="date" value={range.to} onChange={e => setRange(r => ({ ...r, to: e.target.value }))} /></label>
        <button className="primary" disabled={busy} onClick={() => run(async () => setReport(await get<Report>(`/reports/special-journals?from=${range.from}&to=${range.to}`)))}>Xem</button></div>
      <ErrorBox error={error} />
      {report && <>
        <section className="panel">
          <p>Tổng Nhật ký (Nợ): <b>{formatMoney(report.journalTotal)}</b> · Tổng các sổ sau khi loại bút toán trùng: <b>{formatMoney(report.booksTotal)}</b> · {report.balanced ? <b style={{ color: 'var(--accent)' }}>Khớp</b> : <b style={{ color: 'var(--danger, #b42318)' }}>Lệch — cần kiểm tra</b>}
            {' '}· Bút toán trùng: <b>{report.duplicateEntries}</b></p>
          <p className="muted">Mỗi bút toán chỉ có một sổ chính (ưu tiên tiền mặt: thu tiền → chi tiền → bán hàng → mua hàng) và chỉ cộng vào sổ đó; ở sổ khác bút toán vẫn hiện để tra cứu, đánh dấu "trùng" và không cộng — nên Nhật ký chung dồn từ các sổ không bị cộng hai lần.</p>
        </section>
        <div className="toolbar">{report.books.map(b => <button key={b.journal} className={tab === b.journal ? 'primary' : ''} onClick={() => setTab(b.journal)}>{J_VI[b.journal]} ({b.count}{b.duplicateCount ? ` + ${b.duplicateCount} trùng` : ''})</button>)}
          {book && <ExportButton fileName={`${book.name.replace(/ /g, '_')}_${range.from}_${range.to}`} sheet={J_VI[book.journal]} title={book.name} subtitle={`Từ ngày ${formatDate(range.from)} đến ngày ${formatDate(range.to)}`}
            columns={[{ header: 'Ngày' }, { header: 'Số CT' }, { header: 'Diễn giải', width: 40 }, { header: 'TK' }, { header: 'Đối tượng' }, { header: 'Nợ', numeric: true }, { header: 'Có', numeric: true }, { header: 'Ghi chú', width: 30 }]}
            rows={book.entries.flatMap(e => e.lines.map((l, i) => [e.postingDate, e.voucherNumber, i === 0 ? e.description : '', l.accountCode, l.partnerCode, l.debit, l.credit, i === 0 && e.duplicate ? `Trùng — đã cộng ở ${e.mainJournalName}` : null]))} />}</div>
        {book && <div className="scroll"><table className="grid">
          <thead><tr><th>Ngày</th><th>Số CT</th><th>Diễn giải</th><th>TK</th><th>Đối tượng</th><th className="num">Nợ</th><th className="num">Có</th></tr></thead>
          <tbody>{book.entries.map(e => e.lines.map((l, i) => <tr key={`${e.voucherNumber}-${l.lineNo}`} style={e.duplicate ? { opacity: 0.55 } : undefined}>
            <td>{i === 0 ? formatDate(e.postingDate) : ''}</td><td>{i === 0 ? e.voucherNumber : ''}</td>
            <td>{i === 0 ? <>{e.description}{e.duplicate && <div className="muted">Bút toán trùng: đã cộng ở {e.mainJournalName}</div>}{!e.duplicate && e.alsoIn.length > 0 && <div className="muted">Cũng hiện ở: {e.alsoIn.map(j => J_VI[j]).join(', ')} (trùng, không cộng)</div>}</> : ''}</td>
            <td>{l.accountCode}</td><td>{l.partnerCode}</td><td className="num">{l.debit === '0' ? '' : formatMoney(l.debit)}</td><td className="num">{l.credit === '0' ? '' : formatMoney(l.credit)}</td></tr>))}
            {book.entries.length === 0 && <tr><td colSpan={7} className="muted">Không có bút toán.</td></tr>}</tbody>
          <tfoot><tr><td colSpan={5}>Cộng {book.name.toLowerCase()} (đã loại bút toán trùng): {book.count} bút toán</td><td className="num">{formatMoney(book.total)}</td><td className="num">{formatMoney(book.total)}</td></tr></tfoot>
        </table></div>}
      </>}
    </>
  )
}
