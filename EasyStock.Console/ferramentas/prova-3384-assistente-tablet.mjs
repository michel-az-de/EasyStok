/* eslint-disable no-console */
// #3384 (r4 P1-5): no tablet (760 a 1179 px) o Assistente encaixado cobria o cabeçalho, as mensagens e o "Assumir conversa".
// Agora ele toma o lugar da lista de conversas enquanto está aberto (como no computador estreito ele toma o da ficha):
// a conversa e o Assumir ficam inteiros à vista, e ao fechar a lista volta.
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
process.env.VITE_FONTE_DADOS = 'demonstracao'
const { createServer } = await import('vite')
const raiz = fileURLToPath(new URL('..', import.meta.url))
const servidor = await createServer({ root: raiz, logLevel: 'error', server: { host: '127.0.0.1', port: 5802, strictPort: false } })
await servidor.listen()
const origem = servidor.resolvedUrls.local[0].replace(/\/$/, '')
const navegador = await chromium.launch({ headless: true })
const falhas = []
const cobrar = (ok, msg) => { console.log(ok ? 'ok   ' : 'FALHA', msg); if (!ok) falhas.push(msg) }

try {
  for (const [w, h] of [[1024, 768], [800, 1024], [1100, 800]]) {
    const p = await navegador.newPage({ viewport: { width: w, height: h } })
    await p.goto(`${origem}/#/m/atendimento/balcao`, { timeout: 90000 })
    await p.getByRole('button', { name: /^Abrir .+/ }).first().waitFor({ timeout: 30000 })
    await p.getByRole('button', { name: /^Abrir .+/ }).first().click()
    await p.getByRole('button', { name: /^Assumir conversa/ }).waitFor({ timeout: 10000 })
    await p.getByRole('button', { name: 'Abrir assistente' }).first().click()
    const painel = p.locator('section[role="dialog"]').first()
    await painel.waitFor({ timeout: 5000 })
    await p.waitForTimeout(500)
    const r = await p.evaluate(() => {
      const painel = [...document.querySelectorAll('section[role="dialog"]')].find((s) => s.getBoundingClientRect().width > 0)
      const conversa = document.querySelector('section[aria-label="Conversa"]')
      const assumir = [...document.querySelectorAll('button')].find((b) => /^Assumir conversa/.test(b.textContent.trim()))
      const pr = painel.getBoundingClientRect(); const cr = conversa.getBoundingClientRect(); const ar = assumir.getBoundingClientRect()
      const no = document.elementFromPoint(ar.left + ar.width / 2, ar.top + ar.height / 2)
      const sobrepoe = !(pr.right <= cr.left + 1 || pr.left >= cr.right - 1)
      return { sobrepoe, assumirCoberto: !assumir.contains(no), conversa: Math.round(cr.width), painel: Math.round(pr.width) }
    })
    cobrar(!r.sobrepoe, `${w}x${h}: o painel não cobre a conversa (conversa ${r.conversa} px, painel ${r.painel} px)`)
    cobrar(!r.assumirCoberto, `${w}x${h}: "Assumir conversa" à vista com o Assistente aberto`)
    cobrar(r.conversa >= 320, `${w}x${h}: a conversa fica com pelo menos 320 px`)
    const fichaCoberta = await p.getByRole('button', { name: /^Ficha de / }).evaluate((el) => {
      const b = el.getBoundingClientRect()
      return [[b.left + 3, b.top + 3], [b.right - 3, b.bottom - 3], [b.left + b.width / 2, b.top + b.height / 2]]
        .some(([x, y]) => !el.contains(document.elementFromPoint(x, y)))
    })
    cobrar(!fichaCoberta, `${w}x${h}: "Ficha de" continua à vista com o Assistente aberto`)
    await p.keyboard.press('Escape')
    await p.waitForTimeout(400)
    const listaVolta = await p.locator('section[aria-label="Conversas"]').isVisible()
    cobrar(listaVolta, `${w}x${h}: fechado o Assistente, a lista de conversas volta`)
    await p.close()
  }
} finally {
  await navegador.close()
  await servidor.close()
}
if (falhas.length) { console.log(`prova-3384-assistente-tablet: ${falhas.length} falha(s)`); process.exit(1) }
console.log('prova-3384-assistente-tablet: ok')
