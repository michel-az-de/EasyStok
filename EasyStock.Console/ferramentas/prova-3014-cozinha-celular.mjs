// #3014: Cozinha no celular. A 390x844 o quadro é um carrossel de uma coluna por tela (decisão da #2258) e as contagens
// das outras três ficavam fora da vista. A faixa fixa mostra nome e contagem das quatro colunas, tocar leva o
// carrossel até a coluna, e a 1440x900 a faixa some e as quatro colunas seguem lado a lado.
// Modo API (CONSOLE_URL, o que o rodar-provas sobe) com a API interceptada. Com CONSOLE_DEMO_URL aponta um Vite em
// modo demonstração e a mesma prova roda nele também. PLAYWRIGHT_MODULE aponta uma instalação de Playwright.
//
//   node ferramentas/prova-3014-cozinha-celular.mjs
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const origemApi = process.env.CONSOLE_URL ?? 'http://127.0.0.1:5790'
const origemDemo = process.env.CONSOLE_DEMO_URL ?? null
const capturas = process.env.EVIDENCIAS_UX ?? null
if (capturas) mkdirSync(capturas, { recursive: true })

const pedido = (n, status) => ({
  id: `00000000-0000-4000-8000-00000000000${n}`, numeroCurto: `A${n}B${n}C${n}D${n}`, numeroComanda: n, numeroDoDia: n,
  clienteNome: `Cliente ${n}`, status, statusRotulo: status, itens: [], atrasado: false, janela: null,
})
const pedidos = [['aguardando', 3], ['preparando', 2], ['pronto', 1], ['saiu_para_entrega', 1]]
  .flatMap(([status, qtd], i) => Array.from({ length: qtd }, (_, k) => pedido(i * 3 + k + 1, status)))

// A demonstração tem as cinco etapas da esteira (a primeira se chama Na fila no título, Pago na seção).
const COLUNAS_DEMO = ['Na fila', 'Em preparo', 'Pronto', 'Saiu para entrega', 'Entregue']
const COLUNAS_API = ['Na fila', 'Em preparo', 'Pronto', 'Saiu para entrega']

async function abrir(browser, origem, modo, largura, altura, tema) {
  const context = await browser.newContext({ viewport: { width: largura, height: altura }, reducedMotion: 'reduce', colorScheme: tema })
  if (modo === 'api') {
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
  }
  const page = await context.newPage()
  await page.goto(`${origem}/#/m/cozinha`, { waitUntil: 'domcontentloaded' })
  await page.locator('section[aria-label]').first().waitFor({ timeout: 30000 })
  await page.evaluate(() => document.fonts.ready)
  return { context, page }
}

async function provar(browser, origem, modo) {
  const nomes = modo === 'api' ? COLUNAS_API : COLUNAS_DEMO
  // 1. 390x844: as contagens das 4 colunas visíveis, sem rolagem horizontal da página.
  const { context, page } = await abrir(browser, origem, modo, 390, 844, 'light')
  try {
    const secoes = await page.locator('section[aria-label]').count()
    assert.ok(secoes >= nomes.length, `[${modo}] as ${nomes.length} colunas seguem no DOM (carrossel intacto): ${secoes}`)
    const sobra = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)
    assert.ok(sobra <= 0, `[${modo}] a página não rola na horizontal (sobra ${sobra}px)`)
    const toque = await page.evaluate(() => parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--toque')) || 40)
    const faixa = []
    for (const n of nomes) {
      const botao = page.getByRole('button', { name: new RegExp(`^${n}, \\d+ pedidos?$`) })
      assert.equal(await botao.count(), 1, `[${modo}] a contagem de "${n}" não está visível sem rolar: falta o botão "${n}, N pedidos" na faixa`)
      const caixa = await botao.boundingBox()
      assert.ok(caixa && caixa.x >= 0 && caixa.x + caixa.width <= 390 && caixa.y >= 0 && caixa.y + caixa.height <= 844,
        `[${modo}] "${n}" fora do viewport: ${JSON.stringify(caixa)}`)
      assert.ok(caixa.height >= toque - 0.5, `[${modo}] alvo de toque de "${n}" ${caixa.height}px < ${toque}px`)
      faixa.push(await botao.getAttribute('aria-label'))
    }
    if (modo === 'api') assert.deepEqual(faixa, ['Na fila, 3 pedidos', 'Em preparo, 2 pedidos', 'Pronto, 1 pedido', 'Saiu para entrega, 1 pedido'])
    if (capturas) await page.screenshot({ path: join(capturas, `${modo}-390-claro.png`) })
    // 2. Tocar rola o carrossel até a coluna e a faixa marca a coluna à vista.
    const ultimo = nomes.at(-1)
    await page.getByRole('button', { name: new RegExp(`^${ultimo}, \\d+ pedidos?$`) }).click()
    await page.waitForFunction((i) => {
      const c = document.querySelectorAll('section[data-coluna]')[i]?.getBoundingClientRect()
      return c && c.left >= -1 && c.left < innerWidth - 40
    }, nomes.length - 1, { timeout: 5000 })
    await page.waitForFunction((n) => document.querySelector('nav[aria-label="Colunas do quadro"] [aria-current="true"]')
      ?.getAttribute('aria-label')?.startsWith(n), ultimo, { timeout: 5000 })
    if (capturas) await page.screenshot({ path: join(capturas, `${modo}-390-claro-ultima.png`) })
  } finally { await context.close() }
  // 3. 1440x900: faixa oculta e as quatro colunas lado a lado.
  const larga = await abrir(browser, origem, modo, 1440, 900, 'dark')
  try {
    const faixaOculta = await larga.page.locator('nav[aria-label="Colunas do quadro"]').evaluate((e) => getComputedStyle(e).display)
    assert.equal(faixaOculta, 'none', `[${modo}] a faixa fica oculta no desktop`)
    const tops = await larga.page.locator('section[aria-label]').evaluateAll((s) => s.map((e) => Math.round(e.getBoundingClientRect().top)))
    assert.ok(tops.length >= 4 && new Set(tops.slice(0, 4)).size === 1, `[${modo}] as quatro colunas na mesma linha: ${tops}`)
    if (capturas) await larga.page.screenshot({ path: join(capturas, `${modo}-1440-escuro.png`) })
  } finally { await larga.context.close() }
  console.log(`ok    [${modo}] faixa com as 4 contagens a 390 px, toque leva à coluna, desktop sem a faixa`)
}

const browser = await chromium.launch({ headless: true })
try {
  await provar(browser, origemApi, 'api')
  if (origemDemo) await provar(browser, origemDemo, 'demo')
} finally { await browser.close() }

// 4. As duas telas desenham a mesma faixa: um componente só, usado nos dois arquivos.
const fonte = (arquivo) => readFileSync(new URL(`../src/features/cozinha/${arquivo}`, import.meta.url), 'utf8')
for (const arquivo of ['ConteudoCozinha.jsx', 'TelaCozinhaApi.jsx']) {
  assert.match(fonte(arquivo), /<FaixaColunas\b/, `${arquivo} usa FaixaColunas`)
  assert.match(fonte(arquivo), /data-coluna=/, `${arquivo} marca as colunas com data-coluna`)
}
console.log('ok    ConteudoCozinha e TelaCozinhaApi usam o mesmo FaixaColunas')
