/* eslint-disable no-console */
// Prova da #3322 no navegador (demonstração): o botão Largura muda a largura do Assistente e do Cardápio sem arrastar,
// o controle segue preso ao botão quando o painel cresce, Escape fecha e devolve o foco, e a barra do Cardápio cabe
// na largura mínima (440 px) sem quebrar o título.
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import assert from 'node:assert/strict'

let falhas = 0
const caso = async (nome, fn) => {
  try { await fn(); console.log(`ok    ${nome}`) } catch (e) { falhas++; console.log(`FALHA ${nome}
      ${e.message}`) }
}

const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
// O executor sobe o Vite no modo API, sem dados; esta prova sobe o seu, no modo demonstração (`FONTE_API` falso).
process.env.VITE_FONTE_DADOS = 'demonstracao'
const { createServer } = await import('vite')
const servidor = await createServer({ root: fileURLToPath(new URL('..', import.meta.url)), logLevel: 'error', server: { host: '127.0.0.1', port: 5795, strictPort: false } })
await servidor.listen()
const origem = servidor.resolvedUrls.local[0].replace(/\/$/, '')
const navegador = await chromium.launch({ headless: true })
const p = await navegador.newPage({ viewport: { width: 1440, height: 900 } })
const erros = []
p.on('pageerror', (e) => erros.push(e.message))
const largura = async (alvo) => Math.round((await alvo.boundingBox()).width)

await p.goto(`${origem}/#/m/atendimento/balcao`, { timeout: 90000 })
await p.getByRole('button', { name: /Abrir assistente/i }).first().click()
const painel = p.locator('section[role=dialog]').first()
await painel.waitFor()

await caso('Assistente: o controle muda a largura, segue preso ao botão e Restaurar volta ao padrão', async () => {
  await p.getByRole('button', { name: 'Encaixar à esquerda' }).click()
  const antes = await largura(painel)
  const botao = p.getByRole('button', { name: 'Largura do assistente' })
  await botao.click()
  const faixa = p.getByRole('slider', { name: 'Largura do assistente' })
  await assert.doesNotReject(faixa.waitFor())
  for (let i = 0; i < 40; i++) await faixa.press('ArrowRight')
  await p.waitForTimeout(200)
  assert.ok(await largura(painel) > antes, 'o painel cresceu')
  const b = await botao.boundingBox()
  const caixa = await p.getByRole('dialog', { name: 'Largura do assistente' }).boundingBox()
  const distancia = Math.round(Math.abs(caixa.x + caixa.width - (b.x + b.width)))
  assert.ok(distancia <= 8, `o controle segue preso ao botão (borda direita a ${distancia} px)`)
  await p.getByRole('button', { name: 'Restaurar largura' }).click()
  await p.waitForTimeout(200)
  assert.equal(await largura(painel), antes)
})

await caso('Assistente: Escape fecha o controle, devolve o foco ao botão e o painel continua aberto', async () => {
  await p.getByRole('slider', { name: 'Largura do assistente' }).focus()
  await p.keyboard.press('Escape')
  await p.waitForTimeout(200)
  assert.equal(await p.getByRole('slider', { name: 'Largura do assistente' }).count(), 0)
  assert.equal(await p.evaluate(() => document.activeElement?.getAttribute('aria-label')), 'Largura do assistente')
  assert.ok(await painel.isVisible(), 'o Assistente segue aberto')
})

await caso('Cardápio: o controle muda a largura e a barra cabe em 440 px', async () => {
  await p.reload()
  await p.locator('[role=list] [role=listitem]').first().waitFor()
  for (const c of await p.locator('[role=list] [role=listitem] button, [role=list] li a, [role=list] li button').all()) {
    await c.click()
    const itens = p.getByRole('button', { name: 'Adicionar itens' }).first()
    if (await itens.isVisible() && await itens.isEnabled()) { await itens.click(); break }
  }
  const aside = p.locator('aside[aria-label="Cardápio de hoje"]')
  await aside.waitFor()
  await p.getByRole('button', { name: 'Largura do cardápio' }).click()
  const faixa = p.getByRole('slider', { name: 'Largura do cardápio' })
  await faixa.focus()
  await faixa.press('Home')
  await p.waitForTimeout(200)
  assert.equal(await largura(aside), 440)
  const h2 = aside.locator('h2').first()
  const alturaLinha = await h2.evaluate((e) => parseFloat(getComputedStyle(e).lineHeight))
  assert.ok((await h2.boundingBox()).height <= alturaLinha * 1.2, 'o título fica numa linha')
  const sobra = await aside.evaluate((e) => e.scrollWidth - e.clientWidth)
  assert.equal(sobra, 0, 'nada sai pela lateral')
  for (let i = 0; i < 10; i++) await faixa.press('ArrowRight')
  await p.waitForTimeout(200)
  assert.equal(await largura(aside), 450)
})

await caso('sem erro de página', () => assert.deepEqual(erros, []))

await navegador.close()
await servidor.close()
console.log(`
${4 - falhas} ok, ${falhas} falha(s)`)
process.exit(falhas ? 1 : 0)
