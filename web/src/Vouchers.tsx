import { useCallback, useEffect, useMemo, useState } from 'react'
import { get, post, put, session, type Voucher } from './api'
import { AccountSelect, CASHFLOW_VI, ErrorBox, ExportButton, PartnerSelect, PrintSheet, printSheet, STATUS_VI, TYPE_VI, useAction, useCatalog } from './common'
import { CashDocsView } from './CashDocs'
import { addMoney, amountInWords, formatMoney, parseMoney, today } from './money'

interface LineDraft { accountId: string; side: 'D' | 'C'; amount: string; partnerId: string; description: string; cashFlowCode: string }
const emptyLine = (side: 'D' | 'C'): LineDraft => ({ accountId: '', side, amount: '', partnerId: '', description: '', cashFlowCode: '' })

export function VouchersView() {
  const [from, setFrom] = useState(today().slice(0, 8) + '01')
  const [to, setTo] = useState(today())
  const [status, setStatus] = useState('')
  const [type, setType] = useState('')
  const [text, setText] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(0)
  const [total, setTotal] = useState(0)
  const [list, setList] = useState<Voucher[]>([])
  const [selected, setSelected] = useState<Voucher | null>(null)
  const [editing, setEditing] = useState<Voucher | 'new' | { copyOf: Voucher } | null>(null)
  const [checked, setChecked] = useState<Set<string>>(new Set())
  const { busy, error, run } = useAction()
  const [info, setInfo] = useState('')

  const load = useCallback(() => run(async () => {
    const q = new URLSearchParams({ from, to, skip: String(page * PAGE), take: String(PAGE), ...(status ? { status } : {}), ...(type ? { type } : {}), ...(search ? { text: search } : {}) })
    const data = await get<{ items: Voucher[]; total: number }>(`/vouchers?${q}`)
    setList(data.items)
    setTotal(data.total)
    setSelected(s => (s ? data.items.find(v => v.id === s.id) ?? null : null))
    setChecked(c => new Set([...c].filter(id => data.items.some(v => v.id === id))))
  }), [from, to, status, type, search, page, run])
  useEffect(() => { load() }, [load])

  const act = (label: string, fn: () => Promise<unknown>) => run(async () => { await fn(); setInfo(label); await load() })
  // Bulk actions on the ticked vouchers: one by one in posting-date order through the same endpoints as a single voucher, so every
  // check still applies (approver ≠ preparer, open period, cash funds not negative…); failures are listed, the rest continue.
  const bulk = (label: string, from: string[], steps: string[]) => run(async () => {
    const todo = list.filter(x => checked.has(x.id) && from.includes(x.status))
      .sort((a, b) => (a.postingDate === b.postingDate ? (a.number < b.number ? -1 : 1) : a.postingDate < b.postingDate ? -1 : 1))
    let done = 0
    const failed: string[] = []
    for (const x of todo) {
      try {
        let rv = x.rowVersion
        for (const s of steps) rv = (await post<Voucher>(`/vouchers/${x.id}/${s}?rowVersion=${rv}`)).rowVersion
        done++
      } catch (e) { failed.push(`${x.number}: ${e instanceof Error ? e.message : String(e)}`) }
    }
    const skipped = checked.size - todo.length
    setInfo(`${label}: ${done} chứng từ.${skipped ? ` Bỏ qua ${skipped} chứng từ không ở trạng thái phù hợp.` : ''}${failed.length ? ` Không làm được ${failed.length}: ${failed.slice(0, 8).join(' | ')}${failed.length > 8 ? ' …' : ''}` : ''}`)
    setChecked(new Set())
    await load()
  })
  const count = (...s: string[]) => list.filter(x => checked.has(x.id) && s.includes(x.status)).length

  if (editing && editing !== 'new' && !('copyOf' in editing) && CASH_TYPES.includes(editing.voucherType))
    return <CashDocsView voucher={editing} onDone={() => { setEditing(null); load() }} />
  if (editing) return <VoucherForm voucher={editing === 'new' || 'copyOf' in editing ? null : editing} template={editing !== 'new' && 'copyOf' in editing ? editing.copyOf : null} onDone={() => { setEditing(null); load() }} />

  const v = selected
  return (
    <>
      <div className="toolbar">
        <h2>Chứng từ kế toán</h2>
        <label>Từ ngày<input type="date" value={from} onChange={e => { setFrom(e.target.value); setPage(0) }} /></label>
        <label>Đến ngày<input type="date" value={to} onChange={e => { setTo(e.target.value); setPage(0) }} /></label>
        <label>Trạng thái
          <select value={status} onChange={e => { setStatus(e.target.value); setPage(0) }}>
            <option value="">Tất cả</option>
            {Object.entries(STATUS_VI).map(([k, t]) => <option key={k} value={k}>{t}</option>)}
          </select>
        </label>
        <label>Loại
          <select value={type} onChange={e => { setType(e.target.value); setPage(0) }}>
            <option value="">Tất cả</option>
            {Object.entries(TYPE_VI).map(([k, t]) => <option key={k} value={k}>{t}</option>)}
          </select>
        </label>
        <form onSubmit={e => { e.preventDefault(); setSearch(text.trim()); setPage(0) }} style={{ display: 'contents' }}>
          <label>Tìm<input value={text} onChange={e => setText(e.target.value)} placeholder="số, diễn giải" /></label>
        </form>
        <button onClick={() => load()} disabled={busy}>Tải lại</button>
        <ExportButton fileName={`Chung_tu_${from}_${to}`} sheet="Chứng từ" disabled={list.length === 0}
          columns={[{ header: 'Ngày HT', width: 12 }, { header: 'Số', width: 16 }, { header: 'Loại', width: 22 }, { header: 'Diễn giải', width: 50 }, { header: 'Số tiền', numeric: true, width: 18 }, { header: 'Trạng thái', width: 14 }]}
          rows={list.map(x => [x.postingDate, x.number, TYPE_VI[x.voucherType] ?? x.voucherType, x.description ?? '', x.totalDebit, STATUS_VI[x.status] ?? x.status])} />
        <button className="primary" onClick={() => setEditing('new')}>Thêm chứng từ</button>
      </div>
      <ErrorBox error={error} />
      {info && !error && <div className={info.includes('Không làm được') ? 'msg error' : 'msg ok'}>{info}</div>}
      {checked.size > 0 && <div className="toolbar panel">
        <strong>Đã chọn {checked.size} chứng từ</strong>
        <button disabled={busy || !count('Draft')} onClick={() => bulk('Đã gửi duyệt', ['Draft'], ['submit'])}>Gửi duyệt ({count('Draft')})</button>
        <button disabled={busy || !count('Submitted')} onClick={() => bulk('Đã duyệt', ['Submitted'], ['approve'])}>Duyệt ({count('Submitted')})</button>
        <button disabled={busy || !count('Approved')} onClick={() => bulk('Đã ghi sổ', ['Approved'], ['post'])}>Ghi sổ ({count('Approved')})</button>
        <button className="primary" disabled={busy || !count('Submitted')} onClick={() => bulk('Đã duyệt và ghi sổ', ['Submitted'], ['approve', 'post'])}>Duyệt và ghi sổ ({count('Submitted')})</button>
        <button disabled={busy} onClick={() => setChecked(new Set())}>Bỏ chọn</button>
        <span className="muted">Làm lần lượt theo ngày; chứng từ nào không qua kiểm tra được liệt kê, các chứng từ khác vẫn tiếp tục.</span>
      </div>}
      {v && (
        <div className="panel">
          <div className="toolbar">
            <strong>{v.number}</strong> <span className={`badge ${v.status}`}>{STATUS_VI[v.status]}</span>
            <span className="muted">{TYPE_VI[v.voucherType] ?? v.voucherType} · {v.postingDate} · {v.description}</span>
            <span style={{ marginLeft: 'auto' }} />
            {v.status === 'Draft' && <button onClick={() => setEditing(v)}>Sửa</button>}
            <button onClick={() => act('Kiểm tra: xem kết quả bên dưới', async () => {
              const errs = await post<{ code: string; message: string; lineNo: number | null }[]>(`/vouchers/${v.id}/validate`)
              setInfo(errs.length ? errs.map(e => `${e.lineNo ? 'Dòng ' + e.lineNo + ': ' : ''}${e.message}`).join(' | ') : 'Hợp lệ, có thể gửi duyệt.')
            })}>Kiểm tra</button>
            {v.status === 'Draft' && <button onClick={() => act('Đã gửi duyệt', () => post(`/vouchers/${v.id}/submit?rowVersion=${v.rowVersion}`))}>Gửi duyệt</button>}
            {v.status === 'Submitted' && <button onClick={() => act('Đã duyệt', () => post(`/vouchers/${v.id}/approve?rowVersion=${v.rowVersion}`))}>Duyệt</button>}
            {(v.status === 'Submitted' || v.status === 'Approved') && <button onClick={() => {
              const reason = prompt('Lý do trả lại:'); if (reason) act('Đã trả lại', () => post(`/vouchers/${v.id}/reject?rowVersion=${v.rowVersion}`, { reason }))
            }}>Trả lại</button>}
            {v.status === 'Approved' && <button className="primary" onClick={() => act('Đã ghi sổ', () => post(`/vouchers/${v.id}/post?rowVersion=${v.rowVersion}`))}>Ghi sổ</button>}
            {v.status === 'Posted' && <button className="danger" onClick={() => {
              const reason = prompt('Lý do đảo bút toán:'); if (!reason) return
              const date = prompt('Ngày đảo (yyyy-mm-dd):', v.postingDate); if (date) act('Đã đảo bút toán', () => post(`/vouchers/${v.id}/reverse`, { reversalDate: date, reason }))
            }}>Đảo</button>}
            {(v.status === 'Draft' || v.status === 'Submitted') && <button className="danger" onClick={() => {
              const reason = prompt('Lý do hủy:'); if (reason) act('Đã hủy', () => post(`/vouchers/${v.id}/cancel?rowVersion=${v.rowVersion}`, { reason }))
            }}>Hủy</button>}
            {['GJ', 'ADJ', 'ALC', 'OPB', 'CLS', 'FXR'].includes(v.voucherType) && <button onClick={() => setEditing({ copyOf: v })}>Sao chép</button>}
            <button onClick={printSheet}>In chứng từ</button>
          </div>
          <LinesTable voucher={v} />
          <VoucherPrint voucher={v} />
        </div>
      )}
      <div className="scroll">
        <table className="grid">
          <thead><tr><th><input type="checkbox" aria-label="Chọn tất cả chứng từ trên trang" checked={list.length > 0 && list.every(x => checked.has(x.id))}
            onChange={e => setChecked(e.target.checked ? new Set(list.map(x => x.id)) : new Set())} /></th>
            <th>Ngày HT</th><th>Số</th><th>Loại</th><th>Diễn giải</th><th className="num">Số tiền</th><th>Trạng thái</th></tr></thead>
          <tbody>
            {list.map(x => (
              <tr key={x.id} className={x.id === v?.id ? 'selected' : ''} onClick={() => setSelected(x)} style={{ cursor: 'pointer' }}>
                <td onClick={e => e.stopPropagation()}><input type="checkbox" aria-label={`Chọn ${x.number}`} checked={checked.has(x.id)}
                  onChange={e => setChecked(c => { const n = new Set(c); if (e.target.checked) n.add(x.id); else n.delete(x.id); return n })} /></td>
                <td>{x.postingDate}</td><td>{x.number}</td><td>{TYPE_VI[x.voucherType] ?? x.voucherType}</td><td>{x.description}</td>
                <td className="num">{formatMoney(x.totalDebit)}</td><td><span className={`badge ${x.status}`}>{STATUS_VI[x.status]}</span></td>
              </tr>
            ))}
            {list.length === 0 && <tr><td colSpan={7} className="muted">Không có chứng từ trong khoảng đã chọn.</td></tr>}
          </tbody>
        </table>
      </div>
      <div className="toolbar">
        <span className="muted">{total === 0 ? '' : `Dòng ${page * PAGE + 1}–${page * PAGE + list.length} / ${total}`}</span>
        <button disabled={busy || page === 0} onClick={() => setPage(p => p - 1)}>← Trang trước</button>
        <button disabled={busy || (page + 1) * PAGE >= total} onClick={() => setPage(p => p + 1)}>Trang sau →</button>
      </div>
    </>
  )
}

const PAGE = 200
const CASH_TYPES = ['CR', 'CP', 'BR', 'BP']

/** Printed voucher (internal layout — the legal forms of the accounting regime are not reproduced until verified). */
function VoucherPrint({ voucher: v }: { voucher: Voucher }) {
  const { accounts, partners } = useCatalog()
  const [company, setCompany] = useState('')
  useEffect(() => { get<{ id: string; name: string }[]>('/me/companies').then(cs => setCompany(cs.find(c => c.id === session.companyId)?.name ?? '')).catch(() => undefined) }, [])
  const acc = (id: string) => accounts.find(a => a.id === id)?.code ?? ''
  const par = (id: string | null) => (id ? partners.find(p => p.id === id) : undefined)
  const d = (s: string) => s.split('-').reverse().join('/')
  if (CASH_TYPES.includes(v.voucherType)) {
    const receipt = v.voucherType === 'CR' || v.voucherType === 'BR'
    const p = par(v.partnerId ?? v.lines.find(l => l.partnerId)?.partnerId ?? null)
    const main = acc(v.mainAccountId ?? '')
    const counter = [...new Set(v.lines.filter(l => l.accountId !== v.mainAccountId).map(l => acc(l.accountId)).filter(Boolean))].join(', ')
    const title = `${receipt ? 'PHIẾU THU' : 'PHIẾU CHI'}${v.voucherType === 'BR' || v.voucherType === 'BP' ? ' (BÁO ' + (receipt ? 'CÓ' : 'NỢ') + ' NGÂN HÀNG)' : ''}`
    return (
      <PrintSheet>
        <div><b>{company}</b></div>
        <h1>{title}</h1>
        <div className="center">Ngày {d(v.voucherDate)} · Số: <b>{v.number}</b></div>
        <div className="center">{receipt ? 'Nợ' : 'Có'}: {main} · {receipt ? 'Có' : 'Nợ'}: {counter}</div>
        {v.status !== 'Posted' && <div className="center" style={{ marginTop: 6 }}><span className="stamp">{STATUS_VI[v.status]?.toUpperCase()} — CHƯA GHI SỔ</span></div>}
        <p>Họ và tên người {receipt ? 'nộp' : 'nhận'} tiền: <b>{p ? p.name : '…………………………………'}</b></p>
        <p>Địa chỉ: …………………………………………………{p?.taxCode ? ` · MST: ${p.taxCode}` : ''}</p>
        <p>Lý do {receipt ? 'nộp' : 'chi'}: {v.description}</p>
        <p>Số tiền: <b>{formatMoney(v.totalDebit)}</b> đồng</p>
        <p>Bằng chữ: <i>{amountInWords(v.totalDebit)}</i></p>
        <p>Kèm theo: ………… chứng từ gốc.</p>
        <div className="signs">
          <div><b>Giám đốc</b><br /><i>(Ký, họ tên, đóng dấu)</i></div><div><b>Kế toán trưởng</b><br /><i>(Ký, họ tên)</i></div>
          <div><b>Người lập phiếu</b><br /><i>(Ký, họ tên)</i></div><div><b>Người {receipt ? 'nộp' : 'nhận'} tiền</b><br /><i>(Ký, họ tên)</i></div>
          {v.voucherType === 'CR' || v.voucherType === 'CP' ? <div><b>Thủ quỹ</b><br /><i>(Ký, họ tên)</i></div> : null}
        </div>
        <div className="foot">Mẫu in nội bộ HAK Accounting — chưa đối chiếu với mẫu chứng từ theo chế độ kế toán [CẦN XÁC MINH]. In lúc {new Date().toLocaleString('vi-VN')}.</div>
      </PrintSheet>
    )
  }
  return (
    <PrintSheet>
      <div><b>{company}</b></div>
      <h1>{(TYPE_VI[v.voucherType] ?? 'Chứng từ kế toán').toUpperCase()}</h1>
      <div className="center">Số: <b>{v.number}</b> · Ngày chứng từ: {d(v.voucherDate)} · Ngày ghi sổ: {d(v.postingDate)}</div>
      {v.status !== 'Posted' && <div className="center" style={{ marginTop: 6 }}><span className="stamp">{STATUS_VI[v.status]?.toUpperCase()} — CHƯA GHI SỔ</span></div>}
      <p>Nội dung: {v.description}</p>
      <table>
        <thead><tr><th>STT</th><th>Diễn giải</th><th>TK Nợ</th><th>TK Có</th><th>Đối tượng</th><th>Số tiền</th></tr></thead>
        <tbody>{v.lines.map(l => {
          const p = par(l.partnerId)
          return <tr key={l.lineNo}><td className="center">{l.lineNo}</td><td>{l.description ?? v.description}</td>
            <td>{l.side === 'D' ? acc(l.accountId) : ''}</td><td>{l.side === 'C' ? acc(l.accountId) : ''}</td>
            <td>{p ? `${p.code} – ${p.name}` : ''}</td><td className="num">{formatMoney(l.amount)}</td></tr>
        })}</tbody>
        <tfoot><tr><td colSpan={5}><b>Cộng</b> (Nợ = Có)</td><td className="num"><b>{formatMoney(v.totalDebit)}</b></td></tr></tfoot>
      </table>
      <p>Bằng chữ: <i>{amountInWords(v.totalDebit)}</i></p>
      <div className="signs"><div><b>Người lập</b><br /><i>(Ký, họ tên)</i></div><div><b>Kế toán trưởng</b><br /><i>(Ký, họ tên)</i></div><div><b>Giám đốc</b><br /><i>(Ký, họ tên, đóng dấu)</i></div></div>
      <div className="foot">Mẫu in nội bộ HAK Accounting — chưa đối chiếu với mẫu chứng từ theo chế độ kế toán [CẦN XÁC MINH]. In lúc {new Date().toLocaleString('vi-VN')}.</div>
    </PrintSheet>
  )
}

function LinesTable({ voucher }: { voucher: Voucher }) {
  const { accounts, partners } = useCatalog()
  const acc = (id: string) => accounts.find(a => a.id === id)?.code ?? id.slice(0, 8)
  const par = (id: string | null) => (id ? partners.find(p => p.id === id)?.code ?? '' : '')
  return (
    <table className="grid">
      <thead><tr><th>#</th><th>Tài khoản</th><th>Đối tượng</th><th>Diễn giải</th><th className="num">Nợ</th><th className="num">Có</th></tr></thead>
      <tbody>
        {voucher.lines.map(l => (
          <tr key={l.lineNo}><td>{l.lineNo}</td><td>{acc(l.accountId)}</td><td>{par(l.partnerId)}</td><td>{l.description}</td>
            <td className="num">{l.side === 'D' ? formatMoney(l.amount) : ''}</td><td className="num">{l.side === 'C' ? formatMoney(l.amount) : ''}</td></tr>
        ))}
      </tbody>
      <tfoot><tr><td colSpan={4}>Cộng</td><td className="num">{formatMoney(voucher.totalDebit)}</td><td className="num">{formatMoney(voucher.totalCredit)}</td></tr></tfoot>
    </table>
  )
}

function VoucherForm({ voucher, template, onDone }: { voucher: Voucher | null; template: Voucher | null; onDone: () => void }) {
  const src = voucher ?? template
  const { accounts, partners } = useCatalog()
  const [type, setType] = useState(src?.voucherType ?? 'GJ')
  const [voucherDate, setVoucherDate] = useState(voucher?.voucherDate ?? today())
  const [postingDate, setPostingDate] = useState(voucher?.postingDate ?? today())
  const [description, setDescription] = useState(src?.description ?? '')
  const [lines, setLines] = useState<LineDraft[]>(src
    ? src.lines.map(l => ({ accountId: l.accountId, side: l.side, amount: formatMoney(l.amount), partnerId: l.partnerId ?? '', description: l.description ?? '', cashFlowCode: l.cashFlowCode ?? '' }))
    : [emptyLine('D'), emptyLine('C')])
  const { busy, error, setError, run } = useAction()

  const parsed = lines.map(l => parseMoney(l.amount))
  const totalD = useMemo(() => addMoney(lines.map((l, i) => (l.side === 'D' ? parsed[i] ?? '0' : '0'))), [lines, parsed])
  const totalC = useMemo(() => addMoney(lines.map((l, i) => (l.side === 'C' ? parsed[i] ?? '0' : '0'))), [lines, parsed])
  const diff = addMoney([totalD, '-' + totalC])

  const update = (i: number, patch: Partial<LineDraft>) => setLines(ls => ls.map((l, j) => (j === i ? { ...l, ...patch } : l)))
  const isCash = (id: string) => { const a = accounts.find(x => x.id === id); return !!a && (a.cashKind !== 'None' || (a.reportTags ?? '').split(',').includes('TDT')) }

  const save = () => run(async () => {
    const bad = parsed.findIndex(p => p == null)
    if (bad >= 0) { setError(new Error(`Dòng ${bad + 1}: số tiền không hợp lệ.`)); return }
    const body = {
      voucherType: type, voucherDate, postingDate, description,
      lines: lines.map((l, i) => ({ accountId: l.accountId, side: l.side, amount: parsed[i], partnerId: l.partnerId || null, description: l.description || null,
        cashFlowCode: isCash(l.accountId) ? l.cashFlowCode || null : null })),
    }
    if (voucher) await put(`/vouchers/${voucher.id}?rowVersion=${voucher.rowVersion}`, body)
    else await post('/vouchers', body)
    onDone()
  })

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.ctrlKey && e.key.toLowerCase() === 's') { e.preventDefault(); save() }
      if (e.key === 'Insert') { e.preventDefault(); setLines(ls => [...ls, emptyLine(ls.at(-1)?.side === 'D' ? 'C' : 'D')]) }
      if (e.key === 'Escape') onDone()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  })

  return (
    <>
      <div className="toolbar">
        <h2>{voucher ? `Sửa chứng từ ${voucher.number}` : template ? `Sao chép từ ${template.number}` : 'Chứng từ mới'}</h2>
        <span className="muted"><kbd>Ctrl+S</kbd> lưu · <kbd>Insert</kbd> thêm dòng · <kbd>Esc</kbd> đóng</span>
        <button onClick={onDone}>Đóng</button>
        <button className="primary" onClick={save} disabled={busy}>Lưu nháp</button>
      </div>
      <ErrorBox error={error} />
      <div className="panel">
        <div className="form-row">
          <label>Loại
            <select value={type} onChange={e => setType(e.target.value)} disabled={!!voucher}>
              {['GJ', 'ADJ', 'ALC', 'OPB', 'CLS', 'FXR'].map(t => <option key={t} value={t}>{t} – {TYPE_VI[t]}</option>)}
            </select>
          </label>
          <label>Ngày chứng từ<input type="date" value={voucherDate} onChange={e => setVoucherDate(e.target.value)} /></label>
          <label>Ngày hạch toán<input type="date" value={postingDate} onChange={e => setPostingDate(e.target.value)} /></label>
          <label style={{ flex: 1, minWidth: 240 }}>Diễn giải<input value={description} onChange={e => setDescription(e.target.value)} /></label>
        </div>
        <table className="grid">
          <thead><tr><th>#</th><th>Nợ/Có</th><th>Tài khoản</th><th>Đối tượng</th><th className="num">Số tiền</th><th>Diễn giải dòng</th>
            <th title="Mã số trên Báo cáo lưu chuyển tiền tệ (B03-DN). Để trống: phần mềm tự xác định theo TK đối ứng">Mã LCTT</th><th /></tr></thead>
          <tbody>
            {lines.map((l, i) => (
              <tr key={i}>
                <td>{i + 1}</td>
                <td><select value={l.side} onChange={e => update(i, { side: e.target.value as 'D' | 'C' })} aria-label="Nợ/Có"><option value="D">Nợ</option><option value="C">Có</option></select></td>
                <td><AccountSelect accounts={accounts} value={l.accountId} onChange={id => update(i, { accountId: id })} ariaLabel="Tài khoản" /></td>
                <td><PartnerSelect partners={partners} value={l.partnerId} onChange={id => update(i, { partnerId: id })} /></td>
                <td><input className="num" value={l.amount} aria-label="Số tiền" aria-invalid={l.amount !== '' && parsed[i] == null}
                  onChange={e => update(i, { amount: e.target.value })}
                  onBlur={() => { const p = parseMoney(l.amount); if (p) update(i, { amount: formatMoney(p) }) }} /></td>
                <td><input value={l.description} onChange={e => update(i, { description: e.target.value })} aria-label="Diễn giải dòng" /></td>
                <td>{isCash(l.accountId) && <select value={l.cashFlowCode} onChange={e => update(i, { cashFlowCode: e.target.value })} aria-label="Mã luồng tiền">
                  <option value="">Tự động</option>{Object.entries(CASHFLOW_VI).map(([k, v]) => <option key={k} value={k}>{k} – {v}</option>)}</select>}</td>
                <td><button onClick={() => setLines(ls => ls.filter((_, j) => j !== i))} aria-label="Xóa dòng">✕</button></td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr><td colSpan={4}>Tổng Nợ / Tổng Có</td><td className="num">{formatMoney(totalD)} / {formatMoney(totalC)}</td>
              <td colSpan={3} style={{ color: diff === '0' ? 'var(--ok)' : 'var(--danger)' }}>{diff === '0' ? 'Cân bằng' : `Chênh lệch ${formatMoney(diff)}`}</td></tr>
          </tfoot>
        </table>
        <button style={{ marginTop: 8 }} onClick={() => setLines(ls => [...ls, emptyLine('D')])}>+ Thêm dòng</button>
        {diff !== '0' && <button style={{ marginTop: 8, marginLeft: 8 }} onClick={() => {
          // Add a line on the lighter side for exactly the difference (decimal strings only).
          const side: 'D' | 'C' = diff.startsWith('-') ? 'D' : 'C'
          setLines(ls => [...ls, { ...emptyLine(side), amount: formatMoney(diff.replace('-', '')) }])
        }}>+ Dòng cân đối ({formatMoney(diff.replace('-', ''))})</button>}
      </div>
    </>
  )
}
