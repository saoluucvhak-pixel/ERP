import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)

// Ứng dụng cài được lên điện thoại (PWA): trình duyệt chỉ cho đăng ký service worker ở HTTPS hoặc localhost; bản chạy thử (vite dev) không đăng ký để khỏi lưu đệm.
if (import.meta.env.PROD && 'serviceWorker' in navigator && window.isSecureContext) {
  navigator.serviceWorker.register('/sw.js').catch(() => undefined)
}
