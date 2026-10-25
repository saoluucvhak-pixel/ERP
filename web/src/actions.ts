/** Quy tắc chung cho mọi thao tác ghi trên giao diện (một nơi, có kiểm thử): quyền, bấm đúp, phiên bản dòng. Máy chủ vẫn là nơi quyết định cuối cùng. */

export interface ActionOptions {
  /** Mã quyền cần có (ví dụ 'vouchers.approve'); thiếu thì không gọi máy chủ. */
  needs?: string
  /** Bỏ qua lần bấm này nếu thao tác cùng hộp đang chạy (chống bấm đúp). */
  exclusive?: boolean
}

export class ActionRefused extends Error {
  reason: 'permission' | 'busy'
  constructor(reason: 'permission' | 'busy', message: string) { super(message); this.name = 'ActionRefused'; this.reason = reason }
}

/** null = được chạy; ngược lại là lý do từ chối. Hàm thuần để kiểm thử. */
export function refuseReason(o: ActionOptions | undefined, inFlight: number, has: (p: string) => boolean): ActionRefused | null {
  if (o?.needs && !has(o.needs)) return new ActionRefused('permission', `Bạn không có quyền thực hiện thao tác này (${o.needs}).`)
  if (o?.exclusive && inFlight > 0) return new ActionRefused('busy', 'Thao tác đang được xử lý, vui lòng đợi.')
  return null
}

/** Gắn phiên bản dòng (rowVersion) vào đường dẫn: mọi thao tác duyệt / ghi sổ / hủy / đảo của người dùng phải gửi nó. */
export function withRowVersion(path: string, rowVersion: number | string): string {
  if (/[?&]rowVersion=/.test(path)) throw new Error(`Đường dẫn đã có rowVersion: ${path}`)
  return `${path}${path.includes('?') ? '&' : '?'}rowVersion=${encodeURIComponent(String(rowVersion))}`
}
