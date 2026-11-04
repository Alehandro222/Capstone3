import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// The dev server runs on port 5173, which the C# API already allows through CORS.
export default defineConfig({
  plugins: [react()],
  server: { port: 5173 },
})
