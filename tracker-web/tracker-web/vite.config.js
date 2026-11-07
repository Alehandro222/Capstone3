import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// The dev server runs on port 5173 and forwards /api to the C# API. The browser then sees one address only, so the
// staff sign-in cookie works without any cross-site settings.
const API = process.env.API_URL ?? 'http://localhost:5080'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': API,
    },
  },
})
