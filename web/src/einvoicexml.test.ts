import assert from 'node:assert/strict'
import { test } from 'node:test'
import { eInvoiceFromXml } from './einvoicexml.ts'

const xml = (extra = '', toan = '<TgTCThue>3000000</TgTCThue><TgTThue>160000</TgTThue><TgTTTBSo>3160000</TgTTTBSo>') => `<?xml version="1.0" encoding="UTF-8"?>
<HDon xmlns:ds="http://www.w3.org/2000/09/xmldsig#"><DLHDon Id="x"><TTChung><PBan>2.0.0</PBan><KHHDon>C26TAA</KHHDon><SHDon>123</SHDon><NLap>2026-10-05</NLap><DVTTe>VND</DVTTe>${extra}</TTChung>
<NDHDon><NBan><Ten>Cong ty A &amp; B</Ten><MST>0401234567</MST></NBan><NMua><Ten>HAK</Ten><MST>0409999999</MST></NMua>
<TToan><THTTLTSuat><LTSuat><TSuat>8%</TSuat><ThTien>2000000</ThTien><TThue>160000</TThue></LTSuat><LTSuat><TSuat>KCT</TSuat><ThTien>1000000</ThTien><TThue>0</TThue></LTSuat></THTTLTSuat>${toan}</TToan></NDHDon></DLHDon>
<ds:Signature><ds:SignedInfo/></ds:Signature></HDon>`

test('đọc hóa đơn thường: tiền là chuỗi, thuế suất 8% và KCT', () => {
  const { invoice, errors } = eInvoiceFromXml(xml(), 'Input')
  assert.deepEqual(errors, [])
  assert.equal(invoice?.series, 'C26TAA'); assert.equal(invoice?.number, '123'); assert.equal(invoice?.issueDate, '2026-10-05')
  assert.equal(invoice?.sellerName, 'Cong ty A & B'); assert.equal(invoice?.sellerTaxCode, '0401234567')
  assert.equal(invoice?.totalPayment, "3160000"); assert.equal(invoice?.status, 'Original'); assert.equal(invoice?.currency, null)
  assert.deepEqual(invoice?.rates.map(r => r.vatRate), ['8', null])
})

test('hóa đơn thay thế / điều chỉnh lấy từ TCHDon', () => {
  assert.equal(eInvoiceFromXml(xml('<TTHDLQuan><TCHDon>1</TCHDon><KHHDCLQuan>C26TAA</KHHDCLQuan><SHDCLQuan>100</SHDCLQuan></TTHDLQuan>'), 'Output').invoice?.status, 'Replacement')
  const adj = eInvoiceFromXml(xml('<TTHDLQuan><TCHDon>2</TCHDon><SHDCLQuan>100</SHDCLQuan></TTHDLQuan>'), 'Output').invoice
  assert.equal(adj?.status, 'Adjustment'); assert.equal(adj?.relatedNumber, '100'); assert.equal(adj?.direction, 'Output')
})

test('thiếu thẻ bắt buộc hoặc tổng không khớp thì báo lỗi, không đoán', () => {
  const bad = eInvoiceFromXml('<HDon><DLHDon><TTChung><SHDon>1</SHDon></TTChung></DLHDon></HDon>', 'Input')
  assert.equal(bad.invoice, undefined); assert.ok(bad.errors.length >= 4)
  const mism = eInvoiceFromXml(xml('', '<TgTCThue>3000000</TgTCThue><TgTThue>999</TgTThue><TgTTTBSo>3000999</TgTTTBSo>'), 'Input')
  assert.equal(mism.invoice, undefined); assert.ok(mism.errors.some(e => e.includes('không khớp')))
})
