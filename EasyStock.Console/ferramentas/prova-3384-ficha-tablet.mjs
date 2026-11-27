/* eslint-disable no-console */
// #3384 (r4 P1-1): no tablet (760 a 1179 px) a ficha mora numa gaveta aberta pelo botão "Ficha de X" do rodapé.
// A crítica r4 não achou a ficha a 1024 porque a pílula "Simular" cobria esse botão. Aqui o botão tem de estar
// inteiro à vista (nada por cima nos cantos nem no centro) e abrir a gaveta da ficha.
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
process.env.VITE_FONTE_DADOS = 'demonstracao'
const { createServer } = await import('vite')
const raiz = fileURLToPath(new URL('..', import.meta.url))
const servidor = await createServer({ root: raiz, logLevel: 'error', server: { host: '127.0.0.1', port: 5799, strictPort: false } })
await servidor.listen()
const origem = servidor.resolvedUrls.local[0].replace(/\/$/, '')
const navegador = await chromium.launch({ headless: true })
const falhas = []
const cobrar = (ok, msg) => { console.log(ok ? 'ok   ' : 'FALHA', msg); if (!ok) falhas.push(msg) }

try {
  for (const [w, h] of [[1024, 768], [1024, 640], [800, 1024]]) {
    const p = await navegador.newPage({ viewport: { width: w, height: h } })
    await p.goto(`${origem}/#/m/atendimento/balcao`, { timeout: 90000 })
    await p.getByRole('button', { name: /^Abrir .+/ }).first().waitFor({ timeout: 30000 })
    // Revisão da #3384: sem conversa aberta, o gatilho flutuante do Assistente e a pílula Simular não se cobrem.
    const choque = await p.evaluate(() => {
      const ret = (el) => el && el.getBoundingClientRect().width > 0 ? el.getBoundingClientRect() : null
      const sim = ret([...document.querySelectorAll('button')].find((b) => b.textContent.trim() === 'Simular'))
      const assist = ret(document.querySelector('button[aria-label="Abrir assistente"]'))
      if (!sim || !assist) return false
      return !(sim.right <= assist.left || sim.left >= assist.right || sim.bottom <= assist.top || sim.top >= assist.bottom)
    })
    cobrar(!choque, `${w}x${h}: Simular não cobre o gatilho do Assistente`)
    await p.getByRole('button', { name: /^Abrir .+/ }).first().click()
    const botao = p.getByRole('button', { name: /^Ficha de / })
    await botao.waitFor({ timeout: 10000 })
    await p.waitForTimeout(400)
    const coberto = await botao.evaluate((el) => {
      const r = el.getBoundingClientRect()
      const pontos = [[r.left + 3, r.top + 3], [r.right - 3, r.top + 3], [r.left + 3, r.bottom - 3], [r.right - 3, r.bottom - 3], [r.left + r.width / 2, r.top + r.height / 2]]
      return pontos.map(([x, y]) => document.elementFromPoint(x, y)).filter((no) => !el.contains(no)).map((no) => String(no?.className || no?.tagName))
    })
    cobrar(coberto.length === 0, `${w}x${h}: "Ficha de" inteiro à vista${coberto.length ? ` (coberto por ${[...new Set(coberto)].join(', ')})` : ''}`)
    await botao.click()
    const abriu = await p.getByRole('button', { name: 'Fechar Ficha' }).waitFor({ timeout: 5000 }).then(() => true, () => false)
    cobrar(abriu, `${w}x${h}: o botão abre a gaveta da ficha`)
    await p.close()
  }
} finally {
  await navegador.close()
  await servidor.close()
}
if (falhas.length) { console.log(`prova-3384-ficha-tablet: ${falhas.length} falha(s)`); process.exit(1) }
console.log('prova-3384-ficha-tablet: ok')
