// Đọc file XML hóa đơn điện tử (dữ liệu tải từ hệ thống HĐĐT) thành dạng nhập trung tính của HAK (schemas/e-invoice-import).
// Tên thẻ theo cấu trúc HDon/DLHDon thường gặp (TTChung, NDHDon/NBan/NMua, TToan). [CẦN XÁC MINH trên file thật của từng nhà cung cấp]:
// kết quả luôn được hiển thị để người dùng xem lại trước khi nhập; thẻ nào không tìm thấy sẽ báo lỗi, không đoán.
// Tiền là chuỗi thập phân, không bao giờ qua số thực.

export interface EInvoiceJson {
  direction: 'Input' | 'Output'; sellerTaxCode: string; sellerName: string; buyerTaxCode: string | null; buyerName: string | null
  series: string; number: string; issueDate: string; currency: string | null; exchangeRate: string | null
  amountBeforeVat: string; vatAmount: string; totalPayment: string; status: 'Original' | 'Replacement' | 'Adjustment'
  relatedSeries: string | null; relatedNumber: string | null; lookupCode: string | null
  rates: { vatRate: string | null; amountBeforeVat: string; vatAmount: string }[]
}

interface XNode { name: string; text: string; children: XNode[] }

/** Minimal XML reader: element tree with namespace prefixes removed; attributes, comments, CDATA wrappers and declarations are skipped. */
export function parseXml(xml: string): XNode {
  const root: XNode = { name: '#root', text: '', children: [] }
  const stack: XNode[] = [root]
  const re = /<!--[\s\S]*?-->|<\?[\s\S]*?\?>|<!\[CDATA\[([\s\S]*?)\]\]>|<(\/?)([A-Za-z_][\w.\-:]*)((?:"[^"]*"|'[^']*'|[^>"'])*?)(\/?)>|([^<]+)/g
  let m: RegExpExecArray | null
  while ((m = re.exec(xml))) {
    const top = stack[stack.length - 1]
    if (m[1] !== undefined) top.text += m[1]
    else if (m[6] !== undefined) top.text += unescapeXml(m[6])
    else if (m[3] !== undefined) {
      const name = m[3].includes(':') ? m[3].slice(m[3].indexOf(':') + 1) : m[3]
      if (m[2]) { if (stack.length > 1 && top.name === name) stack.pop() }
      else { const n: XNode = { name, text: '', children: [] }; top.children.push(n); if (!m[5]) stack.push(n) }
    }
  }
  return root.children[0] ?? root
}

const unescapeXml = (s: string) => s.replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&amp;/g, '&')
const kids = (n: XNode | undefined, name: string) => (n ? n.children.filter(c => c.name === name) : [])
const child = (n: XNode | undefined, name: string) => kids(n, name)[0]
const path = (n: XNode | undefined, ...names: string[]) => names.reduce<XNode | undefined>((cur, nm) => child(cur, nm), n)
const val = (n: XNode | undefined) => { const t = n?.text.trim(); return t ? t : null }
/** First descendant (depth-first) with this tag, for fields that sit at different depths between providers. */
function find(n: XNode | undefined, name: string): XNode | undefined {
  if (!n) return undefined
  for (const c of n.children) { if (c.name === name) return c; const d = find(c, name); if (d) return d }
  return undefined
}

const MONEY = /^-?\d+(\.\d{1,4})?$/
const money = (s: string | null): string | null => {
  if (s == null) return null
  const t = s.replace(/\s/g, '')
  if (!MONEY.test(t)) return null
  const neg = t.startsWith('-'); const [i, f] = (neg ? t.slice(1) : t).split('.')
  const core = i.replace(/^0+(?=\d)/, '') + (f && /[1-9]/.test(f) ? '.' + f.replace(/0+$/, '') : '')
  return (neg && core !== '0' ? '-' : '') + core
}
const addStr = (vals: string[]) => {
  let total = 0n
  for (const v of vals) { const neg = v.startsWith('-'); const [i, f = ''] = (neg ? v.slice(1) : v).split('.'); const x = BigInt(i) * 10000n + BigInt((f + '0000').slice(0, 4)); total += neg ? -x : x }
  const neg = total < 0n; const a = neg ? -total : total; const fr = (a % 10000n).toString().padStart(4, '0').replace(/0+$/, '')
  return (neg ? '-' : '') + (a / 10000n).toString() + (fr ? '.' + fr : '')
}

function isoDate(s: string | null): string | null {
  if (!s) return null
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(s.trim()) ?? (() => { const d = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(s.trim()); return d ? [d[0], d[3], d[2], d[1]] : null })()
  return m ? `${m[1]}-${m[2]}-${m[3]}` : null
}

function rateOf(s: string | null): string | null {
  if (!s) return null
  const t = s.replace('%', '').replace(',', '.').trim()
  return /^\d+(\.\d+)?$/.test(t) ? t.replace(/^0+(?=\d)/, '').replace(/\.0+$/, '') : null // KCT, KKKNT… → không có thuế suất số
}

/** Convert one invoice XML; returns the invoice or a list of Vietnamese error messages. */
export function eInvoiceFromXml(xml: string, direction: 'Input' | 'Output'): { invoice?: EInvoiceJson; errors: string[] } {
  const errors: string[] = []
  let root: XNode
  try { root = parseXml(xml) } catch { return { errors: ['Không đọc được XML.'] } }
  const dl = find(root, 'DLHDon') ?? root
  const tt = find(dl, 'TTChung')
  const seller = path(dl, 'NDHDon', 'NBan') ?? find(dl, 'NBan')
  const buyer = path(dl, 'NDHDon', 'NMua') ?? find(dl, 'NMua')
  const pay = path(dl, 'NDHDon', 'TToan') ?? find(dl, 'TToan')
  const need = <T,>(v: T | null | undefined, label: string) => { if (v == null) errors.push(`Thiếu ${label}.`); return v ?? null }
  const series = need(val(child(tt, 'KHHDon')), 'ký hiệu hóa đơn (KHHDon)')
  const number = need(val(child(tt, 'SHDon')), 'số hóa đơn (SHDon)')
  const issueDate = need(isoDate(val(child(tt, 'NLap'))), 'ngày lập (NLap)')
  const sellerTaxCode = need(val(child(seller, 'MST')), 'MST người bán')
  const sellerName = need(val(child(seller, 'Ten')), 'tên người bán')
  const rates = kids(find(pay, 'THTTLTSuat'), 'LTSuat').map(l => ({
    vatRate: rateOf(val(child(l, 'TSuat'))), amountBeforeVat: money(val(child(l, 'ThTien'))), vatAmount: money(val(child(l, 'TThue'))),
  }))
  if (rates.some(r => r.amountBeforeVat == null || r.vatAmount == null)) errors.push('Bảng thuế suất có số tiền không hợp lệ.')
  const okRates = rates.filter((r): r is { vatRate: string | null; amountBeforeVat: string; vatAmount: string } => r.amountBeforeVat != null && r.vatAmount != null)
  const before = money(val(child(pay, 'TgTCThue'))) ?? (okRates.length ? addStr(okRates.map(r => r.amountBeforeVat)) : null)
  const vat = money(val(child(pay, 'TgTThue'))) ?? (okRates.length ? addStr(okRates.map(r => r.vatAmount)) : null)
  const total = money(val(child(pay, 'TgTTTBSo'))) ?? (before != null && vat != null ? addStr([before, vat]) : null)
  need(before, 'tổng tiền chưa thuế (TgTCThue)'); need(vat, 'tổng tiền thuế (TgTThue)'); need(total, 'tổng thanh toán (TgTTTBSo)')
  if (before != null && vat != null && okRates.length && (addStr(okRates.map(r => r.amountBeforeVat)) !== before || addStr(okRates.map(r => r.vatAmount)) !== vat))
    errors.push('Tổng theo từng thuế suất không khớp tổng trên hóa đơn.')
  const nature = val(find(tt, 'TCHDon'))
  const status = nature === '1' ? 'Replacement' : nature === '2' ? 'Adjustment' : 'Original'
  if (errors.length || series == null || number == null || issueDate == null || sellerTaxCode == null || sellerName == null || before == null || vat == null || total == null) return { errors }
  const rate = val(child(tt, 'TGia'))
  return { errors, invoice: {
    direction, sellerTaxCode, sellerName, buyerTaxCode: val(child(buyer, 'MST')), buyerName: val(child(buyer, 'Ten')),
    series, number, issueDate, currency: val(child(tt, 'DVTTe')) === 'VND' ? null : val(child(tt, 'DVTTe')), exchangeRate: money(rate),
    amountBeforeVat: before, vatAmount: vat, totalPayment: total, status,
    relatedSeries: val(find(tt, 'KHHDCLQuan')), relatedNumber: val(find(tt, 'SHDCLQuan')), lookupCode: val(find(root, 'MCCQT')),
    rates: okRates.length ? okRates : [{ vatRate: null, amountBeforeVat: before, vatAmount: vat }],
  } }
}
