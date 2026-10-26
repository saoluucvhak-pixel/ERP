import { sha256 } from '@noble/hashes/sha2.js'

export class ApiError extends Error {
  readonly code: string
  readonly status: number
  readonly errors: { code: string; message: string; lineNo?: number | null }[]
  constructor(status: number, code: string, message: string, errors: ApiError['errors'] = []) {
    super(message); this.status = status; this.code = code; this.errors = errors
  }
}

const OWNER_KEY = 'hak.auth-owner'
const PENDING_KEY = 'hak.pending'
const REVIEW_KEY = 'hak.pending.review'
// The server retains idempotency responses for seven days. Never replay a tab record near/beyond that boundary.
const MAX_RETRY_AGE = 6 * 24 * 60 * 60 * 1000
let token: string | null = sessionStorageGet('hak.token')
let companyId: string | null = sessionStorageGet('hak.company')
let authOwner: string | null = token ? sessionStorageGet(OWNER_KEY) ?? newIdempotencyKey() : null
if (authOwner) sessionStorageSet(OWNER_KEY, authOwner)
let reviewRequired = sessionStorageGet(REVIEW_KEY) === '1'

function sessionStorageGet(k: string): string | null {
  try { return sessionStorage.getItem(k) } catch { return null }
}
function sessionStorageSet(k: string, v: string | null) {
  try { if (v == null) sessionStorage.removeItem(k); else sessionStorage.setItem(k, v) } catch { /* private mode */ }
}
function notifyPending() {
  if (typeof window !== 'undefined') window.dispatchEvent(new Event('hak-pending'))
}
function requireReview() { reviewRequired = true; sessionStorageSet(REVIEW_KEY, '1'); notifyPending() }
export const pendingReviewNeeded = () => reviewRequired
export function acknowledgePendingReview() { reviewRequired = false; sessionStorageSet(REVIEW_KEY, null); notifyPending() }

let onExpired: (() => void) | null = null
export const setSessionExpiredHandler = (h: (() => void) | null) => { onExpired = h }

interface Ctx { readonly token: string | null; readonly companyId: string | null; readonly authOwner: string | null }
const snapshot = (): Ctx => Object.freeze({ token, companyId, authOwner })
const sameAuth = (c: Ctx) => c.token === token && c.authOwner === authOwner
const sameCtx = (c: Ctx) => sameAuth(c) && c.companyId === companyId
const uncertainOps = new Map<string, WriteOp>()
const inFlightWrites = new Map<string, Promise<unknown>>()
const activeOps = new Map<string, Promise<unknown>>()

export const session = {
  get token() { return token },
  get companyId() { return companyId },
  get ownerId() { return authOwner },
  setToken(t: string | null) {
    if (t === token) return
    if (uncertainOps.size > 0) requireReview()
    token = t; authOwner = t ? newIdempotencyKey() : null
    sessionStorageSet('hak.token', t); sessionStorageSet(OWNER_KEY, authOwner)
    uncertainOps.clear(); inFlightWrites.clear(); activeOps.clear(); sessionStorageSet(PENDING_KEY, null)
    notifyPending()
  },
  setCompany(c: string | null) { companyId = c; sessionStorageSet('hak.company', c); notifyPending() },
}

async function send(url: string, init: RequestInit): Promise<Response> {
  try { return await fetch(url, init) }
  catch {
    throw new ApiError(0, 'KHONG_KET_NOI', init.method === 'GET'
      ? 'Không kết nối được máy chủ HAK. Kiểm tra kết nối và mở lại HAK.'
      : 'Mất kết nối khi gửi yêu cầu. Chưa xác định đã xử lý hay chưa; kiểm tra kết quả trước khi thực hiện lại.')
  }
}
function parse(text: string, status: number): any {
  if (!text) return undefined
  try { return JSON.parse(text) }
  catch { throw new ApiError(status >= 200 && status < 300 ? 0 : status, 'HTTP_' + status, `Máy chủ trả lời không đúng định dạng (mã ${status}); cần đối chiếu kết quả.`) }
}

export function newIdempotencyKey(): string {
  const c = globalThis.crypto
  if (c?.randomUUID) return c.randomUUID()
  const b = new Uint8Array(16)
  if (c?.getRandomValues) c.getRandomValues(b); else for (let i = 0; i < 16; i++) b[i] = Math.floor(Math.random() * 256)
  return Array.from(b, x => x.toString(16).padStart(2, '0')).join('')
}

export const UNCERTAIN_CODE = 'KET_QUA_CHUA_XAC_DINH'
export const STALE_CODE = 'NGU_CANH_DA_DOI'
export const isStaleContext = (e: unknown) => e instanceof ApiError && e.code === STALE_CODE
export type OpState = 'InFlight' | 'Uncertain' | 'Confirmed' | 'Rejected' | 'Detached'
export interface WriteOp {
  readonly id: string; readonly method: string; readonly path: string; readonly bodyText: string | undefined
  readonly key: string; readonly ctx: Ctx; readonly createdAt: number; state: OpState
}
const newOp = (method: string, path: string, body: unknown, key: string | undefined, ctx: Ctx): WriteOp => ({
  id: newIdempotencyKey(), method, path, bodyText: body === undefined ? undefined : JSON.stringify(body), key: key ?? newIdempotencyKey(),
  ctx: Object.freeze({ ...ctx }), createdAt: Date.now(), state: 'InFlight',
})

/** Match IdempotencyMiddleware.ExcludedPath. Canonicalize dot segments/encoding before deciding whether a request can be stored/replayed. */
export function supportsSafeRetry(path: string): boolean {
  try {
    if (!path.startsWith('/') || path.startsWith('//') || path.includes('\\') || path.includes('#')) return false
    const decoded = decodeURIComponent(path.split('?')[0])
    if (decoded.includes('\\') || decoded.startsWith('//')) return false
    const p = new URL(decoded, 'https://hak.invalid').pathname.toLowerCase()
    // Match IdempotencyMiddleware.CallsExternalProvider: calls to the e-invoice provider run outside the idempotency transaction.
    if (/^\/einvoice\/(.*\/)?(issue|retry|process-pending|adjust-information|send)\/?$/.test(p)) return false
    return !['/auth', '/setup', '/backups', '/admin/backups', '/admin/users', '/me', '/exchange/keys']
      .some(prefix => p === prefix || p.startsWith(prefix + '/') || ['/backups', '/admin/backups', '/admin/users', '/exchange/keys'].includes(prefix) && p.startsWith(prefix))
  } catch { return false }
}
const fingerprint = (t: string) => Array.from(sha256(new TextEncoder().encode(t)), b => b.toString(16).padStart(2, '0')).join('')
interface StoredOp { id: string; method: string; path: string; bodyText?: string; key: string; companyId: string | null; createdAt: number }
interface PendingEnvelope { version: 2; ownerId: string; tokenFingerprint: string; ops: StoredOp[] }
const fresh = (at: number) => Number.isFinite(at) && at <= Date.now() + 5 * 60 * 1000 && Date.now() - at < MAX_RETRY_AGE

/** Persist no token. The nonce distinguishes login generations; SHA-256 proves the restored token is exactly the token of the original server idempotency scope. Works on HTTP LAN too. */
export function serializePending(ops: Iterable<WriteOp>): string {
  const safe = [...ops].filter(o => o.ctx.token && o.ctx.authOwner && supportsSafeRetry(o.path) && fresh(o.createdAt) && (o.state === 'Uncertain' || o.state === 'InFlight'))
  const first = safe[0]
  if (!first) return JSON.stringify({ version: 2, ownerId: '', tokenFingerprint: '', ops: [] })
  return JSON.stringify({ version: 2, ownerId: first.ctx.authOwner!, tokenFingerprint: fingerprint(first.ctx.token!),
    ops: safe.filter(o => o.ctx.token === first.ctx.token && o.ctx.authOwner === first.ctx.authOwner).map((o): StoredOp => ({
      id: o.id, method: o.method, path: o.path, bodyText: o.bodyText, key: o.key, companyId: o.ctx.companyId, createdAt: o.createdAt,
    })),
  } satisfies PendingEnvelope)
}
function inspectPending(json: string | null, tokenNow: string | null, ownerNow: string | null): { ops: WriteOp[]; review: boolean } {
  if (!json) return { ops: [], review: false }
  try {
    const data = JSON.parse(json) as PendingEnvelope
    if (data.version !== 2 || !tokenNow || !ownerNow || data.ownerId !== ownerNow || data.tokenFingerprint !== fingerprint(tokenNow) || !Array.isArray(data.ops))
      return { ops: [], review: true }
    const seen = new Set<string>(); const ops: WriteOp[] = []; let review = false
    for (const r of data.ops) {
      if (typeof r.id !== 'string' || !r.id || seen.has(r.id) || typeof r.key !== 'string' || !/^[A-Za-z0-9_-]{8,100}$/.test(r.key)
        || !['POST', 'PUT', 'DELETE'].includes(r.method) || typeof r.path !== 'string' || !supportsSafeRetry(r.path)
        || !(r.companyId === null || typeof r.companyId === 'string') || !(r.bodyText === undefined || typeof r.bodyText === 'string') || !fresh(r.createdAt)) { review = true; continue }
      seen.add(r.id)
      ops.push({ ...r, bodyText: r.bodyText, ctx: Object.freeze({ token: tokenNow, companyId: r.companyId, authOwner: ownerNow }), state: 'Uncertain' })
    }
    return { ops, review }
  } catch { return { ops: [], review: true } }
}
export function restorePending(json: string | null, tokenNow: string | null, ownerNow: string | null = authOwner): WriteOp[] {
  return inspectPending(json, tokenNow, ownerNow).ops
}
function persistPending() {
  const owned = [...uncertainOps.values()].filter(o => sameAuth(o.ctx))
  sessionStorageSet(PENDING_KEY, owned.length ? serializePending(owned) : null); notifyPending()
}
const restored = inspectPending(sessionStorageGet(PENDING_KEY), token, authOwner)
for (const op of restored.ops) uncertainOps.set(op.id, op)
if (restored.review) { requireReview(); persistPending() } // Purge legacy/unbound/sensitive records, retain only independently verified rows.
export const pendingWrites = (): readonly WriteOp[] => [...uncertainOps.values()].filter(o => sameCtx(o.ctx) && o.state === 'Uncertain')
export const hasPendingWrite = () => pendingWrites().length > 0
const sleep = (ms: number) => new Promise(r => setTimeout(r, ms))

export function api<T = unknown>(method: string, path: string, body?: unknown, idempotencyKey?: string, ctx: Ctx = snapshot()): Promise<T> {
  const mutating = method === 'POST' || method === 'PUT' || method === 'DELETE'
  if (!mutating || !ctx.token || !supportsSafeRetry(path)) return apiCore<T>(method, path, body, ctx)
  const op = newOp(method, path, body, idempotencyKey, ctx)
  const signature = `${method} ${path} ${op.bodyText ?? ''} ${ctx.authOwner ?? ''}|${ctx.companyId ?? ''}|${idempotencyKey ?? ''}`
  const running = inFlightWrites.get(signature)
  if (running) return running as Promise<T>
  const p = runOp<T>(op).finally(() => { if (inFlightWrites.get(signature) === p) inFlightWrites.delete(signature) })
  inFlightWrites.set(signature, p)
  return p
}
async function apiCore<T>(method: string, path: string, body: unknown, ctx: Ctx): Promise<T> {
  const result = await apiOnce<T>(method, path, body === undefined ? undefined : JSON.stringify(body), undefined, ctx)
  if (!sameCtx(ctx)) throw new ApiError(0, STALE_CODE, 'Phiên hoặc công ty đã đổi; bỏ phản hồi của ngữ cảnh cũ.')
  return result
}
function forget(op: WriteOp) { if (uncertainOps.get(op.id) === op) { uncertainOps.delete(op.id); persistPending() } }
function detach(op: WriteOp): ApiError {
  if (sameAuth(op.ctx) && fresh(op.createdAt)) { op.state = 'Uncertain'; uncertainOps.set(op.id, op); persistPending() }
  else { op.state = 'Detached'; forget(op); requireReview() }
  return new ApiError(0, STALE_CODE, 'Ngữ cảnh đã đổi; không gửi lại thao tác cũ. Quay lại đúng tài khoản/công ty để đối chiếu kết quả trước khi nhập lại.')
}
function runOp<T>(op: WriteOp): Promise<T> {
  const running = activeOps.get(op.id)
  if (running) return running as Promise<T>
  const p = Promise.resolve().then(() => executeOp<T>(op)).finally(() => { if (activeOps.get(op.id) === p) activeOps.delete(op.id) })
  activeOps.set(op.id, p); return p
}
async function executeOp<T>(op: WriteOp): Promise<T> {
  op.state = 'InFlight'
  for (let attempt = 0; ; attempt++) {
    if (!sameCtx(op.ctx) || !fresh(op.createdAt)) throw detach(op)
    // Save before dispatch: reloading while the first response is pending must retain the original key too.
    uncertainOps.set(op.id, op); persistPending()
    try {
      const result = await apiOnce<T>(op.method, op.path, op.bodyText, op.key, op.ctx)
      op.state = 'Confirmed'; forget(op)
      if (!sameCtx(op.ctx)) { requireReview(); throw new ApiError(0, STALE_CODE, 'Thao tác đã được xác nhận ở ngữ cảnh cũ; đối chiếu tại đúng công ty, không nhập lại.') }
      return result
    } catch (e) {
      if (isStaleContext(e)) throw e
      const unknown = e instanceof ApiError && (e.status === 0 || e.status >= 500 && e.status < 600)
      if (!unknown) { op.state = 'Rejected'; forget(op); throw e }
      if (!sameCtx(op.ctx) || !fresh(op.createdAt)) throw detach(op)
      if (attempt >= 2) {
        op.state = 'Uncertain'; uncertainOps.set(op.id, op); persistPending(); throw new UncertainError(op.id)
      }
      await sleep(1000 * (attempt + 1))
    }
  }
}
export class UncertainError extends ApiError {
  readonly opId: string
  constructor(opId: string) {
    super(0, UNCERTAIN_CODE, 'Mất phản hồi khi đang ghi: thao tác CÓ THỂ đã lưu. Dùng “Thử lại an toàn” với cùng khóa hoặc đối chiếu danh sách trước khi nhập lại.')
    this.opId = opId
  }
}
export async function retryPendingWrite(opId?: string): Promise<boolean> {
  const op = opId ? uncertainOps.get(opId) : pendingWrites()[0]
  if (!op || !sameCtx(op.ctx)) return false
  await runOp(op); return true
}
async function apiOnce<T>(method: string, path: string, bodyText: string | undefined, key: string | undefined, ctx: Ctx): Promise<T> {
  const headers: Record<string, string> = { 'X-Device': 'web' }
  if (key) headers['Idempotency-Key'] = key
  if (ctx.token) headers.Authorization = `Bearer ${ctx.token}`
  if (ctx.companyId) headers['X-Company-Id'] = ctx.companyId
  if (bodyText !== undefined) headers['Content-Type'] = 'application/json'
  const res = await send(`/api${path}`, { method, headers, body: bodyText })
  if (res.status === 204) return undefined as T
  let text: string
  try { text = await res.text() } catch { throw new ApiError(0, 'PHAN_HOI_BI_MAT', 'Mất nội dung phản hồi; chưa xác định kết quả ghi.') }
  const data = parse(text, res.status)
  if (!res.ok) {
    if (res.status === 401 && ctx.token && sameAuth(ctx) && (data?.code ?? '') === 'UNAUTHENTICATED') { session.setToken(null); onExpired?.() }
    throw new ApiError(res.status, data?.code ?? 'HTTP_' + res.status, data?.message ?? res.statusText, data?.errors ?? [])
  }
  return data as T
}
/** Signed JSON is sent byte-for-byte, once. Raw/excluded routes are never persisted or automatically retried. */
export async function postRaw<T>(path: string, text: string): Promise<T> {
  const ctx = snapshot()
  const result = await apiOnce<T>('POST', path, text, undefined, ctx)
  if (!sameCtx(ctx)) throw new ApiError(0, STALE_CODE, 'Ngữ cảnh đã đổi; đối chiếu kết quả tại ngữ cảnh gốc.')
  return result
}
export const get = <T,>(p: string) => api<T>('GET', p)
export const post = <T,>(p: string, b?: unknown) => api<T>('POST', p, b ?? {})
export const put = <T,>(p: string, b?: unknown) => api<T>('PUT', p, b)


// ---- DTOs (money fields are decimal strings) ----
export interface Account { id: string; code: string; name: string; parentId: string | null; isPostable: boolean; requiresPartner: boolean; isActive: boolean; openItemNature: string; cashKind: string; reportTags?: string | null; negativeBalancePolicy?: string }
export interface Partner { id: string; code: string; rowVersion?: number; name: string; taxCode: string | null; isActive: boolean; address?: string | null; phone?: string | null; email?: string | null; creditLimit?: string | null; paymentTermsDays?: number | null; paymentDiscountPercent?: string | null; paymentDiscountDays?: number | null; paymentDiscountOptions?: string | null; isSupplier?: boolean; isCustomer?: boolean; isEmployee?: boolean }
export interface Period { id: string; fiscalYearId: string; periodNo: number; startDate: string; endDate: string; status: 'Open' | 'Locked' }
export interface VoucherLine { lineNo: number; accountId: string; side: 'D' | 'C'; amount: string; partnerId: string | null; description: string | null; cashFlowCode?: string | null; settleJournalLineId?: string | null; dueDate?: string | null; foreignCurrency?: string | null }
export interface Voucher { id: string; number: string; voucherType: string; status: string; voucherDate: string; postingDate: string; description: string; totalDebit: string; totalCredit: string; rowVersion: number; reversesVoucherId: string | null; lines: VoucherLine[]; businessEvent?: string | null; mainAccountId?: string | null; partnerId?: string | null; sourceSystem?: string | null; counterpartyName?: string | null; counterpartyUnit?: string | null }
export interface TrialBalanceRow { accountId?: string; accountCode: string; accountName: string; openingDebit: string; openingCredit: string; periodDebit: string; periodCredit: string; closingDebit: string; closingCredit: string; level?: number; isGroup?: boolean }
export interface OpenItem { journalLineId: string; voucherNumber: string; voucherType: string; postingDate: string; dueDate: string; partnerId: string; side: 'Debit' | 'Credit'; amount: string; allocated: string; open: string; description: string | null
  foreignCurrency?: string | null; foreignAmount?: string | null; openForeign?: string | null }
