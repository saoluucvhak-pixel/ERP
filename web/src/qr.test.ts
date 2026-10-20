import { test } from 'node:test'
import assert from 'node:assert/strict'
import { assetCodeFromScan, assetUrl, isLocalAddress, qrSvg } from './qr.ts'

test('assetUrl and assetCodeFromScan round-trip, including codes with spaces and slashes', () => {
  const url = assetUrl('http://192.168.1.10:5080/', 'TS 01/A')
  assert.equal(url, 'http://192.168.1.10:5080/#/m/a/TS%2001%2FA')
  assert.equal(assetCodeFromScan(url), 'TS 01/A')
  assert.equal(assetCodeFromScan('TSCD-001'), 'TSCD-001')
  assert.equal(assetCodeFromScan('  https://x/#/m/a/MAY1?x=1 '), 'MAY1')
  assert.equal(assetCodeFromScan('có dấu cách và <script>'), null)
})

test('localhost addresses are flagged because phones cannot reach them', () => {
  assert.ok(isLocalAddress('http://localhost:5080'))
  assert.ok(isLocalAddress('http://127.0.0.1:5080'))
  assert.ok(!isLocalAddress('http://192.168.1.10:5080'))
})

test('qrSvg returns a scalable svg', () => {
  const svg = qrSvg('http://192.168.1.10:5080/#/m/a/TSCD-001')
  assert.ok(svg.startsWith('<svg'))
  assert.ok(svg.includes('viewBox'))
})
