import { test } from 'node:test'
import assert from 'node:assert/strict'
import { buildXlsx, crc32, zipStore } from './xlsx.ts'

const enc = new TextEncoder()
const dec = new TextDecoder()

test('crc32 matches the reference value', () => {
  assert.equal(crc32(enc.encode('123456789')), 0xcbf43926)
})

test('zipStore writes local headers, central directory and end record', () => {
  const zip = zipStore([['a.txt', enc.encode('xin chào')]])
  const view = new DataView(zip.buffer)
  assert.equal(view.getUint32(0, true), 0x04034b50)
  assert.equal(view.getUint32(zip.length - 22, true), 0x06054b50)
  assert.equal(view.getUint16(zip.length - 22 + 10, true), 1)
  assert.ok(dec.decode(zip).includes('xin chào'))
})

test('workbook keeps exact decimal text for numeric cells and escapes text', () => {
  const xlsx = dec.decode(buildXlsx('BCĐPS', [{ header: 'TK' }, { header: 'Số tiền', numeric: true }],
    [['111 <Tiền & mặt>', '12345678901234.5678'], ['112', '-7.25'], ['113', null], ['114', 'không phải số']]))
  assert.ok(xlsx.includes('<v>12345678901234.5678</v>'))
  assert.ok(xlsx.includes('<v>-7.25</v>'))
  assert.ok(xlsx.includes('111 &lt;Tiền &amp; mặt&gt;'))
  assert.ok(xlsx.includes('không phải số'))       // non-numeric text in a numeric column stays text
  assert.ok(xlsx.includes('name="BCĐPS"'))
})
