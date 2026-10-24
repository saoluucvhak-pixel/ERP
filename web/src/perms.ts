import { useSyncExternalStore } from 'react'

/** Quyền của người dùng ở công ty đang chọn (null = chưa tải: chưa ẩn gì, tránh nhấp nháy). Nạp ở App sau khi chọn công ty. */
let granted: ReadonlySet<string> | null = null
const listeners = new Set<() => void>()
export const setPermissions = (p: readonly string[] | null) => { granted = p ? new Set(p) : null; listeners.forEach(l => l()) }
export const hasPermission = (p: string) => granted === null || granted.has(p)

/** Hook: có quyền `p` hay không (chưa tải xong thì coi là có; máy chủ vẫn là nơi quyết định cuối cùng với dữ liệu). */
export function usePermission(p: string): boolean {
  return useSyncExternalStore(cb => { listeners.add(cb); return () => { listeners.delete(cb) } }, () => hasPermission(p))
}
