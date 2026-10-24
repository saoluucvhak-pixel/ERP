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
  setToken(t: string | null) { token = t; sessionStorageSet('hak.token', t) },
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
/** Ngữ cảnh (phiên + công ty) tại thời điểm tạo thao tác; mọi lần gửi lại dùng đúng ngữ cảnh này, không đọc lại biến toàn cục. */
interface Ctx { token: string | null; companyId: string | null }
const snapshot = (): Ctx => ({ token, companyId })
const sameCtx = (c: Ctx) => c.token === token && c.companyId === companyId
interface PendingWrite { method: string; path: string; body: unknown; key: string; ctx: Ctx }
let pendingWrite: PendingWrite | null = null
/** Chỉ báo có thao tác chờ khi đang ở đúng phiên/công ty đã tạo ra nó; đổi tài khoản hay công ty thì ẩn (không xóa, không gửi dưới ngữ cảnh mới). */
export const hasPendingWrite = () => pendingWrite !== null && sameCtx(pendingWrite.ctx)
const sleep = (ms: number) => new Promise(r => setTimeout(r, ms))

/**
 * Thao tác ghi (POST/PUT/DELETE) mang một Idempotency-Key: mất kết nối giữa chừng thì tự gửi lại tối đa 2 lần với CÙNG khóa (máy chủ trả phản hồi cũ nếu lần trước đã ghi,
 * không ghi hai lần). Vẫn không nối được thì báo "kết quả chưa xác định" và giữ yêu cầu để người dùng bấm thử lại an toàn; không bao giờ đoán là đã ghi hay chưa.
 */
export async function api<T = unknown>(method: string, path: string, body?: unknown, idempotencyKey?: string, ctx: Ctx = snapshot()): Promise<T> {
  const mutating = method === 'POST' || method === 'PUT' || method === 'DELETE'
  const key = mutating ? (idempotencyKey ?? newIdempotencyKey()) : undefined
  for (let attempt = 0; ; attempt++) {
    try {
      const result = await apiOnce<T>(method, path, body, key, ctx)
      if (pendingWrite && pendingWrite.key === key) pendingWrite = null
      return result
    } catch (e) {
      if (!(mutating && e instanceof ApiError && e.status === 0)) throw e
      if (attempt >= 2) {
        pendingWrite = { method, path, body, key: key!, ctx }
        throw new ApiError(0, UNCERTAIN_CODE, 'Mất kết nối khi đang ghi: thao tác CÓ THỂ đã được lưu. Bấm "Thử lại an toàn" (không ghi trùng) hoặc mở lại danh sách để kiểm tra trước khi nhập lại.')
      }
      await sleep(1000 * (attempt + 1))
    }
  }
}

/** Gửi lại thao tác ghi chưa xác định với đúng khóa cũ. Trả về true nếu máy chủ xác nhận (ghi mới hoặc nhận lại phản hồi cũ). */
export async function retryPendingWrite(): Promise<boolean> {
  const p = pendingWrite
  if (!p || !sameCtx(p.ctx)) return false
  await api(p.method, p.path, p.body, p.key, p.ctx)
  return true
}

async function apiOnce<T = unknown>(method: string, path: string, body: unknown, key: string | undefined, ctx: Ctx): Promise<T> {
  const headers: Record<string, string> = { 'X-Device': 'web' }
  if (key) headers['Idempotency-Key'] = key
  if (ctx.token) headers.Authorization = `Bearer ${ctx.token}`
  if (ctx.companyId) headers['X-Company-Id'] = ctx.companyId
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  const res = await send(`/api${path}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) })
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
export interface Partner { id: string; code: string; rowVersion?: number; name: string; taxCode: string | null; isActive: boolean; address?: string | null; phone?: string | null; email?: string | null; creditLimit?: string | null; paymentTermsDays?: number | null; paymentDiscountPercent?: string | null; paymentDiscountDays?: number | null; isSupplier?: boolean; isCustomer?: boolean; isEmployee?: boolean }
export interface Period { id: string; fiscalYearId: string; periodNo: number; startDate: string; endDate: string; status: 'Open' | 'Locked' }
export interface VoucherLine { lineNo: number; accountId: string; side: 'D' | 'C'; amount: string; partnerId: string | null; description: string | null; cashFlowCode?: string | null; settleJournalLineId?: string | null; dueDate?: string | null; foreignCurrency?: string | null }
export interface Voucher { id: string; number: string; voucherType: string; status: string; voucherDate: string; postingDate: string; description: string; totalDebit: string; totalCredit: string; rowVersion: number; reversesVoucherId: string | null; lines: VoucherLine[]; businessEvent?: string | null; mainAccountId?: string | null; partnerId?: string | null; sourceSystem?: string | null; counterpartyName?: string | null; counterpartyUnit?: string | null }
export interface TrialBalanceRow { accountId?: string; accountCode: string; accountName: string; openingDebit: string; openingCredit: string; periodDebit: string; periodCredit: string; closingDebit: string; closingCredit: string; level?: number; isGroup?: boolean }
export interface OpenItem { journalLineId: string; voucherNumber: string; voucherType: string; postingDate: string; dueDate: string; partnerId: string; side: 'Debit' | 'Credit'; amount: string; allocated: string; open: string; description: string | null
  foreignCurrency?: string | null; foreignAmount?: string | null; openForeign?: string | null }
