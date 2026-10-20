import { useCallback, useEffect, useMemo, useState } from 'react'
import { api, get, post, put } from './api'
import { ErrorBox, Modal, useAction } from './common'
import { formatDate, formatMoney, parseMoney, today } from './money'
import type { Order } from './Orders'

interface Item { id: string; code: string; name: string; unit: string; isService?: boolean }
interface MsRow { id: string; orderId: string; orderNumber: string; orderLineNo: number; milestoneNo: number; name: string; percentage: string; quantity: string; unit: string; itemCode: string; dueDate: string; acceptanceNumber: string | null; status: string }
interface TimeRow { id: string; orderId: string; orderNumber: string; orderLineNo: number; itemCode: string; workDate: string; worker: string; hours: string; note: string | null; acceptanceNumber: string | null; locked: boolean }
const MS_VI: Record<string, string> = { PENDING: 'Chưa đến hạn', OVERDUE: 'Quá hạn', WAITING: 'Chờ xác nhận nghiệm thu', DONE: 'Đã nghiệm thu' }

/** Dịch vụ theo mốc (tỷ lệ % số lượng dòng đơn) và theo giờ công (chấm giờ rồi chốt thành biên bản nghiệm thu). */
export function ServiceProgress({ orders, items, onChanged }: { orders: Order[]; items: Item[]; onChanged: () => void }) {
  const [part, setPart] = useState<'ms' | 'time'>('ms')
  const [ms, setMs] = useState<MsRow[]>([])
  const [times, setTimes] = useState<TimeRow[]>([])
  const [setup, setSetup] = useState<{ orderId: string; lineNo: number; rows: { name: string; pct: string; due: string }[] } | null>(null)
  const [tf, setTf] = useState({ orderId: '', lineNo: 0, date: today(), worker: '', hours: '', note: '' })
  const [close, setClose] = useState({ through: today(), perUnit: '8' })
  const [info, setInfo] = useState('')
  const { busy, error, run, setError } = useAction()
  const isService = useCallback((id: string) => items.find(i => i.id === id)?.isService, [items])
  const svcLines = useMemo(() => orders.flatMap(o => o.lines.filter(l => isService(l.itemId)).map(l => ({ o, l, it: items.find(i => i.id === l.itemId)! }))), [orders, items, isService])
  const load = useCallback(() => run(async () => { setMs(await get<MsRow[]>('/service-progress/milestones')); setTimes(await get<TimeRow[]>('/service-progress/time')) }), [run])
  useEffect(() => { load() }, [load])
  const act = (msg: string, fn: () => Promise<unknown>) => run(async () => { await fn(); setInfo(msg); await load(); onChanged() })
  const sel = svcLines.find(x => x.o.id === tf.orderId && x.l.lineNo === tf.lineNo)
  const openHours = times.filter(t => t.orderId === tf.orderId && t.orderLineNo === tf.lineNo && !t.locked && t.workDate <= close.through).reduce((s, t) => s + Number(t.hours), 0)
  return (
    <>
      <div className="toolbar">
        <button className={part === 'ms' ? 'primary' : ''} onClick={() => setPart('ms')}>Mốc thực hiện</button>
        <button className={part === 'time' ? 'primary' : ''} onClick={() => setPart('time')}>Chấm giờ</button></div>
      <ErrorBox error={error} />
      {info && !error && <div className="msg ok">{info}</div>}
      {part === 'ms' && <section className="panel"><h3>Mốc thực hiện dịch vụ</h3>
        <p className="muted">Mốc là một tỷ lệ % số lượng của dòng dịch vụ. Hoàn thành mốc lập biên bản nghiệm thu (người khác xác nhận), sau đó lập hóa đơn trong phạm vi đã nghiệm thu.</p>
        <div className="scroll"><table className="grid"><thead><tr><th>Đơn</th><th>Dịch vụ</th><th>Mốc</th><th className="num">Tỷ lệ</th><th className="num">Số lượng</th><th>Hạn</th><th>Tình trạng</th><th /></tr></thead>
          <tbody>{ms.map(m => <tr key={m.id} style={m.status === 'OVERDUE' ? { color: 'var(--danger, #b42318)' } : undefined}><td>{m.orderNumber}/{m.orderLineNo}</td><td>{m.itemCode}</td><td>{m.milestoneNo}. {m.name}</td><td className="num">{formatMoney(m.percentage)}%</td>
            <td className="num">{formatMoney(m.quantity)} {m.unit}</td><td>{formatDate(m.dueDate)}</td><td>{MS_VI[m.status]}{m.acceptanceNumber ? ` (${m.acceptanceNumber})` : ''}</td>
            <td>{(m.status === 'PENDING' || m.status === 'OVERDUE') && <button className="primary" disabled={busy} onClick={() => act('Đã lập biên bản nghiệm thu cho mốc; chờ người khác xác nhận.', () => post(`/service-progress/milestones/${m.id}/complete`, { date: today() }))}>Hoàn thành mốc</button>}</td></tr>)}
            {ms.length === 0 && <tr><td colSpan={8} className="muted">Chưa khai mốc nào.</td></tr>}</tbody></table></div>
        <div className="toolbar"><label>Khai mốc cho dòng dịch vụ<select aria-label="Dòng dịch vụ khai mốc" value={setup ? `${setup.orderId}:${setup.lineNo}` : ''} onChange={e => { const [o, l] = e.target.value.split(':'); setSetup(o ? { orderId: o, lineNo: Number(l), rows: [{ name: '', pct: '', due: today() }] } : null) }}>
          <option value="">— chọn —</option>{svcLines.map(x => <option key={`${x.o.id}:${x.l.lineNo}`} value={`${x.o.id}:${x.l.lineNo}`}>{x.o.number}/{x.l.lineNo} · {x.it.code} × {formatMoney(x.l.quantity)} {x.it.unit}</option>)}</select></label></div>
      </section>}
      {part === 'time' && <section className="panel"><h3>Chấm giờ dịch vụ</h3>
        <div className="form-row">
          <label>Dòng dịch vụ<select aria-label="Dòng dịch vụ chấm giờ" value={tf.orderId ? `${tf.orderId}:${tf.lineNo}` : ''} onChange={e => { const [o, l] = e.target.value.split(':'); setTf(f => ({ ...f, orderId: o ?? '', lineNo: Number(l ?? 0) })) }}>
            <option value="">— chọn —</option>{svcLines.map(x => <option key={`${x.o.id}:${x.l.lineNo}`} value={`${x.o.id}:${x.l.lineNo}`}>{x.o.number}/{x.l.lineNo} · {x.it.code} ({x.it.unit})</option>)}</select></label>
          <label>Ngày<input type="date" value={tf.date} onChange={e => setTf(f => ({ ...f, date: e.target.value }))} /></label>
          <label>Người thực hiện<input value={tf.worker} onChange={e => setTf(f => ({ ...f, worker: e.target.value }))} /></label>
          <label>Số giờ<input inputMode="decimal" size={5} value={tf.hours} onChange={e => setTf(f => ({ ...f, hours: e.target.value }))} /></label>
          <label style={{ flex: 1 }}>Ghi chú<input value={tf.note} onChange={e => setTf(f => ({ ...f, note: e.target.value }))} /></label>
          <button disabled={busy || !tf.orderId || !tf.worker.trim() || !tf.hours} onClick={() => { const h = parseMoney(tf.hours); if (!h) { setError(new Error('Số giờ không hợp lệ.')); return } act('Đã ghi giờ.', async () => { await post('/service-progress/time', { orderId: tf.orderId, orderLineNo: tf.lineNo, workDate: tf.date, worker: tf.worker, hours: h, note: tf.note || null }); setTf(f => ({ ...f, hours: '', note: '' })) }) }}>Ghi giờ</button>
        </div>
        <div className="scroll"><table className="grid"><thead><tr><th>Ngày</th><th>Đơn</th><th>Dịch vụ</th><th>Người thực hiện</th><th className="num">Giờ</th><th>Ghi chú</th><th>Chốt vào</th><th /></tr></thead>
          <tbody>{times.map(t => <tr key={t.id}><td>{formatDate(t.workDate)}</td><td>{t.orderNumber}/{t.orderLineNo}</td><td>{t.itemCode}</td><td>{t.worker}</td><td className="num">{formatMoney(t.hours)}</td><td>{t.note}</td><td>{t.locked ? t.acceptanceNumber : ''}</td>
            <td>{!t.locked && <button aria-label="Xóa dòng giờ" disabled={busy} onClick={() => act('Đã xóa dòng giờ.', () => api('DELETE', `/service-progress/time/${t.id}`))}>✕</button>}</td></tr>)}
            {times.length === 0 && <tr><td colSpan={8} className="muted">Chưa có dòng chấm giờ.</td></tr>}</tbody></table></div>
        {sel && <div className="toolbar" style={{ marginTop: 8 }}>
          <label>Chốt giờ đến ngày<input type="date" value={close.through} onChange={e => setClose(c => ({ ...c, through: e.target.value }))} /></label>
          <label>Số giờ mỗi {sel.it.unit}<input inputMode="decimal" size={4} value={close.perUnit} onChange={e => setClose(c => ({ ...c, perUnit: e.target.value }))} /></label>
          <span className="muted">Giờ chưa chốt đến ngày này: {formatMoney(String(openHours))}{parseMoney(close.perUnit) ? ` → ${formatMoney(String(Math.round(openHours / Number(parseMoney(close.perUnit)) * 10000) / 10000))} ${sel.it.unit}` : ''}</span>
          <button className="primary" disabled={busy || openHours <= 0} onClick={() => { const p = parseMoney(close.perUnit); if (!p) { setError(new Error('Số giờ mỗi đơn vị không hợp lệ.')); return } act('Đã lập biên bản nghiệm thu từ chấm giờ; chờ người khác xác nhận.', () => post('/service-progress/time/close', { orderId: tf.orderId, orderLineNo: tf.lineNo, through: close.through, hoursPerUnit: p, acceptanceDate: today() })) }}>Chốt chấm giờ → nghiệm thu</button></div>}
      </section>}
      {setup && <Modal title="Khai mốc thực hiện" onClose={() => setSetup(null)} busy={busy}
        footer={<><button onClick={() => setSetup(null)}>Đóng</button><button className="primary" disabled={busy} onClick={() => run(async () => {
          const milestones = setup.rows.filter(r => r.name.trim() || r.pct.trim()).map(r => { const p = parseMoney(r.pct); if (!p || !r.due) throw new Error(`Mốc "${r.name || '(chưa đặt tên)'}": nhập tỷ lệ % và ngày hợp lệ.`); return { name: r.name, percentage: p, dueDate: r.due } })
          await put(`/service-progress/milestones/${setup.orderId}/${setup.lineNo}`, { milestones }); setSetup(null); setInfo('Đã lưu mốc.'); await load()
        })}>Lưu</button></>}>
        <table className="grid"><thead><tr><th>Tên mốc</th><th className="num">Tỷ lệ %</th><th>Hạn</th><th /></tr></thead>
          <tbody>{setup.rows.map((r, i) => <tr key={i}>
            <td><input aria-label="Tên mốc" value={r.name} onChange={e => setSetup(s => s && { ...s, rows: s.rows.map((x, j) => j === i ? { ...x, name: e.target.value } : x) })} /></td>
            <td className="num"><input aria-label="Tỷ lệ mốc" size={6} inputMode="decimal" value={r.pct} onChange={e => setSetup(s => s && { ...s, rows: s.rows.map((x, j) => j === i ? { ...x, pct: e.target.value } : x) })} /></td>
            <td><input type="date" aria-label="Hạn mốc" value={r.due} onChange={e => setSetup(s => s && { ...s, rows: s.rows.map((x, j) => j === i ? { ...x, due: e.target.value } : x) })} /></td>
            <td><button aria-label="Xóa mốc" onClick={() => setSetup(s => s && { ...s, rows: s.rows.filter((_, j) => j !== i) })}>✕</button></td></tr>)}</tbody></table>
        <div className="toolbar"><button onClick={() => setSetup(s => s && { ...s, rows: [...s.rows, { name: '', pct: '', due: today() }] })}>+ Thêm mốc</button></div>
        <p className="muted">Tổng tỷ lệ các mốc của dòng không vượt 100%. Chỉ sửa được khi chưa có mốc nào hoàn thành.</p>
      </Modal>}
    </>
  )
}
