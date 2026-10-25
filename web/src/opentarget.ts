import { useState } from 'react'
import { navStore } from './common'
import type { OpenType } from './navIntents'

/** Chứng từ mà danh sách việc cần làm yêu cầu mở thẳng ở màn này (đọc lúc dựng; App xóa ý định khi rời màn đích). */
export function useOpenTarget(type: OpenType): string | undefined {
  const [id] = useState(() => (navStore.intent?.open?.type === type ? navStore.intent.open.id : undefined))
  return id
}
/** Thuộc tính cho dòng bảng của chứng từ được chỉ định: tô nổi và cuộn vào giữa màn hình. */
export const focusRowProps = (id: string, target?: string) => id === target
  ? { className: 'focus-row', ref: (el: HTMLTableRowElement | null) => { el?.scrollIntoView?.({ block: 'center' }) } }
  : {}
