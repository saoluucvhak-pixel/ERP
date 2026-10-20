import { navStore } from './common'
import { BarChart, CHART_COLORS, Donut, Legend, LineChart } from './charts'
import { addMoney, formatMoney } from './money'

export interface OverviewData {
  asOf: string; year: number; cash: string; cashOnHand: string; bank: string; receivable: string; payable: string; inventory: string
  hasIncomeTemplate: boolean; incomeTemplateVerified: boolean
  income: { month: number; revenue: string; netSales: string; cost: string; profit: string }[]
  cashFlow: { month: number; inflow: string; outflow: string; closing: string }[]
  costBreakdown: { name: string; amount: string }[]
  inventoryTop: { code: string; name: string; unit: string; quantity: string; value: string }[]
  bestSellers: { code: string; name: string; quantity: string; value: string }[]
}
export interface AgingLite { nature: string; data: { rows: { notDue: string; buckets: string[] }[] } }

const mil = (v: string) => Number(v) / 1_000_000
const milText = (v: string) => mil(v).toLocaleString('vi-VN', { maximumFractionDigits: 2 })
const months = (n: { month: number }[]) => n.map(m => `Th ${m.month}`)
const sum = (vals: string[]) => addMoney(vals)

function Num({ label, value, go, danger }: { label: string; value: string; go?: string; danger?: boolean }) {
  return <div className="ov-row" style={{ cursor: go ? 'pointer' : undefined }} onClick={() => go && navStore.go?.(go)}><span>{label}</span><b style={{ color: danger ? 'var(--danger)' : 'var(--accent)' }}>{milText(value)}</b></div>
}
function Card({ title, right, children, grow }: { title: string; right?: React.ReactNode; children: React.ReactNode; grow?: number }) {
  return <section className="ov-card" style={{ flex: `${grow ?? 1} 1 340px` }}><div className="ov-head"><h3>{title}</h3>{right}</div>{children}</section>
}

/** Các thẻ tổng quan kiểu MISA. Đơn vị Triệu đồng; số đầy đủ xem ở các màn chi tiết. */
export function OverviewCards({ data, aging, vat }: { data: OverviewData | null; aging: AgingLite[]; vat: string | null }) {
  if (!data) return <p className="muted">Đang tải số liệu tổng quan…</p>
  const yearRevenue = sum(data.income.map(i => i.revenue)); const yearCost = sum(data.income.map(i => i.cost)); const yearProfit = sum(data.income.map(i => i.profit))
  const noTemplate = !data.hasIncomeTemplate
  const totalCashIn = sum(data.cashFlow.map(c => c.inflow)); const totalCashOut = sum(data.cashFlow.map(c => c.outflow))
  const costParts = data.costBreakdown.map((c, i) => ({ name: c.name, value: Number(c.amount) / 1_000_000, color: CHART_COLORS[i % CHART_COLORS.length] }))
  const debt = (n: 'Receivable' | 'Payable') => {
    const rows = aging.filter(x => x.nature === n).flatMap(x => x.data.rows)
    const late = sum(rows.flatMap(r => r.buckets)); const inTerm = sum(rows.map(r => r.notDue)); const total = sum([late, inTerm])
    return { late, inTerm, total, pct: Number(total) === 0 ? 0 : Math.min(100, Math.round((Number(late) / Number(total)) * 100)) }
  }
  return (
    <>
      {noTemplate && <div className="msg error">Chưa nhập mẫu B02-DN nên chưa có doanh thu, chi phí, lợi nhuận. Nhập mẫu ở Tổng hợp → Báo cáo theo mẫu. {data.incomeTemplateVerified ? '' : ''}</div>}
      {data.hasIncomeTemplate && !data.incomeTemplateVerified && <p className="muted">Doanh thu, chi phí, lợi nhuận lấy từ mẫu B02-DN chưa xác minh — chỉ để tham khảo.</p>}
      <div className="ov-grid">
        <Card title="Tình hình tài chính" grow={2} right={<span className="muted">Đvt: Triệu đồng</span>}>
          <div className="ov-two">
            <div>
              <div className="ov-sum" onClick={() => navStore.go?.('ledger')}><span>TỔNG TIỀN</span><b>{milText(data.cash)}</b></div>
              <div className="ov-sub"><Num label="Tiền mặt" value={data.cashOnHand} go="ledger" /><Num label="Tiền gửi" value={data.bank} go="ledger" /></div>
              <Num label="Phải thu" value={data.receivable} go="arap" />
              <Num label="Phải trả" value={data.payable} go="arap" />
            </div>
            <div>
              <Num label="Doanh thu" value={noTemplate ? '0' : yearRevenue} go="reports" />
              <Num label="Chi phí" value={noTemplate ? '0' : yearCost} go="reports" />
              <Num label="Lợi nhuận" value={noTemplate ? '0' : yearProfit} go="reports" danger={Number(yearProfit) < 0} />
              <Num label="Hàng tồn kho" value={data.inventory} go="stock" />
              {vat != null && <Num label="Thuế GTGT tháng này (dự thảo)" value={vat} go="vat" />}
            </div>
          </div>
          <div className="muted ov-foot">Số đã ghi sổ đến {data.asOf.split('-').reverse().join('/')}. Doanh thu, chi phí, lợi nhuận: từ đầu năm {data.year}.</div>
        </Card>
        {(['Receivable', 'Payable'] as const).map(n => { const d = debt(n); return (
          <Card key={n} title={n === 'Receivable' ? 'Nợ phải thu theo hạn nợ' : 'Nợ phải trả theo hạn nợ'}>
            <div className="muted">Tổng</div><div className="ov-big">{milText(d.total)} <small>Triệu đồng</small></div>
            <div className="ov-two" style={{ marginTop: 8 }}><div><div className="muted">Quá hạn</div><b style={{ color: 'var(--warn)' }}>{milText(d.late)}</b></div><div style={{ textAlign: 'right' }}><div className="muted">Trong hạn</div><b>{milText(d.inTerm)}</b></div></div>
            <div className="debt-bar" aria-label={`Quá hạn ${d.pct}%`}><div style={{ width: `${d.pct}%` }} /></div>
          </Card>) })}
      </div>

      <div className="ov-grid">
        <Card title="Doanh thu, chi phí, lợi nhuận" grow={2} right={<span className="muted">Năm {data.year} · Đvt: Triệu đồng</span>}>
          <div className="ov-totals"><div><span className="muted">DOANH THU</span><b style={{ color: 'var(--accent)' }}>{milText(noTemplate ? '0' : yearRevenue)}</b></div>
            <div><span className="muted">CHI PHÍ</span><b>{milText(noTemplate ? '0' : yearCost)}</b></div>
            <div><span className="muted">LỢI NHUẬN</span><b style={{ color: Number(yearProfit) < 0 ? 'var(--danger)' : undefined }}>{noTemplate ? '0' : Number(yearProfit) < 0 ? `(${milText(yearProfit.replace('-', ''))})` : milText(yearProfit)}</b></div></div>
          <BarChart label="Doanh thu, chi phí, lợi nhuận theo tháng" labels={months(data.income.length ? data.income : data.cashFlow)}
            series={[{ name: 'Doanh thu', color: CHART_COLORS[0], values: data.income.map(i => mil(i.revenue)) }, { name: 'Chi phí', color: CHART_COLORS[3], values: data.income.map(i => mil(i.cost)) }, { name: 'Lợi nhuận', color: CHART_COLORS[1], values: data.income.map(i => mil(i.profit)) }]} />
          <Legend items={[{ name: 'Doanh thu', color: CHART_COLORS[0] }, { name: 'Chi phí', color: CHART_COLORS[3] }, { name: 'Lợi nhuận', color: CHART_COLORS[1] }]} />
        </Card>
        <Card title="Hàng hóa tồn kho" right={<span className="muted">Đvt: Triệu đồng</span>}>
          <div className="muted">Tổng cộng</div><div className="ov-big">{milText(data.inventory)} <small>Triệu đồng</small></div>
          <table className="grid"><thead><tr><th>Tên</th><th className="num">Số lượng</th><th className="num">Giá trị</th></tr></thead>
            <tbody>{data.inventoryTop.map((i, k) => <tr key={i.code}><td><span className="dot" style={{ background: CHART_COLORS[k % CHART_COLORS.length] }} />{i.name}</td><td className="num">{formatMoney(i.quantity)}</td><td className="num">{formatMoney(i.value)}</td></tr>)}
              {data.inventoryTop.length === 0 && <tr><td colSpan={3} className="muted">Chưa có tồn kho.</td></tr>}</tbody></table>
          <div style={{ textAlign: 'right' }}><button className="link" style={{ color: 'var(--accent)' }} onClick={() => navStore.go?.('stock')}>Xem thêm</button></div>
        </Card>
      </div>

      <div className="ov-grid">
        <Card title="Doanh thu" grow={2} right={<span className="muted">Năm {data.year} · Đvt: Triệu đồng</span>}>
          <div className="ov-big">{milText(noTemplate ? '0' : sum(data.income.map(i => i.netSales)))} <small>Triệu đồng</small></div>
          <LineChart label="Doanh thu thuần theo tháng" labels={months(data.income.length ? data.income : data.cashFlow)} series={[{ name: 'Doanh thu', color: CHART_COLORS[0], values: data.income.map(i => mil(i.netSales)) }]} />
        </Card>
        <Card title="Mặt hàng bán chạy" right={<span className="muted">Năm {data.year}</span>}>
          <table className="grid"><thead><tr><th>Tên</th><th className="num">Số lượng</th><th className="num">Giá trị</th></tr></thead>
            <tbody>{data.bestSellers.map((i, k) => <tr key={i.code}><td><span className="dot" style={{ background: CHART_COLORS[k % CHART_COLORS.length] }} />{i.name}</td><td className="num">{formatMoney(i.quantity)}</td><td className="num">{formatMoney(i.value)}</td></tr>)}
              {data.bestSellers.length === 0 && <tr><td colSpan={3} className="muted">Chưa có hóa đơn bán trong năm.</td></tr>}</tbody></table>
          <div style={{ textAlign: 'right' }}><button className="link" style={{ color: 'var(--accent)' }} onClick={() => navStore.go?.('revenue')}>Xem thêm</button></div>
        </Card>
      </div>

      <div className="ov-grid">
        <Card title="Dòng tiền" grow={2} right={<span className="muted">Năm {data.year} · Đvt: Triệu đồng</span>}>
          <div className="ov-totals"><div><span className="muted">THU</span><b style={{ color: CHART_COLORS[0] }}>{milText(totalCashIn)}</b></div><div><span className="muted">CHI</span><b>{milText(totalCashOut)}</b></div><div><span className="muted">TỒN</span><b style={{ color: CHART_COLORS[1] }}>{milText(data.cash)}</b></div></div>
          <LineChart label="Dòng tiền theo tháng" labels={months(data.cashFlow)} series={[{ name: 'Thu', color: CHART_COLORS[0], values: data.cashFlow.map(c => mil(c.inflow)) }, { name: 'Chi', color: '#9aa5b4', values: data.cashFlow.map(c => mil(c.outflow)) }, { name: 'Tồn', color: CHART_COLORS[1], values: data.cashFlow.map(c => mil(c.closing)) }]} />
          <Legend items={[{ name: 'Thu', color: CHART_COLORS[0] }, { name: 'Chi', color: '#9aa5b4' }, { name: 'Tồn', color: CHART_COLORS[1] }]} />
        </Card>
        <Card title="Chi phí" right={<span className="muted">Năm {data.year} · Đvt: Triệu đồng</span>}>
          <div className="ov-big">{milText(noTemplate ? '0' : yearCost)} <small>Triệu đồng</small></div>
          <div style={{ display: 'flex', justifyContent: 'center' }}><Donut label="Cơ cấu chi phí" parts={costParts} /></div>
          <Legend items={costParts.map(p => ({ name: `${p.name} (${p.value.toLocaleString('vi-VN', { maximumFractionDigits: 2 })})`, color: p.color }))} />
          {costParts.length === 0 && <p className="muted" style={{ textAlign: 'center' }}>Chưa có chi phí trong năm.</p>}
        </Card>
      </div>
    </>
  )
}
