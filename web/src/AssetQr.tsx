import { useEffect, useState } from 'react'
import { Modal, PrintSheet, printSheet, useCompanyName } from './common'
import { assetUrl, getPublicUrl, isLocalAddress, qrSvg, setPublicUrl } from './qr'

/** In nhãn QR dán lên máy: quét bằng camera điện thoại mở trang báo sự cố của đúng máy đó (cần điện thoại truy cập được địa chỉ máy chủ HAK, thường cùng mạng Wi-Fi nội bộ). */
export function QrLabelsButton({ items, label = 'In nhãn QR' }: { items: { code: string; name: string }[]; label?: string }) {
  const [open, setOpen] = useState(false)
  const [printing, setPrinting] = useState(false)
  const [base, setBase] = useState(getPublicUrl())
  const company = useCompanyName()
  useEffect(() => { if (!printing) return; const t = setTimeout(() => { printSheet(); setPrinting(false) }, 200); return () => clearTimeout(t) }, [printing])
  const warn = isLocalAddress(base)
  return <>
    <button disabled={items.length === 0} onClick={() => setOpen(true)} title="In nhãn mã QR cho các tài sản đang hiển thị">{label}</button>
    {open && <Modal title={`Nhãn QR tài sản (${items.length})`} onClose={() => setOpen(false)}
      footer={<><button onClick={() => setOpen(false)}>Đóng</button><button className="primary" disabled={!base.trim()} onClick={() => { setPublicUrl(base); setOpen(false); setPrinting(true) }}>In {items.length} nhãn</button></>}>
      <label>Địa chỉ máy chủ HAK mà điện thoại truy cập được
        <input value={base} onChange={e => setBase(e.target.value)} placeholder="http://192.168.1.10:5080" /></label>
      {warn && <div className="msg error">Địa chỉ này (localhost / 127.0.0.1) chỉ dùng được trên chính máy tính này — điện thoại quét sẽ không mở được. Nhập địa chỉ IP của máy chủ trong mạng nội bộ, ví dụ http://192.168.1.10:5080.</div>}
      <p className="muted">Ví dụ mã đầu tiên: <code>{items[0] ? assetUrl(base || 'http://…', items[0].code) : ''}</code></p>
      {items[0] && <div style={{ width: 120 }} dangerouslySetInnerHTML={{ __html: qrSvg(assetUrl(base || 'http://…', items[0].code)) }} />}
    </Modal>}
    {printing && <PrintSheet>
      <div className="qr-labels">{items.map(i => <div key={i.code} className="qr-label">
        <div className="qr-img" dangerouslySetInnerHTML={{ __html: qrSvg(assetUrl(base, i.code)) }} />
        <div className="qr-text"><b>{i.code}</b><div>{i.name}</div><small>{company} · Quét QR để báo sự cố</small></div></div>)}</div>
    </PrintSheet>}
  </>
}
