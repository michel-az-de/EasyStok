/* eslint-disable no-console */
// #3386 (revisão r5, item 2): a espera atrasada aparece UMA vez só no painel aberto e nunca some. Barra com o motivo
// "esperando": o cabeçalho não repete. Barra com outro motivo (passagem, reclamação, restrição): o cabeçalho diz
// "Cliente espera há …". Renderizado no navegador (modo demonstração), percorrendo as conversas.
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
process.env.VITE_FONTE_DADOS = 'demonstracao'
const { createServer } = await import('vite')
const raiz = fileURLToPath(new URL('..', import.meta.url))
const servidor = await createServer({ root: raiz, logLevel: 'error', server: { host: '127.0.0.1', port: 5807, strictPort: false } })
await servidor.listen()
const origem = servidor.resolvedUrls.local[0].replace(/\/$/, '')
const navegador = await chromium.launch({ headless: true })
const falhas = []
const cobrar = (ok, msg) => { console.log(ok ? 'ok   ' : 'FALHA', msg); if (!ok) falhas.push(msg) }
try {
  const p = await navegador.newPage({ viewport: { width: 1440, height: 900 } })
  await p.goto(`${origem}/#/m/atendimento/balcao`, { timeout: 90000 })
  await p.getByRole('button', { name: /^Abrir .+/ }).first().waitFor({ timeout: 30000 })
  const n = await p.locator('button[data-conversa-id]').count()
  const casos = { naBarra: 0, noCabecalho: 0 }
  for (let i = 0; i < Math.min(n, 20); i++) {
    await p.locator('button[data-conversa-id]').nth(i).click()
    await p.waitForTimeout(300)
    const r = await p.evaluate(() => {
      const cab = document.querySelector('h2[data-foco-reserva] small')?.textContent ?? ''
      const barra = [...document.querySelectorAll('main *, #conteudo-principal *')].filter((e) => e.children.length === 0 && /Cliente falou por último e espera/.test(e.textContent)).length
      return { cab, barra, cabeçalho: /Cliente espera há \d/.test(cab) }
    })
    console.log('conversa', i, JSON.stringify(r))
    cobrar(!(r.barra > 0 && r.cabeçalho), `conversa ${i}: a espera não aparece nos dois lugares`)
    if (r.barra > 0) casos.naBarra++
    if (r.cabeçalho) casos.noCabecalho++
  }
  cobrar(casos.naBarra > 0, 'a espera atrasada aparece na barra quando é o motivo')
  cobrar(casos.noCabecalho > 0, 'a espera atrasada aparece no cabeçalho quando a barra mostra outro motivo')
  await p.close()
} finally {
  await navegador.close()
  await servidor.close()
}
if (falhas.length) { console.log(`prova-3386-espera: ${falhas.length} falha(s)`); process.exit(1) }
console.log('prova-3386-espera: ok')
