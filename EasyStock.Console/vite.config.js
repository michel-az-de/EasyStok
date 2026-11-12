import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { execSync } from 'node:child_process'
import { plugin as metaDoSha } from './ferramentas/shaDoConsole.mjs'

// Em desenvolvimento, /api/agente e /api/saude vão para o servidor local do agente
// (npm run agente). O resto de /api vai para a API do EasyStok quando API_ALVO está
// definido (ex.: API_ALVO=https://api.easystok.online), o que liga o modo real no dev
// sem CORS. Em produção o Caddy serve o console e a API na mesma origem.
const agenteLocal = 'http://127.0.0.1:5245'
const apiAlvo = process.env.API_ALVO

// Assets externos com hash no nome (#2254): o HTML fica pequeno e sem cache, e o JS e o
// CSS em /assets/ ficam em cache imutável no navegador. Antes, o singlefile embutia
// tudo (2,5 MB) no index.html, que o servidor manda sem cache.
export default defineConfig({
  // #2393: o build de produção (modo API) falha sem a meta do SHA; o deploy passa CONSOLE_SHA.
  plugins: [react(), metaDoSha({ exigir: process.env.VITE_FONTE_DADOS === 'api', env: process.env, lerGit: () => { try { return execSync('git rev-parse HEAD', { stdio: ['ignore', 'pipe', 'ignore'] }).toString() } catch { return '' } } })],
  server: {
    proxy: {
      '/api/agente': agenteLocal,
      '/api/saude': agenteLocal,
      '/api': apiAlvo ? { target: apiAlvo, changeOrigin: true } : agenteLocal,
    },
  },
})
