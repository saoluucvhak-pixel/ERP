export class ApiError extends Error {
  readonly code: string
  readonly status: number
  readonly errors: { code: string; message: string; lineNo?: number | null }[]
  constructor(status: number, code: string, message: string, errors: ApiError['errors'] = []) {
    super(message)
    this.status = status
    this.code = code
    this.errors = errors
  }
}

let token: string | null = sessionStorageGet('hak.token')
let companyId: string | null = sessionStorageGet('hak.company')

function sessionStorageGet(k: string): string | null {
  try { return sessionStorage.getItem(k) } catch { return null }
}
function sessionStorageSet(k: string, v: string | null) {
  try { if (v == null) sessionStorage.removeItem(k); else sessionStorage.setItem(k, v) } catch { /* private mode */ }
}

/** Registered by the app shell: called when a request that carried a token is refused as unauthenticated (session expired, revoked, server restarted). */
let onExpired: (() => void) | null = null
export const setSessionExpiredHandler = (h: (() => void) | null) => { onExpired = h }

export const session = {
  get token() { return token },
  get companyId() { return companyId },
  setToken(t: string | null) { token = t; sessionStorageSet('hak.token', t); if (t === null) { uncertainOps.clear(); sessionStorageSet(PENDING_KEY, null) } },
  setCompany(c: string | null) { companyId = c; sessionStorageSet('hak.company', c) },
}

/** fetch that reports a stopped / unreachable HAK server in Vietnamese instead of the browser's "TypeError: Failed to fetch". */
async function send(url: string, init: RequestInit): Promise<Response> {
  try { return await fetch(url, init) }
  catch {
    throw new ApiError(0, 'KHONG_KET_NOI', 'Không kết nối được máy chủ HAK — có thể HAK đã tắt. Mở lại bằng tệp "Chay HAK", chờ trang tự mở rồi thao tác lại (dữ liệu chưa bị thay đổi).')
  }
}

/** Response body as JSON; a non-JSON answer (e.g. a proxy error page) becomes a readable error. */
function parse(text: string, status: number): any {
  if (!text) return undefined
  try { return JSON.parse(text) }
  catch { throw new ApiError(status, 'HTTP_' + status, `Máy chủ trả lời không đúng định dạng (mã ${status}).`) }
}

/** Khóa chống ghi trùng. crypto.randomUUID chỉ có ở ngữ cảnh bảo mật (https/localhost); truy cập qua http trong LAN phải dùng getRandomValues. */
export function newIdempotencyKey(): string {
  const c = globalThis.crypto
  if (c?.randomUUID) return c.randomUUID()
  const b = new Uint8Array(16)
  if (c?.getRandomValues) c.getRandomValues(b); else for (let i = 0; i < 16; i++) b[i] = Math.floor(Math.random() * 256)
  return Array.from(b, x => x.toString(16).padStart(2, '0')).join('')
}

export const UNCERTAIN_CODE = 'KET_QUA_CHUA_XAC_DINH'
/** Phản hồi của một yêu cầu ĐỌC thuộc phiên/công ty cũ đến sau khi đã đổi ngữ cảnh: bị bỏ, không được ghi đè dữ liệu của ngữ cảnh mới. */
export const STALE_CODE = 'NGU_CANH_DA_DOI'
export const isStaleContext = (e: unknown) => e instanceof ApiError && e.code === STALE_CODE
/** Ngữ cảnh (phiên + công ty) tại thời điểm tạo thao tác; mọi lần gửi lại dùng đúng ngữ cảnh này, không đọc lại biến toàn cục. */
interface Ctx { token: string | null; companyId: string | null }
const snapshot = (): Ctx => ({ token, companyId })
const sameCtx = (c: Ctx) => c.token === token && c.companyId === companyId
/**
 * Một thao tác ghi: nội dung JSON được chụp THÀNH CHUỖI một lần lúc tạo (sửa đối tượng gốc về sau không đổi được thứ đã/sẽ gửi), cùng khóa, cùng ngữ cảnh.
 * Dedupe, lần gửi đầu, tự gửi lại và "thử lại an toàn" đều dùng đúng chuỗi + khóa này.
 * Trạng thái: InFlight (đang gửi) → Confirmed (máy chủ nhận) | Rejected (máy chủ từ chối rõ ràng) | Uncertain (mất kết nối, chưa biết đã ghi hay chưa).
 */
export type OpState = 'InFlight' | 'Uncertain' | 'Confirmed' | 'Rejected'
export interface WriteOp { readonly id: string; readonly method: string; readonly path: string; readonly bodyText: string | undefined; readonly key: string; readonly ctx: Ctx; state: OpState }
const newOp = (method: string, path: string, body: unknown, key: string | undefined, ctx: Ctx): WriteOp => {
  const key2 = key ?? newIdempotencyKey()
  return { id: key2, method, path, bodyText: body === undefined ? undefined : JSON.stringify(body), key: key2, ctx, state: 'InFlight' }
}
/** Các thao tác chưa xác định, theo thứ tự phát sinh; mỗi thao tác một chỗ (không ghi đè nhau), lọc theo phiên + công ty khi đọc. */
const uncertainOps = new Map<string, WriteOp>()

/**
 * Tải lại trang không được làm mất khả năng "thử lại an toàn": thao tác chưa xác định được lưu ở sessionStorage của TAB (cùng nơi giữ token, mất khi đóng tab).
 * Chỉ lưu id, khóa chống trùng, phương thức, đường dẫn, nội dung đã chụp và công ty — KHÔNG lưu token; khi khôi phục gắn với phiên hiện tại. Đường dẫn mang bí mật
 * (đăng nhập, sao lưu, người dùng, khóa trao đổi) không bao giờ có khóa chống trùng nên không bao giờ nằm ở đây. Đăng xuất xóa hết.
 */
const PENDING_KEY = 'hak.pending'
interface StoredOp { id: string; method: string; path: string; bodyText: string | undefined; key: string; companyId: string | null }
export function serializePending(ops: Iterable<WriteOp>): string {
  return JSON.stringify([...ops].map((o): StoredOp => ({ id: o.id, method: o.method, path: o.path, bodyText: o.bodyText, key: o.key, companyId: o.ctx.companyId })))
}
export function restorePending(json: string | null, tokenNow: string | null): WriteOp[] {
  if (!json || !tokenNow) return []
  try {
    const rows = JSON.parse(json) as StoredOp[]
    return rows.filter(r => typeof r.id === 'string' && typeof r.key === 'string' && typeof r.method === 'string' && typeof r.path === 'string')
      .map(r => ({ id: r.id, method: r.method, path: r.path, bodyText: r.bodyText, key: r.key, ctx: { token: tokenNow, companyId: r.companyId }, state: 'Uncertain' as OpState }))
  } catch { return [] }
}
const persistPending = () => {
  sessionStorageSet(PENDING_KEY, uncertainOps.size === 0 ? null : serializePending(uncertainOps.values()))
  if (typeof window !== 'undefined') window.dispatchEvent(new Event('hak-pending'))   // banner "thao tác chưa xác định" cập nhật
}
for (const o of restorePending(sessionStorageGet(PENDING_KEY), token)) uncertainOps.set(o.id, o)
/** Thao tác chưa xác định của phiên + công ty đang dùng (đổi tài khoản hay công ty thì không thấy; không xóa, không gửi dưới ngữ cảnh mới). */
export const pendingWrites = (): readonly WriteOp[] => [...uncertainOps.values()].filter(o => sameCtx(o.ctx))
export const hasPendingWrite = () => pendingWrites().length > 0
const sleep = (ms: number) => new Promise(r => setTimeout(r, ms))

/**
 * Thao tác ghi (POST/PUT/DELETE) mang một Idempotency-Key: mất kết nối giữa chừng thì tự gửi lại tối đa 2 lần với CÙNG khóa và CÙNG nội dung (máy chủ trả phản hồi cũ nếu lần trước đã ghi,
 * không ghi hai lần). Vẫn không nối được thì báo "kết quả chưa xác định" và giữ thao tác để người dùng bấm thử lại an toàn; không bao giờ đoán là đã ghi hay chưa.
 */
export function api<T = unknown>(method: string, path: string, body?: unknown, idempotencyKey?: string, ctx: Ctx = snapshot()): Promise<T> {
  const mutating = method === 'POST' || method === 'PUT' || method === 'DELETE'
  if (!mutating) return apiCore<T>(method, path, body, undefined, ctx)
  const op = newOp(method, path, body, idempotencyKey, ctx)
  if (idempotencyKey) return runOp<T>(op)
  // Chống bấm trùng (nhấp đúp, phím tắt lặp): yêu cầu GHI giống hệt (cùng phương thức, đường dẫn, nội dung đã chụp, phiên, công ty) đang bay thì dùng chung một lần gửi
  // và một khóa — không tạo khóa mới nên không ghi hai lần. Khi lần gửi xong, thao tác y hệt lần sau là một thao tác mới.
  const signature = `${method} ${path} ${op.bodyText ?? ''} ${ctx.token ?? ''}|${ctx.companyId ?? ''}`
  const running = inFlightWrites.get(signature)
  if (running) return running as Promise<T>
  const p = runOp<T>(op).finally(() => { inFlightWrites.delete(signature) })
  inFlightWrites.set(signature, p)
  return p
}
const inFlightWrites = new Map<string, Promise<unknown>>()

async function apiCore<T = unknown>(method: string, path: string, body: unknown, _key: undefined, ctx: Ctx): Promise<T> {
  const result = await apiOnce<T>(method, path, body === undefined ? undefined : JSON.stringify(body), undefined, ctx)
  if (!sameCtx(ctx)) throw new ApiError(0, STALE_CODE, 'Phiên hoặc công ty đã đổi trong lúc tải; bỏ kết quả cũ.')
  return result
}

async function runOp<T>(op: WriteOp): Promise<T> {
  op.state = 'InFlight'
  for (let attempt = 0; ; attempt++) {
    try {
      const result = await apiOnce<T>(op.method, op.path, op.bodyText, op.key, op.ctx)
      op.state = 'Confirmed'; if (uncertainOps.delete(op.id)) persistPending()
      return result
    } catch (e) {
      if (!(e instanceof ApiError && e.status === 0)) { op.state = 'Rejected'; if (uncertainOps.delete(op.id)) persistPending(); throw e }
      if (attempt >= 2) {
        op.state = 'Uncertain'; uncertainOps.set(op.id, op); persistPending()
        throw new UncertainError(op.id)
      }
      await sleep(1000 * (attempt + 1))
    }
  }
}

/** Kết quả chưa xác định: mang `opId` để nút "Thử lại an toàn" gửi lại ĐÚNG thao tác này. */
export class UncertainError extends ApiError {
  readonly opId: string
  constructor(opId: string) {
    super(0, UNCERTAIN_CODE, 'Mất kết nối khi đang ghi: thao tác CÓ THỂ đã được lưu. Bấm "Thử lại an toàn" (không ghi trùng) hoặc mở lại danh sách để kiểm tra trước khi nhập lại.')
    this.opId = opId
  }
}

/** Gửi lại một thao tác chưa xác định (theo `opId`, mặc định cái cũ nhất của ngữ cảnh hiện tại) với đúng khóa + nội dung cũ. Trả về true nếu máy chủ xác nhận (ghi mới hoặc nhận lại phản hồi cũ). */
export async function retryPendingWrite(opId?: string): Promise<boolean> {
  const op = opId ? uncertainOps.get(opId) : pendingWrites()[0]
  if (!op || !sameCtx(op.ctx)) return false
  await runOp(op)
  return true
}

async function apiOnce<T = unknown>(method: string, path: string, bodyText: string | undefined, key: string | undefined, ctx: Ctx): Promise<T> {
  const headers: Record<string, string> = { 'X-Device': 'web' }
  if (key) headers['Idempotency-Key'] = key
  if (ctx.token) headers.Authorization = `Bearer ${ctx.token}`
  if (ctx.companyId) headers['X-Company-Id'] = ctx.companyId
  if (bodyText !== undefined) headers['Content-Type'] = 'application/json'
  const res = await send(`/api${path}`, { method, headers, body: bodyText })
  if (res.status === 204) return undefined as T
  const text = await res.text()
  const data = parse(text, res.status)
  if (!res.ok) {
    // chỉ phiên hiện hành mới bị coi là hết hạn; phản hồi 401 của một phiên cũ không được đăng xuất phiên mới
    if (res.status === 401 && ctx.token && ctx.token === token && (data?.code ?? '') === 'UNAUTHENTICATED') { session.setToken(null); onExpired?.() }
    else if (res.status === 401 && !ctx.token && !token) session.setToken(null)
    throw new ApiError(res.status, data?.code ?? 'HTTP_' + res.status, data?.message ?? res.statusText, data?.errors ?? [])
  }
  return data as T
}

/** Sends a text body exactly as given (signed JSON packages must reach the server byte for byte). */
export async function postRaw<T>(path: string, text: string): Promise<T> {
  const headers: Record<string, string> = { 'X-Device': 'web', 'Content-Type': 'application/json' }
  if (token) headers.Authorization = `Bearer ${token}`
  if (companyId) headers['X-Company-Id'] = companyId
  const res = await send(`/api${path}`, { method: 'POST', headers, body: text })
  const data = parse(await res.text(), res.status)
  if (!res.ok) throw new ApiError(res.status, data?.code ?? 'HTTP_' + res.status, data?.message ?? res.statusText, data?.errors ?? [])
  return data as T
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
