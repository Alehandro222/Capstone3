import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

// The dev server runs on port 5173 and forwards /api to the C# API, so the
// browser only ever talks to one address and the staff sign-in cookie just works.
// API address: API_URL in the environment or in a .env file, else localhost:5080.
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const API = env.API_URL || 'http://localhost:5080'

  return {
    plugins: [react()],
    server: {
      port: 5173,
      proxy: {
        '/api': {
          target: API,
          // Tell the API each phone's real address (X-Forwarded-For). Without it every
          // phone looks like this server, and they all share one rate limit.
          xfwd: true,
          // API down or still starting: answer 502 with a message the pages can show,
          // instead of the default empty 500 (which looks like an API bug).
          configure: (proxy) => {
            proxy.on('error', (_err, _req, res) => {
              if (!res.headersSent && res.writeHead) {
                res.writeHead(502, { 'Content-Type': 'application/json' })
              }
              res.end?.(JSON.stringify({ message: 'Cannot reach the API. Is it running?' }))
            })
          },
        },
      },
    },
  }
})
