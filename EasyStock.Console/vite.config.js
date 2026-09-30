import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { viteSingleFile } from 'vite-plugin-singlefile'

export default defineConfig({
  plugins: [react(), viteSingleFile()],
  build: { cssCodeSplit: false, assetsInlineLimit: 100000000 },
  // Em desenvolvimento, /api vai para o servidor local do agente (npm run agente).
  server: { proxy: { '/api': 'http://127.0.0.1:5245' } },
})
