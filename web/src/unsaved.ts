import { useEffect, useRef, useState } from 'react'

/** Các biểu mẫu đang có nội dung nhập dở chưa lưu. Rời màn (đổi tab, phân hệ, công ty, đăng xuất) hoặc đóng / tải lại trang thì phải hỏi trước. */
const dirtySources = new Set<object>()

const beforeUnload = (e: BeforeUnloadEvent) => { e.preventDefault(); e.returnValue = '' }

export const hasUnsavedWork = () => dirtySources.size > 0

/** Hỏi người dùng trước khi rời màn nếu còn nội dung chưa lưu; trả về true nếu được phép đi tiếp. */
export function confirmLeave(): boolean {
  if (!hasUnsavedWork()) return true
  return window.confirm('Còn nội dung đang nhập chưa lưu. Rời khỏi màn hình này sẽ mất nội dung đó. Tiếp tục?')
}

/** Đánh dấu biểu mẫu là "đang nhập dở" khi dirty = true (tự gỡ khi dirty = false hoặc khi biểu mẫu bị đóng). */
export function useUnsavedGuard(dirty: boolean) {
  const id = useRef({})
  useEffect(() => {
    const me = id.current
    if (dirty) dirtySources.add(me); else dirtySources.delete(me)
    if (typeof window !== 'undefined') {
      if (dirtySources.size > 0) window.addEventListener('beforeunload', beforeUnload); else window.removeEventListener('beforeunload', beforeUnload)
    }
    return () => {
      dirtySources.delete(me)
      if (typeof window !== 'undefined' && dirtySources.size === 0) window.removeEventListener('beforeunload', beforeUnload)
    }
  }, [dirty])
}

/** Biểu mẫu "dở dang" = giá trị hiện tại khác giá trị lúc mở biểu mẫu. Lưu xong mà biểu mẫu về trạng thái ban đầu thì hết dở dang. */
export function useUnsavedWhenChanged(snapshot: unknown, disabled = false) {
  const [first] = useState(() => JSON.stringify(snapshot))
  useUnsavedGuard(!disabled && JSON.stringify(snapshot) !== first)
}
