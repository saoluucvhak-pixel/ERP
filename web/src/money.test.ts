import { test } from 'node:test'
import assert from 'node:assert/strict'
import { addMoney, amountInWords, csvCell, daysBefore, formatMoney, optionalRate, parseMoney, parseRate, today, formatDate, formatDateTime, formatMonth } from './money.ts'

test('formatMoney groups with dots and uses comma decimals, never via Number', () => {
  assert.equal(formatMoney('1234567'), '1.234.567')
  assert.equal(formatMoney('1234567.5000'), '1.234.567,5')
  assert.equal(formatMoney('-1000'), '-1.000')
  assert.equal(formatMoney('99999999999999.9999'), '99.999.999.999.999,9999')
  assert.equal(formatMoney('0'), '0')
})

test('parseMoney accepts Vietnamese and canonical input, rejects garbage', () => {
  assert.equal(parseMoney('1.234.567'), '1234567')
  assert.equal(parseMoney('1.234.567,5'), '1234567.5')
  assert.equal(parseMoney('1234567.5'), '1234567.5')
  assert.equal(parseMoney(' 12 000 '), '12000')
  assert.equal(parseMoney('0012'), '12')
  assert.equal(parseMoney('1,23456'), null)
  assert.equal(parseMoney('-5'), null)
  assert.equal(parseMoney('abc'), null)
  assert.equal(parseMoney(''), null)
})

test('addMoney is exact where floating point is not', () => {
  assert.equal(addMoney(['0.1', '0.2']), '0.3')
  assert.notEqual(0.1 + 0.2, 0.3)
  assert.equal(addMoney(['99999999999999.9999', '0.0001']), '100000000000000')
  assert.equal(addMoney(['1000', '-1000']), '0')
  assert.equal(addMoney(['5', '-7.25']), '-2.25')
})

test('parseRate allows 6 decimals with the same separators as money', () => {
  assert.equal(parseRate('25.300'), '25300')
  assert.equal(parseRate('25300,123456'), '25300.123456')
  assert.equal(parseRate('25.133,333333'), '25133.333333')
  assert.equal(parseRate('1.0850'), '1.0850')
  assert.equal(parseRate('1,1234567'), null)
  assert.equal(parseRate('0'), '0')
  assert.equal(parseRate('-1'), null)
})

test('today is the local calendar date, not the UTC one', () => {
  const d = new Date()
  assert.equal(today(), `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`)
})
test('amountInWords reads Vietnamese money', () => {
  assert.equal(amountInWords('0'), 'Không đồng')
  assert.equal(amountInWords('1250000'), 'Một triệu hai trăm năm mươi nghìn đồng')
  assert.equal(amountInWords('105'), 'Một trăm linh năm đồng')
  assert.equal(amountInWords('21'), 'Hai mươi mốt đồng')
  assert.equal(amountInWords('15'), 'Mười lăm đồng')
  assert.equal(amountInWords('25'), 'Hai mươi lăm đồng')
  assert.equal(amountInWords('11'), 'Mười một đồng')
  assert.equal(amountInWords('1005000'), 'Một triệu không trăm linh năm nghìn đồng')
  assert.equal(amountInWords('1000000000'), 'Một tỷ đồng')
  assert.equal(amountInWords('12345678901'), 'Mười hai tỷ ba trăm bốn mươi lăm triệu sáu trăm bảy mươi tám nghìn chín trăm linh một đồng')
  assert.equal(amountInWords('2000000000000'), 'Hai nghìn tỷ đồng')
  assert.equal(amountInWords('-30'), 'Âm ba mươi đồng')
  assert.equal(amountInWords('12.50', 'đô la Mỹ'), 'Mười hai phẩy năm đô la Mỹ')
})
test('formatDate hiển thị dd/MM/yyyy, giữ nguyên chuỗi không phải ngày', () => {
  assert.equal(formatDate('2026-10-19'), '19/10/2026')
  assert.equal(formatDate(''), '')
  assert.equal(formatDate(null), '')
  assert.equal(formatDate('không có'), 'không có')
})

test('formatMonth và formatDateTime', () => {
  assert.equal(formatMonth('2026-10-19'), '10/2026')
  assert.equal(formatMonth('2026-03'), '03/2026')
  assert.match(formatDateTime('2026-10-19T08:05:00'), /^19\/10\/2026 08:05$/)
  assert.equal(formatDateTime('2026-10-19'), '19/10/2026')
})

test('parseMoney/parseRate reject ambiguous or malformed input', () => {
  for (const bad of ['1.2.3,45', '12.34,56', '1,2,3', '1,234.56', '1..2', '1.234.56', '12 34', '.5', '5.', ',5', '1e3', '--1'])
    assert.equal(parseMoney(bad), null, bad)
  assert.equal(parseRate('1.2.3,45'), null)
  assert.equal(parseRate(''), null)
})

test('parseMoney round-trips formatMoney and handles edge values', () => {
  for (const v of ['0', '1', '999', '1000', '1234567.5', '123456789012345678.1234', '0.0001'])
    assert.equal(parseMoney(formatMoney(v)), v, v)
  assert.equal(parseMoney('0'), '0')
  assert.equal(parseMoney('1 234 567,5'), '1234567.5')
  assert.equal(parseMoney('0,5'), '0.5')
})

test('optionalRate: để trống là không có, nhập sai thì báo lỗi chứ không lặng lẽ thành thuế 0', () => {
  assert.equal(optionalRate(''), null)
  assert.equal(optionalRate('   '), null)
  assert.equal(optionalRate('10'), '10')
  assert.equal(optionalRate('8,5'), '8.5')
  assert.throws(() => optionalRate('10%'), /không hợp lệ/)
  assert.throws(() => optionalRate('abc', 'Dung sai'), /Dung sai/)
})

test('daysBefore: ngày hợp lệ lùi đúng, ô ngày bị xóa thì null (không ném lỗi làm trắng màn hình)', () => {
  assert.equal(daysBefore('2026-10-21', 60), '2026-08-22')
  assert.equal(daysBefore('2026-03-01', 1), '2026-02-28')
  assert.equal(daysBefore('2024-03-01', 1), '2024-02-29')
  assert.equal(daysBefore('', 60), null)
  assert.equal(daysBefore('21/10/2026', 60), null)
})

test('csvCell: vô hiệu hóa ô bắt đầu bằng ký tự công thức và thoát dấu ngoặc kép', () => {
  assert.equal(csvCell('Nguyễn Văn A'), '"Nguyễn Văn A"')
  assert.equal(csvCell('=HYPERLINK("http://x","a")'), '"\'=HYPERLINK(""http://x"",""a"")"')
  assert.equal(csvCell('+1'), '"\'+1"')
  assert.equal(csvCell('-2'), '"\'-2"')
  assert.equal(csvCell('@SUM(A1)'), '"\'@SUM(A1)"')
  assert.equal(csvCell('a=b'), '"a=b"')
  assert.equal(csvCell('Chuyển "gấp"'), '"Chuyển ""gấp"""')
})
