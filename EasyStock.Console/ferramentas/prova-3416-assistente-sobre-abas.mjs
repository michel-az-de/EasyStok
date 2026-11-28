/* eslint-disable no-console */
// #3416 (crítica de navegação, P1-4): no celular o botão flutuante do Assistente não cobre nenhuma aba de baixo.
// A faixa de avisos do aparelho (notificação negada, som bloqueado) fica abaixo da barra de abas e a sobe; o
// gatilho, `fixed` no pé da janela, precisa contar essa faixa. Mede com e sem a faixa, nas abas Conversas e Mensagens
// (sem conversa aberta, quando o gatilho flutua): o centro de cada aba responde à própria aba.
// Uso: node ferramentas/prova-3416-assistente-sobre-abas.mjs
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
process.env.VITE_FONTE_DADOS = 'demonstracao'
const { createServer } = await import('vite')
const raiz = fileURLToPath(new URL('..', import.meta.url))
const servidor = await createServer({ root: raiz, logLevel: 'error', ssr: { external: ['react', 'react-dom'] }, server: { host: '127.0.0.1', port: 5827, strictPort: false } })
await servidor.listen()
const origem = servidor.resolvedUrls.local[0].replace(/\/$/, '')
const navegador = await chromium.launch({ headless: true })
const falhas = []
const cobrar = (ok, msg) => { console.log(ok ? 'ok···' : 'FALHA', msg); if (!ok) falhas.push(msg) }

// O que está no centro de cada aba de baixo, e se a faixa do aparelho está à vista.
const medir = (p) => p.evaluate(() => {
  const abas = [...document.querySelectorAll('[role=tab]')].filter((t) => t.closest('[data-painel-mobile]') === null)
  return {
    faixa: Boolean(document.querySelector('[aria-label="Dispensar avisos do aparelho"]')),
    abas: abas.map((t) => {
      const r = t.getBoundingClientRect()
      const alvo = document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2)
      return { aba: t.textContent.replace(/\d+/g, '').trim(), responde: Boolean(alvo && t.contains(alvo)) }
    }),
  }
})

async function rodar(comFaixa) {
  const contexto = await navegador.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true })
  // Sem a faixa: notificação concedida e, se ainda houver aviso (som bloqueado), Dispensar logo abaixo.
  if (!comFaixa) await contexto.grantPermissions(['notifications'])
  const p = await contexto.newPage()
  await p.goto(`${origem}/#/m/atendimento/balcao`, { timeout: 90000 })
  await p.locator('[role=tab]', { hasText: 'Mensagens' }).waitFor({ timeout: 30000 })
  await p.waitForTimeout(700)
  if (!comFaixa) {
    const dispensar = p.getByRole('button', { name: 'Dispensar avisos do aparelho' })
    if (await dispensar.count()) { await dispensar.click(); await p.waitForTimeout(300) }
  }
  for (const aba of ['Conversas', 'Mensagens']) {
    if (aba === 'Mensagens') { await p.locator('[role=tab]', { hasText: 'Mensagens' }).click(); await p.waitForTimeout(600) }
    const m = await medir(p)
    cobrar(m.faixa === comFaixa, `${comFaixa ? 'com' : 'sem'} a faixa do aparelho (aba ${aba}): faixa à vista = ${m.faixa}`)
    for (const { aba: nome, responde } of m.abas) cobrar(responde, `${comFaixa ? 'com' : 'sem'} faixa, aba ${aba} ativa: o centro de "${nome}" responde à própria aba`)
  }
  // Painel aberto, com o passo a passo da faixa aberto (o pé mais alto): o topo do painel e o Fechar ficam na tela.
  if (comFaixa) {
    const passo = p.locator('button[aria-controls], button[aria-expanded]').filter({ hasText: /bloquead|Som|Avisos/i }).first()
    if (await passo.count()) { await passo.click(); await p.waitForTimeout(300) }
    await p.getByRole('button', { name: 'Abrir assistente' }).click()
    await p.waitForTimeout(600)
    const topo = await p.evaluate(() => {
      const fechar = [...document.querySelectorAll('button')].find((b) => /Fechar/.test(b.getAttribute('aria-label') ?? '') && b.closest('[role=dialog],section,aside'))
      return fechar ? Math.round(fechar.getBoundingClientRect().top) : null
    })
    cobrar(topo !== null && topo >= 0, `com faixa e passo a passo aberto: o Fechar do painel fica na tela (topo=${topo})`)
  }
  await contexto.close()
}

try {
  await rodar(true)
  await rodar(false)
} finally {
  await navegador.close()
  await servidor.close()
}
if (falhas.length) { console.log(`\n${falhas.length} falha(s)`); process.exit(1) }
console.log('\nprova-3416 verde')
