import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { viteSingleFile } from 'vite-plugin-singlefile'

// Em desenvolvimento, /api/agente e /api/saude vão para o servidor local do agente
// (npm run agente). O resto de /api vai para a API do EasyStok quando API_ALVO está
// definido (ex.: API_ALVO=https://api.easystok.online), o que liga o modo real no dev
// sem CORS. Em produção o Caddy serve o console e a API na mesma origem.
const agenteLocal = 'http://127.0.0.1:5245'
const apiAlvo = process.env.API_ALVO

export default defineConfig({
  plugins: [react(), viteSingleFile()],
  build: { cssCodeSplit: false, assetsInlineLimit: 100000000 },
  server: {
    proxy: {
      '/api/agente': agenteLocal,
      '/api/saude': agenteLocal,
      '/api': apiAlvo ? { target: apiAlvo, changeOrigin: true } : agenteLocal,
    },
  },
})
