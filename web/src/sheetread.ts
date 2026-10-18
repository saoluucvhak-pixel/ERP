// Reads the first sheet of an .xlsx file, or a .csv / .txt file, into rows of text cells — no external library.
// Numbers stay text: a numeric xlsx cell gives its stored decimal text (Excel's 15-significant-digit precision), never a JS
// float in between. The server parses and validates every value; this only turns a file into a grid.

const dec = new TextDecoder('utf-8')

async function inflateRaw(data: Uint8Array): Promise<Uint8Array> {
  const stream = new Blob([data as Uint8Array<ArrayBuffer>]).stream().pipeThrough(new DecompressionStream('deflate-raw'))
  return new Uint8Array(await new Response(stream).arrayBuffer())
}

/** Entries of a zip archive (STORE or DEFLATE), read from the central directory. */
export async function readZip(bytes: Uint8Array): Promise<Map<string, Uint8Array>> {
  const v = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength)
  let end = -1
  for (let i = bytes.length - 22; i >= Math.max(0, bytes.length - 22 - 65535); i--)
    if (v.getUint32(i, true) === 0x06054b50) { end = i; break }
  if (end < 0) throw new Error('Tệp không phải .xlsx hợp lệ (không đọc được cấu trúc zip).')
  const count = v.getUint16(end + 10, true)
  let p = v.getUint32(end + 16, true)
  const out = new Map<string, Uint8Array>()
  for (let n = 0; n < count; n++) {
    if (v.getUint32(p, true) !== 0x02014b50) throw new Error('Tệp .xlsx bị hỏng.')
    const method = v.getUint16(p + 10, true)
    const size = v.getUint32(p + 20, true)
    const nameLen = v.getUint16(p + 28, true), extraLen = v.getUint16(p + 30, true), commentLen = v.getUint16(p + 32, true)
    const local = v.getUint32(p + 42, true)
    const name = dec.decode(bytes.subarray(p + 46, p + 46 + nameLen))
    const start = local + 30 + v.getUint16(local + 26, true) + v.getUint16(local + 28, true)
    const raw = bytes.subarray(start, start + size)
    if (method === 0) out.set(name, raw)
    else if (method === 8) out.set(name, await inflateRaw(raw))
    p += 46 + nameLen + extraLen + commentLen
  }
  return out
}

const unescapeXml = (s: string) => s.replace(/&(lt|gt|amp|quot|apos|#\d+|#x[0-9a-fA-F]+);/g, (_, e: string) =>
  e === 'lt' ? '<' : e === 'gt' ? '>' : e === 'amp' ? '&' : e === 'quot' ? '"' : e === 'apos' ? "'"
    : String.fromCodePoint(e[1] === 'x' ? parseInt(e.slice(2), 16) : parseInt(e.slice(1), 10)))

/** Concatenated text of the <t> runs of a shared / inline string, skipping phonetic hints (<rPh>). */
const textOf = (xml: string) => [...xml.replace(/<rPh\b[\s\S]*?<\/rPh>/g, '').matchAll(/<t\b[^>]*>([\s\S]*?)<\/t>/g)].map(m => unescapeXml(m[1])).join('')

function colIndex(ref: string): number {
  let n = 0
  for (const ch of ref.replace(/\d+$/, '')) n = n * 26 + (ch.charCodeAt(0) - 64)
  return n - 1
}

/** Numeric cell text as a plain decimal string, limited to Excel's 15 significant digits ("1234.5599999999999" → "1234.56"). */
export function numberText(v: string): string {
  if (!/[eE]/.test(v) && v.replace(/[-.]/g, '').replace(/^0+/, '').length <= 15) return v
  const n = Number(v)
  if (!Number.isFinite(n)) return v
  let s = n.toPrecision(15)
  if (/e/.test(s)) s = n.toLocaleString('en-US', { useGrouping: false, maximumFractionDigits: 20 })
  return s.includes('.') ? s.replace(/0+$/, '').replace(/\.$/, '') : s
}

const xmlOf = (files: Map<string, Uint8Array>, name: string) => { const f = files.get(name); return f ? dec.decode(f) : undefined }

export async function readXlsx(bytes: Uint8Array): Promise<string[][]> {
  const files = await readZip(bytes)
  const shared = [...(xmlOf(files, 'xl/sharedStrings.xml') ?? '').matchAll(/<si>([\s\S]*?)<\/si>/g)].map(m => textOf(m[1]))
  // First sheet in workbook order → its part via the workbook relationships.
  let path = 'xl/worksheets/sheet1.xml'
  const rid = /<sheet\b[^>]*\br:id="([^"]+)"/.exec(xmlOf(files, 'xl/workbook.xml') ?? '')?.[1]
  const target = rid && new RegExp(`<Relationship\\b[^>]*Id="${rid}"[^>]*Target="([^"]+)"`).exec(xmlOf(files, 'xl/_rels/workbook.xml.rels') ?? '')?.[1]
  if (target) path = target.startsWith('/') ? target.slice(1) : `xl/${target}`
  const sheet = xmlOf(files, path)
  if (!sheet) throw new Error('Không tìm thấy trang tính đầu tiên trong tệp .xlsx.')
  const rows: string[][] = []
  for (const r of sheet.matchAll(/<row\b([^>]*)>([\s\S]*?)<\/row>/g)) {
    const rowNo = Number(/\br="(\d+)"/.exec(r[1])?.[1] ?? rows.length + 1)
    const cells: string[] = []
    let next = 0
    for (const c of r[2].matchAll(/<c\b([^>]*?)(?:\/>|>([\s\S]*?)<\/c>)/g)) {
      const attrs = c[1], body = c[2] ?? ''
      const ref = /\br="([A-Z]+\d+)"/.exec(attrs)?.[1]
      const col = ref ? colIndex(ref) : next
      next = col + 1
      const t = /\bt="([^"]+)"/.exec(attrs)?.[1]
      const vv = /<v>([\s\S]*?)<\/v>/.exec(body)?.[1]
      let text = ''
      if (t === 's') text = shared[Number(vv)] ?? ''
      else if (t === 'inlineStr') text = textOf(body)
      else if (t === 'str' || t === 'e') text = unescapeXml(vv ?? '')
      else if (t === 'b') text = vv === '1' ? '1' : '0'
      else if (vv != null) text = numberText(vv)
      cells[col] = text
    }
    while (rows.length < rowNo - 1) rows.push([])
    rows.push(Array.from(cells, x => x ?? ''))
  }
  return rows
}

/** CSV / tab-separated text: delimiter guessed from the first line (; , or tab), quotes per RFC 4180, BOM removed. */
export function parseCsv(text: string): string[][] {
  if (text.charCodeAt(0) === 0xfeff) text = text.slice(1)
  const firstLine = text.split(/\r?\n/, 1)[0] ?? ''
  const counts = [';', ',', '\t'].map(d => [d, firstLine.split(d).length] as const)
  const delim = counts.sort((a, b) => b[1] - a[1])[0][0]
  const rows: string[][] = []
  let row: string[] = [], cell = '', quoted = false
  for (let i = 0; i < text.length; i++) {
    const ch = text[i]
    if (quoted) {
      if (ch === '"' && text[i + 1] === '"') { cell += '"'; i++ }
      else if (ch === '"') quoted = false
      else cell += ch
    } else if (ch === '"' && cell === '') quoted = true
    else if (ch === delim) { row.push(cell); cell = '' }
    else if (ch === '\n' || ch === '\r') {
      if (ch === '\r' && text[i + 1] === '\n') i++
      row.push(cell); rows.push(row); row = []; cell = ''
    } else cell += ch
  }
  if (cell !== '' || row.length) { row.push(cell); rows.push(row) }
  return rows
}

/** Reads a user-chosen file: .xlsx by content signature, anything else as UTF-8 CSV. */
export async function readSheetFile(file: File): Promise<string[][]> {
  const bytes = new Uint8Array(await file.arrayBuffer())
  if (bytes[0] === 0x50 && bytes[1] === 0x4b) return readXlsx(bytes)
  if (/\.xls$/i.test(file.name)) throw new Error('Tệp .xls (Excel 97–2003) chưa hỗ trợ; lưu lại dưới dạng .xlsx hoặc CSV UTF-8.')
  return parseCsv(dec.decode(bytes))
}

/** Lower-case, no Vietnamese diacritics, single spaces — for matching column headers. */
export const headerKey = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/đ/gi, 'd').toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim()

/** Excel date serial (1900 system) → yyyy-MM-dd; other text unchanged. */
export function excelDate(v: string): string {
  if (!/^\d{4,5}(\.\d+)?$/.test(v)) return v
  const d = new Date(Date.UTC(1899, 11, 30) + Math.floor(Number(v)) * 86400000)
  return d.toISOString().slice(0, 10)
}
