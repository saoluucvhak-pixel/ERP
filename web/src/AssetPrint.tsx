import { useEffect, useState } from 'react'
import { get } from './api'
import { PrintSheet, PrintedAt, printSheet, useCompanyName } from './common'
import { formatDate, formatMoney, addMoney, amountInWords, today } from './money'

export interface PrintCountLine { code: string; name: string; bookDepartment: string | null; result: 'NotCounted' | 'Found' | 'Missing' | 'Damaged'; actualDepartment: string | null; note: string | null; netBookValue: string }
export interface PrintCount { number: string; countDate: string; note: string | null; status: 'Draft' | 'Completed' | 'Cancelled'; total: number; counted: number; missing: number; damaged: number; moved: number; missingNetBookValue: string; lines: PrintCountLine[] }
const RESULT = { NotCounted: 'Chưa kiểm', Found: 'Có, đúng sổ', Missing: 'Không thấy', Damaged: 'Hỏng' } as const

function usePrintOnce(on: boolean, done: () => void) {
  useEffect(() => { if (!on) return; const t = setTimeout(() => { printSheet(); done() }, 150); return () => clearTimeout(t) }, [on, done])
}

/** Biên bản kiểm kê tài sản cố định, công cụ dụng cụ (mẫu 05-TSCĐ theo chế độ kế toán [CẦN XÁC MINH]): số theo sổ, kết quả kiểm, chênh lệch, ý kiến, chữ ký. */
export function CountPrintButton({ count }: { count: PrintCount }) {
  const [on, setOn] = useState(false)
  const company = useCompanyName()
  usePrintOnce(on, () => setOn(false))
  const missing = count.lines.filter(l => l.result === 'Missing'), damaged = count.lines.filter(l => l.result === 'Damaged'), moved = count.lines.filter(l => l.result === 'Found' && l.actualDepartment && l.actualDepartment !== l.bookDepartment)
  return <>
    <button onClick={() => setOn(true)}>In biên bản kiểm kê</button>
    {on && <PrintSheet>
      <div><b>{company}</b></div>
      <h1>BIÊN BẢN KIỂM KÊ TÀI SẢN CỐ ĐỊNH, CÔNG CỤ DỤNG CỤ</h1>
      <div className="center">Số: <b>{count.number}</b> · Thời điểm kiểm kê: ngày {formatDate(count.countDate)}{count.status !== 'Completed' ? ` · (${count.status === 'Draft' ? 'CHƯA CHỐT' : 'ĐÃ HỦY'})` : ''}</div>
      <p>Ban kiểm kê: ………………………………………………………………………………</p>
      <table><thead><tr><th>STT</th><th>Mã</th><th>Tên tài sản</th><th>Bộ phận theo sổ</th><th>Kết quả kiểm</th><th>Bộ phận thực tế</th><th>Giá trị còn lại theo sổ</th><th>Ghi chú</th></tr></thead>
        <tbody>{count.lines.map((l, i) => <tr key={l.code}><td className="center">{i + 1}</td><td>{l.code}</td><td>{l.name}</td><td>{l.bookDepartment ?? ''}</td><td>{RESULT[l.result]}</td><td>{l.actualDepartment ?? ''}</td><td className="num">{formatMoney(l.netBookValue)}</td><td>{l.note ?? ''}</td></tr>)}</tbody>
        <tfoot><tr><td colSpan={6}><b>Tổng giá trị còn lại theo sổ ({count.lines.length} tài sản)</b></td><td className="num"><b>{formatMoney(addMoney(count.lines.map(l => l.netBookValue)))}</b></td><td /></tr></tfoot></table>
      <p>Kết quả: đã kiểm {count.counted}/{count.total}; không thấy <b>{missing.length}</b> (giá trị còn lại {formatMoney(count.missingNetBookValue)}); hỏng <b>{damaged.length}</b>; khác bộ phận theo sổ <b>{moved.length}</b>.</p>
      {count.missingNetBookValue !== '0' && <p>Giá trị còn lại tài sản không thấy bằng chữ: <i>{amountInWords(count.missingNetBookValue)}</i></p>}
      <p>Ý kiến giải quyết số chênh lệch: {count.note ?? '…………………………………………………………………………'}</p>
      <div className="signs"><div><b>Thủ kho / người giữ tài sản</b><br /><i>(Ký, họ tên)</i></div><div><b>Kế toán</b><br /><i>(Ký, họ tên)</i></div><div><b>Trưởng ban kiểm kê</b><br /><i>(Ký, họ tên)</i></div><div><b>Giám đốc</b><br /><i>(Ký, họ tên, đóng dấu)</i></div></div>
      <div className="foot">Mẫu in nội bộ HAK Accounting — chưa đối chiếu với mẫu biên bản kiểm kê TSCĐ theo chế độ kế toán [CẦN XÁC MINH]. In lúc <PrintedAt />.</div>
    </PrintSheet>}
  </>
}

interface Hist { department: string; custodian: string | null; location: string | null; startDate: string; endDate: string | null; note: string | null }
interface Reg { id: string; code: string; name: string; originalCost: string; accumulated: string; netBookValue: string; inServiceDate: string; usefulLifeMonths: number }

/** Biên bản giao nhận tài sản cố định, công cụ dụng cụ (mẫu 01-TSCĐ [CẦN XÁC MINH]): bên giao (bộ phận / người giữ trước), bên nhận, nguyên giá, giá trị còn lại. */
export function HandoverPrintButton({ assetId, code, name }: { assetId: string; code: string; name: string }) {
  const [data, setData] = useState<{ hist: Hist[]; reg: Reg | null } | null>(null)
  const company = useCompanyName()
  usePrintOnce(!!data, () => setData(null))
  const load = async () => {
    const hist = await get<Hist[]>(`/assets/${assetId}/assignments`).catch(() => [])
    const reg = (await get<Reg[]>(`/assets?asOf=${today()}`).catch(() => [])).find(r => r.id === assetId) ?? null
    setData({ hist, reg })
  }
  const cur = data?.hist.find(h => h.endDate == null) ?? data?.hist[0]
  const prev = data ? data.hist.find(h => h !== cur) : undefined
  return <>
    <button onClick={load} title="In biên bản giao nhận tài sản theo lần bàn giao hiện tại">In biên bản bàn giao</button>
    {data && cur && <PrintSheet>
      <div><b>{company}</b></div>
      <h1>BIÊN BẢN GIAO NHẬN TÀI SẢN CỐ ĐỊNH, CÔNG CỤ DỤNG CỤ</h1>
      <div className="center">Ngày {formatDate(cur.startDate)}</div>
      <p><b>Bên giao:</b> {prev ? `${prev.department}${prev.custodian ? ` – ${prev.custodian}` : ''}` : 'Phòng kế toán / quản lý tài sản'}</p>
      <p><b>Bên nhận:</b> {cur.department}{cur.custodian ? ` – ${cur.custodian}` : ''}{cur.location ? ` · vị trí: ${cur.location}` : ''}</p>
      <table><thead><tr><th>STT</th><th>Mã</th><th>Tên tài sản</th><th>Ngày đưa vào sử dụng</th><th>Nguyên giá</th><th>Hao mòn lũy kế</th><th>Giá trị còn lại</th></tr></thead>
        <tbody><tr><td className="center">1</td><td>{code}</td><td>{name}</td><td>{data.reg ? formatDate(data.reg.inServiceDate) : ''}</td><td className="num">{formatMoney(data.reg?.originalCost)}</td><td className="num">{formatMoney(data.reg?.accumulated)}</td><td className="num">{formatMoney(data.reg?.netBookValue)}</td></tr></tbody></table>
      <p>Tình trạng tài sản khi bàn giao: {cur.note ?? '…………………………………………………………………………'}</p>
      <p className="muted">Bên nhận chịu trách nhiệm giữ gìn, sử dụng đúng mục đích và báo ngay khi hỏng, mất.</p>
      <div className="signs"><div><b>Bên giao</b><br /><i>(Ký, họ tên)</i></div><div><b>Bên nhận</b><br /><i>(Ký, họ tên)</i></div><div><b>Kế toán trưởng</b><br /><i>(Ký, họ tên)</i></div><div><b>Giám đốc</b><br /><i>(Ký, họ tên, đóng dấu)</i></div></div>
      <div className="foot">Mẫu in nội bộ HAK Accounting — chưa đối chiếu với mẫu biên bản giao nhận TSCĐ theo chế độ kế toán [CẦN XÁC MINH]. In lúc <PrintedAt />.</div>
    </PrintSheet>}
  </>
}
