// #3044: Cozinha legível a distância. O pedido sem janela diz há quanto tempo espera (base pagoEm ?? criadoEm, a mesma
// de tempoAteInicio), com algarismos tabulares; o pedido com janela mantém o aviso que já existe, sem segundo tempo.
// A linha principal (identificação e situação) usa tokens maiores que --t-corpo e --t-apoio, a 1440 e a 390.
// Modo API (CONSOLE_URL, o que o rodar-provas sobe) com a API interceptada. PLAYWRIGHT_MODULE aponta o Playwright.
//
//   node ferramentas/prova-3044-cozinha-tempo.mjs
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const origem = process.env.CONSOLE_URL ?? 'http://127.0.0.1:5790'

const MIN = 60000
const base = (n, extra) => ({
  id: `00000000-0000-4000-8000-00000000000${n}`, numeroCurto: `A${n}B${n}C${n}D${n}`, numeroComanda: n, numeroDoDia: n,
  clienteNome: `Cliente ${n}`, status: 'aguardando', statusRotulo: 'Aguardando', itens: [], atrasado: false, janela: null, ...extra,
})

async function abrir(browser, largura, altura) {
  const agora = Date.now()
  const pedidos = [
    base(1, { pagoEm: new Date(agora - 12 * MIN).toISOString(), criadoEm: new Date(agora - 50 * MIN).toISOString() }),
    base(2, { criadoEm: new Date(agora - 7 * MIN).toISOString() }),
    base(3, { criadoEm: new Date(agora - 2 * 86400000).toISOString(), pagoEm: new Date(agora - 2 * 86400000).toISOString(),
      inicioPrevistoEm: new Date(agora + 3 * 3600000).toISOString(), janela: { label: 'Hoje, 18:00' } }),
    base(4, { status: 'preparando', pagoEm: new Date(agora - 25 * MIN).toISOString() }),
  ]
  const context = await browser.newContext({ viewport: { width: largura, height: altura }, reducedMotion: 'reduce' })
  await context.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'prova-controlada',
    expiraEm: Date.now() + 3600000, empresa: { id: '11111111-1111-4111-8111-111111111111', nome: 'Empresa de teste' },
    usuario: { id: '22222222-2222-4222-8222-222222222222', nome: 'Dona de teste' } })))
  await context.route('**/api/**', (route) => {
    const caminho = new URL(route.request().url()).pathname
    if (!caminho.startsWith('/api/')) return route.continue()
    const ok = (data) => route.fulfill({ json: { data, meta: {} } })
    if (caminho === '/api/auth/me/modulos') return ok({ modulos: ['cozinha', 'atendimento'].map((id) => ({ id, liberado: true })), acoes: {} })
    if (caminho === '/api/kds/pedidos') return ok(pedidos)
    if (caminho.includes('/eventos')) return route.abort()
    return ok([])
  })
  const page = await context.newPage()
  await page.goto(`${origem}/#/m/cozinha`, { waitUntil: 'domcontentloaded' })
  await page.locator('section[aria-label]').first().waitFor({ timeout: 30000 })
  await page.evaluate(() => document.fonts.ready)
  return { context, page }
}

const medir = (page) => page.evaluate(() => {
  const cartoes = [...document.querySelectorAll('li[class*="cartao"]')].filter((li) => li.querySelector('[class*="numeroCartao"]'))
  const px = (el) => parseFloat(getComputedStyle(el).fontSize)
  return cartoes.map((li) => {
    const num = li.querySelector('[class*="numeroCartao"]').textContent
    const sit = li.querySelector('[class*="rotuloSituacao"]')
    const tempo = li.querySelector('[data-tempo]')
    return {
      num, situacao: sit.textContent.trim(), pxNumero: px(li.querySelector('[class*="numeroCartao"]')), pxSituacao: px(sit),
      tempo: tempo?.textContent ?? null, tabular: tempo ? getComputedStyle(tempo).fontVariantNumeric : null,
    }
  })
})

const browser = await chromium.launch({ headless: true })
try {
  for (const [largura, altura] of [[1440, 900], [390, 844]]) {
    const { context, page } = await abrir(browser, largura, altura)
    try {
      const m = await medir(page)
      console.log(`medida ${largura}:`, JSON.stringify(m.map((c) => [c.num.slice(0, 12), c.pxNumero, c.pxSituacao, c.tempo])))
      const por = (n) => m.find((c) => c.num.startsWith(`Pedido ${n}`))
      assert.ok(por(1) && por(2) && por(3) && por(4), `[${largura}] os quatro cartões estão no quadro`)
      assert.equal(por(1).tempo, 'há 12 min', `[${largura}] pagoEm manda sobre criadoEm: ${por(1).tempo}`)
      assert.equal(por(2).tempo, 'há 7 min', `[${largura}] sem pagoEm, criadoEm: ${por(2).tempo}`)
      assert.equal(por(4).tempo, 'há 25 min', `[${largura}] em preparo também: ${por(4).tempo}`)
      assert.equal(por(3).tempo, null, `[${largura}] pedido com janela não ganha segundo tempo (era "há 2 dias")`)
      assert.ok(!/há \d/.test(por(3).situacao), `[${largura}] aviso da janela intacto: ${por(3).situacao}`)
      assert.match(por(1).tabular, /tabular-nums/, `[${largura}] algarismos tabulares no tempo`)
      for (const c of m) {
        assert.ok(c.pxNumero >= 20, `[${largura}] identificação do pedido com ${c.pxNumero}px, esperado >= 20px (--t-titulo)`)
        assert.ok(c.pxSituacao >= 17, `[${largura}] situação e tempo com ${c.pxSituacao}px, esperado >= 17px (--t-forte)`)
      }
      const sobra = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)
      assert.ok(sobra <= 0, `[${largura}] a página não rola na horizontal (sobra ${sobra}px)`)
    } finally { await context.close() }
  }
  console.log('ok    cozinha: tempo decorrido no cartão e linha principal legível a 1440 e a 390')
} finally { await browser.close() }
