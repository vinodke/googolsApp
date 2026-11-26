/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The dev server proxies /api and /hubs to the ASP.NET Core API, so the SPA and API share an origin.
// That lets us use Identity's HttpOnly cookie (no tokens in JavaScript) and SignalR without CORS.
const api = process.env.GOOGOLS_API ?? 'https://localhost:7180'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: api, changeOrigin: true, secure: false },
      '/hubs': { target: api, changeOrigin: true, secure: false, ws: true },
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test-setup.ts'],
  },
})
