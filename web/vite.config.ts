import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// API runs locally (Hak.Api). In the packaged Tauri app the base URL and token come from the shell instead.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: { '/api': 'http://127.0.0.1:5080' },
  },
})
