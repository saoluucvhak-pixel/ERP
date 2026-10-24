/** Ý định mở màn hình từ "Thêm nhanh": chiều bán/mua + loại chứng từ cụ thể, để mỗi nút mở ĐÚNG form (không phụ thuộc màn đang mở hay mặc định của màn). */
export type NavKind = 'Sales' | 'Purchase'
/** Chứng từ cần mở thẳng (từ danh sách việc cần làm). */
export type OpenType = 'voucher' | 'invoice' | 'stockDocument' | 'paymentOrder' | 'order' | 'contract'
export interface OpenTarget { type: OpenType; id: string }
export interface NavIntent {
  open?: OpenTarget
  /** Thu/Chi tiền: loại chứng từ (CR, CP, BR, BP) và nghiệp vụ (businessEvent). */
  voucherType?: 'CR' | 'CP' | 'BR' | 'BP'
  businessEvent?: string
  /** Kho: loại phiếu hoặc tab kiểm kê. */
  stockType?: 'Receipt' | 'Issue' | 'Transfer'
  stockTab?: 'docs' | 'count'
}
export interface QuickItem { label: string; screen: string; kind?: NavKind; intent?: NavIntent }
export interface QuickGroup { title: string; items: QuickItem[] }

export const QUICK_GROUPS: QuickGroup[] = [
  { title: 'Tiền', items: [
    { label: 'Thu tiền', screen: 'cash', intent: { voucherType: 'CR', businessEvent: 'CUSTOMER_RECEIPT' } },
    { label: 'Chi tiền', screen: 'cash', intent: { voucherType: 'CP', businessEvent: 'EXPENSE' } },
    { label: 'Lệnh thanh toán ngân hàng', screen: 'bank' }] },
  { title: 'Mua hàng', items: [
    { label: 'Đơn mua hàng', screen: 'orders', kind: 'Purchase' },
    { label: 'Nhận hàng, nhập kho', screen: 'procurement', kind: 'Purchase' },
    { label: 'Hóa đơn mua', screen: 'trade', kind: 'Purchase' }] },
  { title: 'Bán hàng', items: [
    { label: 'Đơn bán hàng', screen: 'orders', kind: 'Sales' },
    { label: 'Lệnh bán, giao hàng', screen: 'revenue', kind: 'Sales' },
    { label: 'Hóa đơn bán', screen: 'trade', kind: 'Sales' }] },
  { title: 'Kho', items: [
    { label: 'Nhập kho', screen: 'stock', intent: { stockTab: 'docs', stockType: 'Receipt' } },
    { label: 'Xuất kho', screen: 'stock', intent: { stockTab: 'docs', stockType: 'Issue' } },
    { label: 'Kiểm kê', screen: 'stock', intent: { stockTab: 'count' } }] },
  { title: 'Sản xuất, tài sản', items: [{ label: 'Lệnh sản xuất', screen: 'production' }, { label: 'Đề nghị mua sắm tài sản', screen: 'assets' }] },
  { title: 'Tổng hợp', items: [{ label: 'Chứng từ nghiệp vụ khác', screen: 'vouchers' }] },
]
