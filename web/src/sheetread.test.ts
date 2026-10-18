import { test } from 'node:test'
import assert from 'node:assert/strict'
import { deflateRawSync } from 'node:zlib'
import { buildXlsx, crc32 } from './xlsx.ts'
import { excelDate, headerKey, numberText, parseCsv, readXlsx, readZip } from './sheetread.ts'

const enc = new TextEncoder()

/** Zip with DEFLATE entries, as Excel writes them. */
function zipDeflate(entries: [string, string][]): Uint8Array {
  const parts: Uint8Array[] = [], central: Uint8Array[] = []
  let offset = 0
  for (const [name, text] of entries) {
    const data = enc.encode(text), packed = new Uint8Array(deflateRawSync(data)), nameBytes = enc.encode(name)
    const local = new DataView(new ArrayBuffer(30))
    local.setUint32(0, 0x04034b50, true); local.setUint16(8, 8, true); local.setUint32(14, crc32(data), true)
    local.setUint32(18, packed.length, true); local.setUint32(22, data.length, true); local.setUint16(26, nameBytes.length, true)
    parts.push(new Uint8Array(local.buffer), nameBytes, packed)
    const cen = new DataView(new ArrayBuffer(46))
    cen.setUint32(0, 0x02014b50, true); cen.setUint16(10, 8, true); cen.setUint32(16, crc32(data), true)
    cen.setUint32(20, packed.length, true); cen.setUint32(24, data.length, true); cen.setUint16(28, nameBytes.length, true); cen.setUint32(42, offset, true)
    central.push(new Uint8Array(cen.buffer), nameBytes)
    offset += 30 + nameBytes.length + packed.length
  }
  const size = central.reduce((n, p) => n + p.length, 0)
  const end = new DataView(new ArrayBuffer(22))
  end.setUint32(0, 0x06054b50, true); end.setUint16(8, entries.length, true); end.setUint16(10, entries.length, true)
  end.setUint32(12, size, true); end.setUint32(16, offset, true)
  const all = [...parts, ...central, new Uint8Array(end.buffer)]
  const out = new Uint8Array(all.reduce((n, p) => n + p.length, 0))
  let pos = 0
  for (const p of all) { out.set(p, pos); pos += p.length }
  return out
}

test('reads back a workbook written by buildXlsx (inline strings, numbers, gaps)', async () => {
  const bytes = buildXlsx('TK', [{ header: 'Mã' }, { header: 'Tên' }, { header: 'Số tiền', numeric: true }],
    [['111', 'Tiền mặt & quỹ', '1500000.25'], ['', null, '-3']])
  assert.deepEqual(await readXlsx(bytes), [['Mã', 'Tên', 'Số tiền'], ['111', 'Tiền mặt & quỹ', '1500000.25'], ['', '', '-3']])
})

test('reads an Excel-style deflated workbook with shared strings and the first sheet from the relationships', async () => {
  const zip = zipDeflate([
    ['xl/workbook.xml', '<workbook xmlns:r="r"><sheets><sheet name="Số dư" sheetId="3" r:id="rId7"/></sheets></workbook>'],
    ['xl/_rels/workbook.xml.rels', '<Relationships><Relationship Id="rId7" Type="ws" Target="worksheets/sheet3.xml"/></Relationships>'],
    ['xl/sharedStrings.xml', '<sst><si><t>Mã TK</t></si><si><r><t>Khách </t></r><r><t xml:space="preserve">A&amp;B</t></r><rPh><t>x</t></rPh></si></sst>'],
    ['xl/worksheets/sheet3.xml', '<worksheet><sheetData><row r="1"><c r="A1" t="s"><v>0</v></c><c r="C1" t="s"><v>1</v></c></row>' +
      '<row r="3"><c r="A3"><v>1234.5599999999999</v></c><c r="B3" t="b"><v>1</v></c><c r="C3" t="str"><v>=x</v></c></row></sheetData></worksheet>'],
  ])
  assert.equal((await readZip(zip)).size, 4)
  assert.deepEqual(await readXlsx(zip), [['Mã TK', '', 'Khách A&B'], [], ['1234.56', '1', '=x']])
})

test('CSV: delimiter detection, quotes, CRLF, BOM', () => {
  assert.deepEqual(parseCsv('﻿Mã;Tên;Nợ\r\n131;"Công ty ""A""; chi nhánh";1.000.000\r\n'), [['Mã', 'Tên', 'Nợ'], ['131', 'Công ty "A"; chi nhánh', '1.000.000']])
  assert.deepEqual(parseCsv('a,b\n"x\ny",2'), [['a', 'b'], ['x\ny', '2']])
})

test('helpers: header keys, Excel number precision, date serials', () => {
  assert.equal(headerKey('  Mã đối tượng (*) '), 'ma doi tuong')
  assert.equal(headerKey('Số dư Nợ'), 'so du no')
  assert.equal(numberText('1500000'), '1500000')
  assert.equal(numberText('0.30000000000000004'), '0.3')
  assert.equal(numberText('1E-3'), '0.001')
  assert.equal(excelDate('46023'), '2026-01-01')
  assert.equal(excelDate('31/01/2026'), '31/01/2026')
})
