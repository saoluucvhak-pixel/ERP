import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// API runs locally (Hak.Api). In the packaged Tauri app the base URL and token come from the shell instead.
export default defineConfig({
  plugins: [react()],
  server: {
    port: Number(process.env.HAK_WEB_PORT ?? 5173),
    proxy: { '/api': process.env.HAK_API_ORIGIN ?? 'http://127.0.0.1:5080' },   // HAK_* chỉ dùng cho test e2e (cổng riêng)
  },
})
