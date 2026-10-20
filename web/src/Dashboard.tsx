import { useCallback, useEffect, useState } from 'react'
import { get, type Period } from './api'
import { ErrorBox, navStore, useAction, useCatalog } from './common'
import { maxAbs, pct } from './dashboardmath'
import { addMoney, formatMoney, today } from './money'

interface TbRow { accountId: string; accountCode: string; accountName: string; closingDebit: string; closingCredit: string }
interface Aging { bucketLabels: string[]; rows: { notDue: string; buckets: string[] }[] }
interface Counts { draft: number | null; submitted: number | null; approved: number | null; stockDrafts: number | null }

const neg = (v: string) => (v.startsWith('-') ? v.slice(1) : v === '0' ? '0' : '-' + v)
function Bar({ value, max, tone = 'var(--accent, #2b6cb0)' }: { value: string; max: string; tone?: string }) {
  return <div style={{ background: 'var(--line)', borderRadius: 3, height: 8, minWidth: 80 }}><div style={{ width: `${pct(value, max)}%`, background: tone, height: 8, borderRadius: 3 }} /></div>
}

function Tile({ label, value, sub, go, warn }: { label: string; value: string | null; sub?: string; go?: string; warn?: boolean }) {
  return (
    <div className="panel" style={{ flex: '1 1 200px', cursor: go ? 'pointer' : undefined, marginBottom: 0 }} onClick={() => go && navStore.go?.(go)}>
      <div className="muted">{label}</div>
      <div style={{ fontSize: 22, fontWeight: 600, color: warn ? 'var(--danger)' : undefined, fontVariantNumeric: 'tabular-nums' }}>{value == null ? '—' : formatMoney(value)}</div>
      {sub && <div className="muted">{sub}</div>}
    </div>
  )
}

/** Tổng quan: số dư tiền, công nợ, tuổi nợ, việc đang chờ, tình trạng kiểm soát. Chỉ đọc; mục nào người dùng không đủ quyền xem thì hiện "—". */
export function DashboardView() {
  const { accounts } = useCatalog()
  const [tb, setTb] = useState<{ isBalanced: boolean; rows: TbRow[] } | null>(null)
  const [aging, setAging] = useState<{ code: string; name: string; nature: string; data: Aging }[]>([])
  const [counts, setCounts] = useState<Counts | null>(null)
  const [periods, setPeriods] = useState<Period[]>([])
  const [recon, setRecon] = useState<boolean | null>(null)
  const [vat, setVat] = useState<string | null>(null)
  const { busy, error, run } = useAction()
  const asOf = today()
  const month1 = asOf.slice(0, 8) + '01'

  const load = useCallback(() => run(async () => {
    const total = async (path: string) => (await get<{ total: number }>(path)).total
    const [t, r, p, d, s, a, st, v] = await Promise.allSettled([
      get<{ isBalanced: boolean; rows: TbRow[] }>(`/reports/trial-balance?from=${asOf.slice(0, 4)}-01-01&to=${asOf}&rollup=false`),
      get<{ isReconciled: boolean }>(`/reports/subledger-reconciliation?asOf=${asOf}`),
      get<Period[]>('/periods'),
      total('/vouchers?status=Draft&take=1'), total('/vouchers?status=Submitted&take=1'), total('/vouchers?status=Approved&take=1'),
      get<{ status: string }[]>('/stock-documents'),
      get<{ netPayable: string }>(`/vat/payable-summary?from=${month1}&to=${asOf}`),
    ])
    setTb(t.status === 'fulfilled' ? t.value : null)
    setRecon(r.status === 'fulfilled' ? r.value.isReconciled : null)
    setPeriods(p.status === 'fulfilled' ? p.value : [])
    setCounts({ draft: d.status === 'fulfilled' ? d.value : null, submitted: s.status === 'fulfilled' ? s.value : null, approved: a.status === 'fulfilled' ? a.value : null,
      stockDrafts: st.status === 'fulfilled' ? st.value.filter(x => x.status === 'Draft').length : null })
    setVat(v.status === 'fulfilled' ? v.value.netPayable : null)
  }), [run, asOf, month1])
  useEffect(() => { load() }, [load])

  // Aging of every receivable / payable account, one request.
  useEffect(() => {
    get<{ code: string; name: string; nature: string; report: Aging }[]>(`/reports/aging-all?asOf=${asOf}`)
      .then(r => setAging(r.map(x => ({ code: x.code, name: x.name, nature: x.nature, data: x.report }))))
      .catch(() => setAging([]))
  }, [asOf])

  const bal = (id: string) => { const r = tb?.rows.find(x => x.accountId === id); return r ? addMoney([r.closingDebit, '-' + r.closingCredit]) : '0' }
  const cash = accounts.filter(a => a.cashKind !== 'None' && a.isPostable).map(a => ({ a, v: bal(a.id) })).filter(x => x.v !== '0' || x.a.isActive)
  const cashTotal = tb ? addMoney(cash.map(x => x.v)) : null
  const sumNature = (n: string) => (tb ? addMoney(accounts.filter(a => a.openItemNature === n && a.isPostable).map(a => bal(a.id))) : null)
  const receivable = sumNature('Receivable')
  const payable = tb ? neg(sumNature('Payable') ?? '0') : null
  const overdue = (n: string) => addMoney(aging.filter(x => x.nature === n).flatMap(x => x.data.rows.flatMap(r => r.buckets)))
  const maxCash = maxAbs(cash.map(x => x.v))
  const open = periods.filter(p => p.status === 'Open')
  const current = periods.find(p => p.startDate <= asOf && asOf <= p.endDate)
  const todo = counts && (counts.draft ?? 0) + (counts.submitted ?? 0) + (counts.approved ?? 0) + (counts.stockDrafts ?? 0)

  return (
    <>
      <div className="toolbar"><h2>Tổng quan</h2><span className="muted">Số đã ghi sổ đến {asOf.split('-').reverse().join('/')}</span><button onClick={() => load()} disabled={busy}>Tải lại</button></div>
      <ErrorBox error={error} />
      <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap', marginBottom: 12 }}>
        <Tile label="Tiền mặt + ngân hàng" value={cashTotal} sub={`${cash.length} tài khoản`} go="tb" />
        <Tile label="Phải thu (số dư Nợ)" value={receivable} sub={aging.length ? `quá hạn ${formatMoney(overdue('Receivable'))}` : undefined} go="arap" />
        <Tile label="Phải trả (số dư Có)" value={payable} sub={aging.length ? `quá hạn ${formatMoney(overdue('Payable'))}` : undefined} go="arap" />
        <Tile label="Thuế GTGT tháng này (dự thảo từ sổ)" value={vat} sub="đầu ra − đầu vào, chưa phải tờ khai" go="vat" />
      </div>

      <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap', alignItems: 'flex-start' }}>
        <div className="panel" style={{ flex: '1 1 380px' }}>
          <strong>Tiền theo tài khoản</strong>
          <table className="grid"><tbody>{cash.map(x => (
            <tr key={x.a.id} style={{ cursor: 'pointer' }} onClick={() => { navStore.ledger = { accountId: x.a.id, from: asOf.slice(0, 4) + '-01-01', to: asOf }; navStore.go?.('ledger') }}>
              <td>{x.a.code} – {x.a.name}</td><td style={{ width: 120 }}><Bar value={x.v} max={maxCash} tone={x.v.startsWith('-') ? 'var(--danger)' : undefined} /></td>
              <td className="num" style={{ color: x.v.startsWith('-') ? 'var(--danger)' : undefined }}>{formatMoney(x.v)}</td></tr>))}
            {cash.length === 0 && <tr><td className="muted">Chưa có tài khoản tiền (đánh dấu loại quỹ/ngân hàng ở Thiết lập).</td></tr>}</tbody></table>
        </div>

        <div className="panel" style={{ flex: '1 1 380px' }}>
          <strong>Việc đang chờ {todo != null && <span className="badge">{todo}</span>}</strong>
          <table className="grid"><tbody>
            <tr style={{ cursor: 'pointer' }} onClick={() => navStore.go?.('vouchers')}><td>Chứng từ nháp</td><td className="num">{counts?.draft ?? '—'}</td></tr>
            <tr style={{ cursor: 'pointer' }} onClick={() => navStore.go?.('vouchers')}><td>Chứng từ chờ duyệt</td><td className="num">{counts?.submitted ?? '—'}</td></tr>
            <tr style={{ cursor: 'pointer' }} onClick={() => navStore.go?.('vouchers')}><td>Chứng từ đã duyệt, chờ ghi sổ</td><td className="num">{counts?.approved ?? '—'}</td></tr>
            <tr style={{ cursor: 'pointer' }} onClick={() => navStore.go?.('stock')}><td>Phiếu kho nháp</td><td className="num">{counts?.stockDrafts ?? '—'}</td></tr>
          </tbody></table>
        </div>
      </div>

      {aging.length > 0 && <div className="panel">
        <strong>Tuổi nợ</strong>
        {aging.map(x => {
          const cols = [{ label: 'Chưa đến hạn', v: addMoney(x.data.rows.map(r => r.notDue)) }, ...x.data.bucketLabels.map((l, i) => ({ label: `Quá hạn ${l} ngày`, v: addMoney(x.data.rows.map(r => r.buckets[i] ?? '0')) }))]
          const max = maxAbs(cols.map(c => c.v))
          return <div key={x.code} style={{ marginTop: 8 }}><div className="muted">{x.code} – {x.name} ({x.nature === 'Receivable' ? 'phải thu' : 'phải trả'})</div>
            <table className="grid"><tbody>{cols.map((c, i) => <tr key={i}><td style={{ width: 160 }}>{c.label}</td><td><Bar value={c.v} max={max} tone={i === 0 ? undefined : 'var(--danger)'} /></td><td className="num" style={{ width: 140 }}>{formatMoney(c.v)}</td></tr>)}</tbody></table></div>
        })}
      </div>}

      <div className="panel">
        <strong>Kiểm soát</strong>
        <ul style={{ margin: '6px 0 0', paddingLeft: 18 }}>
          <li style={{ color: tb == null ? undefined : tb.isBalanced ? 'var(--ok)' : 'var(--danger)' }}>{tb == null ? 'Bảng cân đối số phát sinh: không xem được' : tb.isBalanced ? '✓ Bảng cân đối số phát sinh cân (tổng Nợ = tổng Có)' : '✗ Bảng cân đối số phát sinh KHÔNG cân — kiểm tra ngay'}</li>
          <li style={{ color: recon == null ? undefined : recon ? 'var(--ok)' : 'var(--danger)' }}>{recon == null ? 'Đối chiếu công nợ: không xem được' : recon ? '✓ Sổ chi tiết công nợ khớp sổ cái' : '✗ Sổ chi tiết công nợ lệch sổ cái — xem màn Công nợ'}</li>
          <li>{current ? `Kỳ hiện tại: kỳ ${current.periodNo} (${current.startDate} → ${current.endDate}) — ${current.status === 'Open' ? 'đang mở' : 'ĐÃ KHÓA, không ghi sổ được'}` : 'Chưa có kỳ kế toán chứa ngày hôm nay (mở năm tài chính ở Thiết lập)'}</li>
          <li>{open.length} kỳ đang mở / {periods.length} kỳ trong năm tài chính{open.length > 1 ? ` — kỳ mở cũ nhất: ${open[0].periodNo}` : ''}</li>
        </ul>
      </div>
    </>
  )
}
