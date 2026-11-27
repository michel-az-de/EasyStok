/* eslint-disable no-console */
// Prova da #3368 no navegador (demonstração): entre a barra e o campo há um laranja só. Campo vazio: "Assumir conversa".
// Com texto ou com um anexo em espera (sem texto): "Enviar e assumir", e o Assumir vira secundário (#3300 F4).
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import assert from 'node:assert/strict'

let falhas = 0
const caso = async (nome, fn) => {
  try { await fn(); console.log(`ok    ${nome}`) } catch (e) { falhas++; console.log(`FALHA ${nome}
      ${e.message}`) }
}

const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
// O executor sobe o Vite no modo API, sem dados; esta prova sobe o seu, no modo demonstração.
process.env.VITE_FONTE_DADOS = 'demonstracao'
const { createServer } = await import('vite')
const servidor = await createServer({ root: fileURLToPath(new URL('..', import.meta.url)), logLevel: 'error', server: { host: '127.0.0.1', port: 5797, strictPort: false } })
await servidor.listen()
const origem = servidor.resolvedUrls.local[0].replace(/\/$/, '')
const navegador = await chromium.launch({ headless: true })
const p = await navegador.newPage({ viewport: { width: 1440, height: 900 } })
const PNG = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==', 'base64')

// Cor normalizada num canvas de 1 px: color-mix e rgb() serializam diferente.
const cores = () => p.evaluate(() => {
  const ctx = document.createElement('canvas').getContext('2d', { willReadFrequently: true })
  const pixel = (css) => { ctx.clearRect(0, 0, 1, 1); ctx.fillStyle = '#000'; ctx.fillStyle = css; ctx.fillRect(0, 0, 1, 1); return ctx.getImageData(0, 0, 1, 1).data.join(',') }
  const fundo = (el) => el ? pixel(getComputedStyle(el).backgroundColor) : null
  const sonda = document.createElement('i'); sonda.style.background = 'var(--acao)'; document.querySelector('[data-conteudo-console]').append(sonda)
  const acao = fundo(sonda); sonda.remove()
  const assumir = [...document.querySelectorAll('button')].find((b) => /^Assumir conversa\s*A?$/.test(b.textContent.trim()))
  const enviar = document.querySelector('button[aria-label="Enviar e assumir a conversa"]')
  return { assumirLaranja: fundo(assumir) === acao, enviarLaranja: fundo(enviar) === acao, enviarHabilitado: enviar ? !enviar.disabled : null }
})

await p.goto(`${origem}/#/m/atendimento/balcao`, { timeout: 90000 })
// A cor do botão tem transição: sem ela a leitura não depende do relógio do CI.
await p.addStyleTag({ content: '*, *::before, *::after { transition: none !important; }' })
const itens = p.locator('[role=list] [role=listitem], [role=list] > li')
await itens.first().waitFor({ timeout: 90000 })
let achou = false
for (let i = 0; i < await itens.count() && !achou; i++) {
  await itens.nth(i).locator('button, a').first().click().catch(() => {})
  await p.waitForTimeout(300)
  achou = (await p.getByRole('button', { name: 'Assumir conversa' }).count()) > 0
}
assert.ok(achou, 'a demonstração tem uma conversa com Assumir')
const enviarPronto = () => p.waitForFunction(() => document.querySelector('button[aria-label="Enviar e assumir a conversa"]')?.disabled === false, null, { timeout: 5000 })
const composer = p.locator('[class*="_composer_"]').filter({ has: p.locator('textarea') }).last()

await caso('campo vazio: só o Assumir é laranja', async () => {
  const c = await cores()
  assert.equal(c.assumirLaranja, true, 'Assumir laranja')
  assert.equal(c.enviarLaranja, false, 'Enviar e assumir sem laranja')
})

await caso('com texto: só o Enviar e assumir é laranja', async () => {
  await composer.locator('textarea').fill('oi')
  await enviarPronto()
  const c = await cores()
  assert.deepEqual([c.assumirLaranja, c.enviarLaranja], [false, true])
  await composer.locator('textarea').fill('')
})

await caso('anexo sem texto: só o Enviar e assumir é laranja', async () => {
  // O chat do site não aceita foto: vale a primeira conversa com Assumir que aceite o anexo (WhatsApp).
  let anexou = false
  for (let i = 0; i < await itens.count() && !anexou; i++) {
    await itens.nth(i).locator('button, a').first().click().catch(() => {})
    await p.waitForTimeout(300)
    if (!(await p.getByRole('button', { name: 'Assumir conversa' }).count())) continue
    const enviou = await composer.locator('input[type="file"]').setInputFiles({ name: 'prova.png', mimeType: 'image/png', buffer: PNG }, { timeout: 1500 }).then(() => true, () => false)
    if (!enviou) continue
    anexou = await p.getByRole('button', { name: 'Remover anexo prova.png', exact: true }).waitFor({ timeout: 1500 }).then(() => true, () => false)
  }
  assert.ok(anexou, 'a demonstração tem uma conversa com Assumir que aceita anexo')
  await enviarPronto()
  const c = await cores()
  assert.equal(c.enviarHabilitado, true, 'Enviar e assumir habilitado com anexo')
  assert.deepEqual([c.assumirLaranja, c.enviarLaranja], [false, true], 'um laranja só')
})

await navegador.close()
await servidor.close()
if (falhas) { console.log(`${falhas} caso(s) falharam`); process.exit(1) }
