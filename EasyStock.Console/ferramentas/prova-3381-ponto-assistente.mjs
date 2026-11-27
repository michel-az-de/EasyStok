/* eslint-disable no-console */
// Prova da #3381 (A2) no navegador (demonstração): o ponto vermelho do botão Assistente indica um alerta real (CDC,
// foto suspeita ou sentimento negativo). Quem não enxerga o ponto precisa ouvir o que ele diz: o botão leva
// `title` e `aria-describedby` com o significado; o nome acessível segue "Abrir assistente" (prova-2185 mede exato).
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import assert from 'node:assert/strict'

let falhas = 0
const caso = async (nome, fn) => {
  try { await fn(); console.log(`ok    ${nome}`) } catch (e) { falhas++; console.log(`FALHA ${nome}\n      ${e.message}`) }
}
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
process.env.VITE_FONTE_DADOS = 'demonstracao'
const { createServer } = await import('vite')
const servidor = await createServer({ root: fileURLToPath(new URL('..', import.meta.url)), logLevel: 'error', server: { host: '127.0.0.1', port: 5798, strictPort: false } })
await servidor.listen()
const origem = servidor.resolvedUrls.local[0].replace(/\/$/, '')
const navegador = await chromium.launch({ headless: true })
const p = await navegador.newPage({ viewport: { width: 1440, height: 900 } })
await p.goto(`${origem}/#/m/atendimento/balcao`, { timeout: 90000 })
const itens = p.locator('[role=list] [role=listitem], [role=list] > li')
await itens.first().waitFor({ timeout: 90000 })
const botao = () => p.getByRole('button', { name: 'Abrir assistente', exact: true }).first()
const ponto = () => botao().locator('span[aria-hidden="true"]')
let comPonto = false, semPonto = false
for (let i = 0; i < await itens.count() && !(comPonto && semPonto); i++) {
  await itens.nth(i).locator('button, a').first().click().catch(() => {})
  await p.waitForTimeout(250)
  if (!(await botao().count())) continue
  if (await ponto().count()) comPonto = true; else semPonto = true
  if (await ponto().count()) break
}
await caso('há conversa com o ponto no Assistente', async () => assert.ok(comPonto, 'a demonstração não tem conversa com o ponto'))
await caso('com ponto: o botão diz o que ele indica (title e descrição acessível) e o nome segue "Abrir assistente"', async () => {
  const b = botao()
  assert.match(await b.getAttribute('title') ?? '', /alerta|atenção|olhar/i, 'title')
  const id = await b.getAttribute('aria-describedby')
  assert.ok(id, 'aria-describedby')
  assert.match(await p.locator(`[id="${id}"]`).textContent(), /alerta|atenção|olhar/i, 'descrição')
})
await navegador.close()
await servidor.close()
if (falhas) { console.log(`${falhas} caso(s) falharam`); process.exit(1) }
