/* eslint-disable no-console */
// #3417 (crítica de navegação, P1-3): um corte só para o trilho em toda rota (1180 px, o DESKTOP do useTamanhoTela) e,
// abaixo dele, o mesmo botão Menu em toda rota, que abre o trilho num modal. Garantia G3 (#2258) no corte novo: o Menu
// existe exatamente onde o trilho some. A 1024 e 1179 o topo do Atendimento fica numa linha só.
// Uso: node ferramentas/prova-3417-menu-do-tablet.mjs
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
process.env.VITE_FONTE_DADOS = 'demonstracao'
const { createServer } = await import('vite')
const raiz = fileURLToPath(new URL('..', import.meta.url))
const servidor = await createServer({ root: raiz, logLevel: 'error', ssr: { external: ['react', 'react-dom'] }, server: { host: '127.0.0.1', port: 5828, strictPort: false } })
await servidor.listen()
const origem = servidor.resolvedUrls.local[0].replace(/\/$/, '')
const navegador = await chromium.launch({ headless: true })
const falhas = []
const cobrar = (ok, msg) => { console.log(ok ? 'ok···' : 'FALHA', msg); if (!ok) falhas.push(msg) }

const ROTAS = { 'Início': '#/', Atendimento: '#/m/atendimento/balcao', Entregas: '#/m/entregas/painel', Cozinha: '#/m/cozinha' }
const LARGURAS = [768, 1024, 1140, 1179, 1180, 1440]

// Trilho à vista (um `nav` com link para o Início e para módulos) e botão Menu à vista.
const estado = (p) => p.evaluate(() => {
  const visivel = (el) => { const r = el.getBoundingClientRect(); const c = getComputedStyle(el); return r.width > 0 && r.height > 0 && c.visibility !== 'hidden' }
  const trilho = [...document.querySelectorAll('nav')].some((nav) => visivel(nav) && !nav.closest('dialog') && nav.querySelector('a[href="#/"]') && nav.querySelector('a[href^="#/m/"]'))
  const menus = [...document.querySelectorAll('button[aria-haspopup="dialog"]')].filter((b) => visivel(b) && /^Menu/.test(b.getAttribute('aria-label') ?? b.textContent.trim()))
  return { trilho, menu: menus.length }
})

try {
  for (const w of LARGURAS) {
    for (const [nome, hash] of Object.entries(ROTAS)) {
      const p = await navegador.newPage({ viewport: { width: w, height: 800 } })
      await p.goto(`${origem}/${hash}`, { timeout: 90000 })
      await p.locator('main').first().waitFor({ timeout: 30000 })
      await p.waitForTimeout(800)
      const e = await estado(p)
      if (w >= 1180) {
        cobrar(e.trilho && e.menu === 0, `${w} px, ${nome}: trilho à vista e sem Menu (trilho=${e.trilho}, menus=${e.menu})`)
      } else {
        cobrar(!e.trilho && e.menu === 1, `${w} px, ${nome}: sem trilho e com um Menu (trilho=${e.trilho}, menus=${e.menu})`)
        if (e.menu === 1) {
          await p.locator('button[aria-haspopup="dialog"]').filter({ hasText: /^Menu/ }).or(p.locator('button[aria-haspopup="dialog"][aria-label^="Menu"]')).first().click()
          await p.waitForTimeout(400)
          const noModal = await p.evaluate(() => {
            const d = document.querySelector('dialog[open]')
            return d ? { inicio: Boolean(d.querySelector('a[href="#/"]')), modulos: d.querySelectorAll('a[href^="#/m/"]').length } : null
          })
          cobrar(Boolean(noModal?.inicio) && noModal.modulos >= 3, `${w} px, ${nome}: o Menu abre o trilho num modal (${JSON.stringify(noModal)})`)
          await p.keyboard.press('Escape')
          await p.waitForTimeout(300)
          const foco = await p.evaluate(() => (document.activeElement?.getAttribute('aria-label') ?? document.activeElement?.textContent ?? '').trim())
          cobrar(/^Menu/.test(foco), `${w} px, ${nome}: Esc fecha o modal e o foco volta ao Menu (${foco})`)
        }
        if (nome === 'Atendimento' && (w === 1024 || w === 1179)) {
          const topoMedido = await p.evaluate(() => {
            const topo = document.querySelector('header')
            return { altura: Math.round(topo.getBoundingClientRect().height) }
          })
          cobrar(topoMedido.altura <= 72, `${w} px, Atendimento: topo numa linha só (${JSON.stringify(topoMedido)})`)
        }
      }
      await p.close()
    }
  }
} finally {
  await navegador.close()
  await servidor.close()
}
if (falhas.length) { console.log(`\n${falhas.length} falha(s)`); process.exit(1) }
console.log('\nprova-3417 verde')
