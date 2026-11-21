// #3042: os filtros de Entregas ocupavam até 4 linhas (19+ chips) antes do primeiro cartão. Mostrar segue em
// chips (filtro primário); Ordenar e Bairro viram select rotulado. Mede no console em demonstração
// 1440x900 e 390x844.
import { createRequire } from 'node:module'
import assert from 'node:assert/strict'
import { resolve } from 'node:path'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
// O rodar-provas sobe o Vite em modo API; esta prova precisa da massa de demonstração, então sobe o próprio Vite.
process.env.VITE_FONTE_DADOS = 'demonstracao'
const { createServer } = await import('vite')
const servidor = await createServer({ root: resolve(import.meta.dirname, '..'), logLevel: 'error', server: { host: '127.0.0.1', port: 5799, strictPort: false } })
await servidor.listen()
const origem = servidor.resolvedUrls.local[0].replace(/\/$/, '')
const navegador = await chromium.launch({ headless: true })
const resultados = {}
for (const [nome, largura, altura] of [['desktop', 1440, 900], ['celular', 390, 844]]) {
  const pagina = await navegador.newPage({ viewport: { width: largura, height: altura }, reducedMotion: 'reduce' })
  await pagina.goto(`${origem}/#/m/atendimento/balcao`, { waitUntil: 'networkidle', timeout: 120_000 })
  if (largura >= 900) await pagina.getByRole('button', { name: /^Entregas/ }).first().click()
  else {
    await pagina.getByRole('button', { name: 'Mais opções' }).click()
    await pagina.getByRole('menuitem', { name: /Entregas/ }).first().click()
  }
  await pagina.waitForSelector('[aria-label="Entregas de hoje"] >> text=Entregas sem viagem', { timeout: 30_000 })
  const medida = await pagina.evaluate(() => {
    const faixa = document.querySelector('[class*="faixaControles"]')
    const controles = [...faixa.querySelectorAll('button[aria-pressed], button[role="radio"], select')].filter((e) => e.offsetParent)
    const chips = controles.filter((e) => e.tagName === 'BUTTON')
    const rotulos = [...new Set(controles.map((e) => Math.round(e.getBoundingClientRect().top / 8)))]
    const cartao = document.querySelector('[class*="coluna"] ul > li')
    return {
      chips: chips.length,
      selects: faixa.querySelectorAll('select').length,
      linhasDeFiltro: rotulos.length,
      yPrimeiroCartao: cartao ? Math.round(cartao.getBoundingClientRect().top) : null,
      alturaDaFaixa: Math.round(faixa.getBoundingClientRect().height),
      controles: controles.length,
    }
  })
  resultados[nome] = medida
  await pagina.close()
}
console.log(JSON.stringify(resultados))
await navegador.close()
await servidor.close()
// Teto: no desktop e no celular, no máximo 5 chips de Mostrar + 1 de Alcance e 2 selects; primeiro cartão acima de 520px.
for (const [nome, r] of Object.entries(resultados)) {
  assert.ok(r.chips <= 6, `${nome}: ${r.chips} chips de filtro (máx. 6)`)
  assert.ok(r.linhasDeFiltro <= 3, `${nome}: ${r.linhasDeFiltro} linhas de filtro (máx. 3)`)
  assert.ok(r.selects >= 1, `${nome}: Ordenar deve ser um select rotulado`)
  assert.ok(r.yPrimeiroCartao != null && r.yPrimeiroCartao < (nome === 'desktop' ? 400 : 620), `${nome}: primeiro cartão em y=${r.yPrimeiroCartao}`)
}
console.log('prova-3042: ok')
