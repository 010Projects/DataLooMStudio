import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': 'http://localhost:5287',
      '/healthz': 'http://localhost:5287',
      '/openapi': 'http://localhost:5287',
    },
  },
})
