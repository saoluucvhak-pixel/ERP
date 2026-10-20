import { useCallback, useEffect, useState } from 'react'
import { get, put, type Account } from './api'
import { AccountSelect, ErrorBox, useAction } from './common'
import { SmartFilter, useFilterState } from './SmartFilter'
import { addMoney, formatMoney, parseMoney } from './money'

interface SplitView { expenseAccountId: string; accountCode: string; accountName: string; label: string | null; percent: string }
interface Row { expenseAccountId: string; accountCode: string; accountName: string; label: string | null; amount: string }
interface Draft { accountId: string; label: string; percent: string }

/** Tổng tỷ lệ bằng chuỗi/BigInt-an toàn: tính theo đơn vị 1/10000 %, không dùng float. */
function sumPercent(rows: Draft[]): { total: bigint; bad: boolean } {
  let total = 0n; let bad = false
  for (const r of rows) {
    const p = parseMoney(r.percent)
    if (p == null) { bad = true; continue }
    const [i, f = ''] = p.split('.')
    total += BigInt(i) * 10000n + BigInt(f.padEnd(4, '0').slice(0, 4))
  }
  return { total, bad }
}

/** Chia chi phí khấu hao / phân bổ của một thẻ (ví dụ bảo hiểm, tiền thuê nhà xưởng trả trước) cho nhiều tài khoản chi phí, bộ phận. Chỉ áp dụng từ kỳ chạy sau. */
export function ExpenseSplitEditor({ assetId, assetLabel, accounts, onClose }: { assetId: string; assetLabel: string; accounts: Account[]; onClose: () => void }) {
  const [rows, setRows] = useState<Draft[]>([])
  const [info, setInfo] = useState('')
  const { busy, error, run, setError } = useAction()
  const load = useCallback(() => run(async () => {
    const s = await get<SplitView[]>(`/assets/${assetId}/expense-splits`)
    setRows(s.map(x => ({ accountId: x.expenseAccountId, label: x.label ?? '', percent: formatMoney(x.percent) })))
  }), [run, assetId])
  useEffect(() => { load() }, [load])
  const set = (i: number, p: Partial<Draft>) => setRows(rs => rs.map((r, k) => (k === i ? { ...r, ...p } : r)))
  const { total, bad } = sumPercent(rows)
  const ok = rows.length === 0 || (!bad && total === 1000000n)
  const sumText = `${total / 10000n}${total % 10000n ? ',' + String(total % 10000n).padStart(4, '0').replace(/0+$/, '') : ''}`
  return (
    <section className="panel" aria-label="Chia chi phí theo bộ phận">
      <div className="toolbar"><h3>Chia chi phí khấu hao / phân bổ: {assetLabel}</h3><button onClick={onClose}>Đóng</button></div>
      <p className="muted">Mỗi kỳ chạy, chi phí của thẻ được ghi Nợ vào các tài khoản dưới đây theo tỷ lệ (tổng đúng 100%); phần lẻ do làm tròn dồn vào dòng có tỷ lệ lớn nhất. Không khai dòng nào thì dùng tài khoản chi phí của thẻ. Đổi cách chia chỉ áp dụng cho các kỳ chạy sau, kỳ đã chạy giữ nguyên.</p>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      <table className="grid"><thead><tr><th>Tài khoản chi phí</th><th>Bộ phận hưởng lợi</th><th className="num">Tỷ lệ %</th><th /></tr></thead>
        <tbody>{rows.map((r, i) => <tr key={i}>
          <td><AccountSelect accounts={accounts} value={r.accountId} onChange={v => set(i, { accountId: v })} filter={a => a.cashKind === 'None' && a.openItemNature === 'None' && !a.requiresPartner} ariaLabel={`TK chi phí dòng ${i + 1}`} /></td>
          <td><input aria-label={`Bộ phận dòng ${i + 1}`} value={r.label} onChange={e => set(i, { label: e.target.value })} /></td>
          <td><input className="num" aria-label={`Tỷ lệ dòng ${i + 1}`} inputMode="decimal" size={8} value={r.percent} onChange={e => set(i, { percent: e.target.value })} /></td>
          <td><button onClick={() => setRows(rs => rs.filter((_, k) => k !== i))}>Bỏ</button></td></tr>)}
          {rows.length === 0 && <tr><td colSpan={4} className="muted">Chưa chia: dùng tài khoản chi phí của thẻ.</td></tr>}</tbody></table>
      <div className="toolbar" style={{ marginTop: 8 }}>
        <button onClick={() => setRows(rs => [...rs, { accountId: '', label: '', percent: rs.length === 0 ? '100' : '' }])}>+ Thêm dòng</button>
        <span className={ok ? 'muted' : 'msg error'} style={{ padding: '2px 8px' }}>Tổng: {sumText}%{rows.length > 0 && !ok ? ' — phải đúng 100%' : ''}</span>
        <button className="primary" disabled={busy || !ok || rows.some(r => !r.accountId)} onClick={() => {
          const body = rows.map(r => ({ expenseAccountId: r.accountId, label: r.label.trim() || null, percent: parseMoney(r.percent) }))
          if (body.some(b => b.percent == null)) { setError(new Error('Tỷ lệ không hợp lệ.')); return }
          run(async () => { await put(`/assets/${assetId}/expense-splits`, body); setInfo(rows.length ? 'Đã lưu cách chia chi phí.' : 'Đã bỏ cách chia, dùng tài khoản chi phí của thẻ.'); await load() })
        }}>Lưu</button>
      </div>
    </section>
  )
}

/** Chi phí khấu hao / phân bổ đã ghi sổ theo tài khoản chi phí và bộ phận trong khoảng ngày. */
export function ExpenseAllocationReport() {
  const [f, pf, cf] = useFilterState('asset-allocation')
  const [rows, setRows] = useState<Row[]>([])
  const { error, run } = useAction()
  useEffect(() => { if (f.from && f.to) run(async () => setRows(await get<Row[]>(`/assets/expense-allocation?from=${f.from}&to=${f.to}`))) }, [run, f.from, f.to])
  const total = addMoney(rows.map(r => r.amount))
  return (
    <section className="panel" aria-label="Chi phí theo bộ phận">
      <h3>Chi phí khấu hao, phân bổ theo bộ phận <span className="muted">· số đã ghi sổ của các kỳ khấu hao giao với khoảng ngày, không tính kỳ đã hủy</span></h3>
      <SmartFilter label="Lọc chi phí theo bộ phận" dateLabel="kỳ khấu hao giao với khoảng ngày" state={f} patch={pf} clear={cf} shown={rows.length} total={rows.length} noSearch />
      <ErrorBox error={error} />
      <table className="grid"><thead><tr><th>Tài khoản chi phí</th><th>Bộ phận</th><th className="num">Số tiền</th></tr></thead>
        <tbody>{rows.map((r, i) => <tr key={i}><td>{r.accountCode} – {r.accountName}</td><td>{r.label ?? ''}</td><td className="num">{formatMoney(r.amount)}</td></tr>)}
          {rows.length === 0 && <tr><td colSpan={3} className="muted">Chưa có khấu hao, phân bổ trong khoảng ngày này.</td></tr>}
          {rows.length > 0 && <tr><td colSpan={2}><b>Cộng</b></td><td className="num"><b>{formatMoney(total)}</b></td></tr>}</tbody></table>
    </section>
  )
}
