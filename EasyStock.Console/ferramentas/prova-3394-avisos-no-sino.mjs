// #3394: os Avisos saem do Modal e abrem num painel junto ao sino, no jeito das redes (WhatsApp, Instagram,
// Facebook): abas Não lidas e Todas, grupos Hoje, Ontem e Anteriores, ponto de não lida, leitura na hora com
// volta quando a API falha, Marcar todas com confirmação e folha no celular. Console real com API controlada.
// PLAYWRIGHT_MODULE aponta uma instalação existente; CONSOLE_URL aponta o Vite em modo API. Não usa dado da loja.
import assert from 'node:assert/strict'
import { createRequire, registerHooks } from 'node:module'
import { mkdirSync } from 'node:fs'
const require = createRequire(import.meta.url)
registerHooks({ resolve(spec, contexto, proximo) {
  if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
    try { return proximo(spec + '.js', contexto) } catch { /* resolução normal */ }
  }
  return proximo(spec, contexto)
} })
const { MODULOS } = await import('../src/dominio/modulos.js')
const { agruparAvisos, posicaoDoPainel } = await import('../src/dominio/avisosDaSessao.js')

// --- Domínio puro: grupos por dia e lugar do painel ---------------------------------------------------------
{
  const agora = new Date(2026, 10, 27, 15, 0).getTime()
  const em = (dia, hora) => new Date(2026, 10, dia, hora, 0).toISOString()
  const grupos = agruparAvisos([
    { id: 'a', createdAt: em(27, 14) }, { id: 'b', createdAt: em(27, 0) }, { id: 'c', createdAt: em(26, 23) },
    { id: 'd', createdAt: em(20, 9) }, { id: 'e', createdAt: null },
  ], agora)
  assert.deepEqual(grupos.map((g) => [g.titulo, g.avisos.map((a) => a.id)]), [['Hoje', ['a', 'b']], ['Ontem', ['c']], ['Anteriores', ['d', 'e']]])
  assert.deepEqual(agruparAvisos([], agora), [], 'sem aviso não há grupo vazio')
  // Topo largo: abre embaixo do sino, alinhado pela direita dele.
  const topo = posicaoDoPainel({ ancora: { top: 10, bottom: 54, left: 900, right: 990 }, janela: { largura: 1280, altura: 800 }, alturaDoPainel: 500 })
  assert.equal(topo.lado, 'abaixo')
  assert.equal(topo.top, 62)
  assert.equal(topo.left, 990 - 400, 'a borda direita do painel encosta na do sino')
  // Topo do tablet: o sino fica no começo da barra; o painel não pode nascer fora da tela.
  const tablet = posicaoDoPainel({ ancora: { top: 70, bottom: 110, left: 205, right: 355 }, janela: { largura: 1100, altura: 800 }, alturaDoPainel: 500 })
  assert.equal(tablet.lado, 'abaixo')
  assert.equal(tablet.left, 12, 'sem espaço à esquerda, o painel encosta na margem')
  // Trilho à esquerda: abre ao lado, sem passar do pé da janela.
  const trilho = posicaoDoPainel({ ancora: { top: 600, bottom: 660, left: 0, right: 96 }, janela: { largura: 1440, altura: 900 }, alturaDoPainel: 500 })
  assert.equal(trilho.lado, 'direita')
  assert.equal(trilho.left, 104)
  assert.ok(trilho.top + 500 <= 900 - 12, `o painel do trilho passa do pé: top ${trilho.top}`)
  assert.ok(trilho.top + trilho.maxHeight <= 900 - 12, `a altura máxima deixa o painel passar do pé: ${trilho.top} + ${trilho.maxHeight}`)
  // Celular: folha na largura da tela, abaixo do sino.
  const celular = posicaoDoPainel({ ancora: { top: 8, bottom: 52, left: 300, right: 344 }, janela: { largura: 390, altura: 844 }, alturaDoPainel: 900 })
  assert.equal(celular.lado, 'folha')
  assert.deepEqual([celular.left, celular.right], [8, 8])
  assert.ok(celular.maxHeight <= 844 - celular.top - 8)
  console.log('ok domínio: grupos Hoje/Ontem/Anteriores e lugar do painel (topo, trilho, celular)')
}

// --- Navegador ------------------------------------------------------------------------------------------------
if (!process.env.CONSOLE_URL) {
  console.log('--  prova de navegador pulada: defina CONSOLE_URL (o rodar-provas.mjs sobe o Vite) e PLAYWRIGHT_MODULE')
  process.exit(0)
}
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const origem = process.env.CONSOLE_URL
const evidencias = process.env.EVIDENCIAS_UX ?? 'builds/prova-3394'
mkdirSync(evidencias, { recursive: true })

const empresaId = '11111111-1111-4111-8111-111111111111'
const minutosAtras = (m) => new Date(Date.now() - m * 60000).toISOString()
const ontem = () => { const d = new Date(); d.setDate(d.getDate() - 1); d.setHours(10, 0, 0, 0); return d.toISOString() }
const AVISOS = [
  { id: 'a1', tipo: 'EstoqueBaixo', titulo: 'Estoque baixo: Lasanha da casa', mensagem: 'Restam 2 porções. Programe a produção de amanhã.', severidade: 'Critica', createdAt: minutosAtras(3) },
  { id: 'a2', tipo: 'Validade', titulo: 'Validade perto: molho branco', mensagem: 'O lote 14 vence em 2 dias.', severidade: 'Alta', createdAt: minutosAtras(50) },
  { id: 'a3', tipo: 'Sistema', titulo: 'Pagamento Pix conferido', mensagem: 'O pedido 0042 teve o Pix confirmado pelo banco.', severidade: 'Media', createdAt: ontem() },
  { id: 'a4', tipo: 'Sistema', titulo: 'Novidade no cardápio do site', mensagem: 'As fotos novas já aparecem para os clientes.', severidade: 'Informativa', createdAt: minutosAtras(60 * 24 * 5) },
]
const LIDOS = [
  { id: 'l1', tipo: 'Sistema', titulo: 'Caixa fechado ontem', mensagem: 'O fechamento bateu com o esperado.', severidade: 'Informativa', createdAt: minutosAtras(60 * 26), lida: true },
]
const estado = { naoLidas: AVISOS.map((a) => ({ ...a, lida: false })), lidas: [...LIDOS], falharLeituraDe: null, atrasoMs: 0 }
const escritas = []
const erros = []

const browser = await chromium.launch({ headless: true })
let ultimaPagina = null
const novaPagina = async ({ width = 1440, height = 900, escuro = false } = {}) => {
  const context = await browser.newContext({ viewport: { width, height }, reducedMotion: 'reduce', colorScheme: escuro ? 'dark' : 'light' })
  await context.grantPermissions(['local-network-access'], { origin: origem }).catch(() => {})
  await context.addInitScript(({ empresaId }) => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'teste-ui-controlado', expiraEm: Date.now() + 3600000, empresa: { id: empresaId, nome: 'Empresa de teste' }, usuario: { id: '22222222-2222-4222-8222-222222222222', nome: 'Dona de teste' } })), { empresaId })
  await context.route('**/api/**', async (route) => {
    const request = route.request(), url = new URL(request.url()), caminho = url.pathname, metodo = request.method()
    if (!caminho.startsWith('/api/')) return route.continue()
    const ok = (data, meta = {}) => route.fulfill({ json: { data, meta } })
    const leitura = caminho.match(/^\/api\/notificacoes\/([^/]+)\/lida$/)
    if (leitura && metodo === 'PATCH') {
      escritas.push(`PATCH ${leitura[1]}`)
      if (estado.atrasoMs) await new Promise((r) => setTimeout(r, estado.atrasoMs))
      if (estado.falharLeituraDe === leitura[1]) return route.fulfill({ status: 500, json: { success: false, error: { message: 'Falha simulada' } } })
      const i = estado.naoLidas.findIndex((a) => a.id === leitura[1])
      if (i >= 0) estado.lidas.unshift({ ...estado.naoLidas.splice(i, 1)[0], lida: true })
      return route.fulfill({ status: 204, body: '' })
    }
    if (caminho === '/api/notificacoes/marcar-todas' && metodo === 'POST') {
      escritas.push('POST marcar-todas')
      estado.lidas.unshift(...estado.naoLidas.map((a) => ({ ...a, lida: true })))
      estado.naoLidas = []
      return route.fulfill({ status: 204, body: '' })
    }
    if (metodo !== 'GET') return ok({})
    if (caminho === '/api/auth/me/modulos') return ok({ modulos: MODULOS.map(({ id }) => ({ id, liberado: true })), acoes: {} })
    if (caminho === '/api/notificacoes/badge') return ok({ count: estado.naoLidas.length })
    if (caminho === '/api/notificacoes/recentes') return ok(estado.naoLidas.slice(0, 10))
    if (caminho === '/api/notificacoes') {
      const todas = [...estado.naoLidas, ...estado.lidas].sort((a, b) => b.createdAt.localeCompare(a.createdAt))
      return ok(todas.slice(0, 20), { total: todas.length, pages: 1, page: 1, limit: 20 })
    }
    if (caminho === '/api/atendimento/agenda') return ok({ lojaDisponivel: false, dias: [] })
    if (caminho === '/api/atendimento/configuracao') return ok({})
    return ok([])
  })
  const page = await context.newPage()
  ultimaPagina = page
  page.on('pageerror', (erro) => erros.push(erro.message))
  page.on('console', (m) => { if (m.type() === 'error' && !/Failed to load resource/.test(m.text())) erros.push(m.text()) })
  return { context, page }
}
const assentar = (page) => page.evaluate(async () => {
  await document.fonts.ready
  await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))
})
const sinoVisivel = (page) => page.getByRole('button', { name: /^Avisos/ }).locator('visible=true').first()
const painel = (page) => page.locator('[data-painel-avisos]')

try {
  // 1. Desktop: o painel abre junto ao sino, sem modal nem fundo escuro.
  const { context, page } = await novaPagina()
  await page.goto(origem + '/#/m/atendimento/balcao')
  const sino = sinoVisivel(page)
  await sino.waitFor()
  await assert.doesNotReject(page.waitForFunction(() => [...document.querySelectorAll('button')].some((b) => /Avisos, 4 não lidas/.test(b.getAttribute('aria-label') ?? '')), null, { timeout: 8000 }), 'o sino conta 4 não lidas')
  await sino.click()
  await painel(page).waitFor({ state: 'visible', timeout: 4000 })
  assert.equal(await page.locator('[aria-modal="true"]').count(), 0, 'abrir os Avisos não pode criar diálogo modal')
  const caixaSino = await sino.boundingBox(), caixaPainel = await painel(page).boundingBox()
  const distancia = Math.min(Math.abs(caixaPainel.y - (caixaSino.y + caixaSino.height)), Math.abs(caixaPainel.x - (caixaSino.x + caixaSino.width)))
  assert.ok(distancia <= 16, `o painel fica longe do sino: ${distancia}px`)
  for (const grupo of ['Hoje', 'Ontem', 'Anteriores']) await painel(page).getByRole('heading', { name: grupo, exact: true }).waitFor()
  assert.equal(await painel(page).locator('[data-nao-lida]').count(), 4, 'quatro pontos de não lida')
  await assentar(page)
  await page.screenshot({ path: `${evidencias}/desktop-claro.png` })
  console.log('ok 1: painel junto ao sino, sem modal, grupos Hoje/Ontem/Anteriores e 4 não lidas')

  // 2. Esc fecha e devolve o foco ao sino; clique fora também fecha.
  await page.keyboard.press('Escape')
  await painel(page).waitFor({ state: 'detached', timeout: 3000 })
  assert.match(await page.evaluate(() => document.activeElement?.getAttribute('aria-label') ?? ''), /^Avisos/, 'o foco volta ao sino')
  await sino.click()
  await painel(page).waitFor({ state: 'visible' })
  await page.mouse.click(720, 880)
  await painel(page).waitFor({ state: 'detached', timeout: 3000 })
  console.log('ok 2: Esc e clique fora fecham, foco volta ao sino')

  // 3. Ler um aviso tira da lista na hora (a API demora) e o contador cai.
  estado.atrasoMs = 1500
  await sino.click()
  await painel(page).waitFor({ state: 'visible' })
  await painel(page).getByRole('button', { name: 'Marcar como lido: Estoque baixo: Lasanha da casa', exact: true }).click()
  await painel(page).getByText('Estoque baixo: Lasanha da casa').waitFor({ state: 'detached', timeout: 500 })
  await page.waitForFunction(() => [...document.querySelectorAll('button')].some((b) => /Avisos, 3 não lidas/.test(b.getAttribute('aria-label') ?? '')), null, { timeout: 500 })
  estado.atrasoMs = 0
  await page.waitForTimeout(1700)
  assert.ok(escritas.includes('PATCH a1'))
  console.log('ok 3: leitura na hora, antes da API responder, e contador em 3')

  // 4. Falha da API devolve o aviso e diz o que houve.
  estado.falharLeituraDe = 'a2'
  await painel(page).getByRole('button', { name: 'Marcar como lido: Validade perto: molho branco', exact: true }).click()
  await painel(page).getByRole('alert').waitFor({ timeout: 4000 })
  await painel(page).getByRole('heading', { name: 'Validade perto: molho branco', exact: true }).waitFor()
  estado.falharLeituraDe = null
  console.log('ok 4: falha da API devolve o aviso e avisa')

  // 5. Aba Todas mostra o lido sem ponto.
  await painel(page).getByRole('tab', { name: /^Todas/ }).click()
  const lido = painel(page).locator('li', { hasText: 'Caixa fechado ontem' })
  await lido.waitFor()
  assert.equal(await lido.locator('[data-nao-lida]').count(), 0, 'lido não leva ponto')
  await painel(page).getByRole('tab', { name: /^Não lidas/ }).click()
  console.log('ok 5: aba Todas mostra lidas sem ponto')

  // 6. Marcar todas pede confirmação (vale para a equipe) e grava.
  await painel(page).getByRole('button', { name: 'Marcar todas como lidas', exact: true }).click()
  const confirma = painel(page).getByRole('button', { name: 'Sim, marcar todas', exact: true })
  await confirma.waitFor()
  assert.match(await painel(page).innerText(), /equipe/i, 'a confirmação diz que vale para a equipe')
  await confirma.click()
  await painel(page).getByText('Tudo em dia', { exact: false }).waitFor({ timeout: 3000 })
  assert.ok(escritas.includes('POST marcar-todas'))
  await assentar(page)
  await page.screenshot({ path: `${evidencias}/desktop-vazio.png` })
  console.log('ok 6: Marcar todas confirma, grava e mostra Tudo em dia')
  await context.close()

  // 7a. Tablet 1100 px: o sino mora no topo; o painel fica inteiro na tela e junto dele.
  const tablet = await novaPagina({ width: 1100, height: 800 })
  await tablet.page.goto(origem + '/#/m/atendimento/balcao')
  const sinoTab = sinoVisivel(tablet.page)
  await sinoTab.waitFor()
  await sinoTab.click()
  await painel(tablet.page).waitFor({ state: 'visible' })
  const [caixaTab, caixaSinoTab] = [await painel(tablet.page).boundingBox(), await sinoTab.boundingBox()]
  assert.ok(caixaTab.x >= 0 && caixaTab.x + caixaTab.width <= 1100, `o painel do tablet sai da tela: ${JSON.stringify(caixaTab)}`)
  assert.ok(Math.abs(caixaTab.y - (caixaSinoTab.y + caixaSinoTab.height)) <= 16, 'o painel do tablet nasce embaixo do sino')
  await assentar(tablet.page)
  await tablet.page.screenshot({ path: `${evidencias}/tablet-claro.png` })
  console.log('ok 7a: tablet 1100 px, painel inteiro embaixo do sino')
  await tablet.context.close()

  // 7. Celular 390 px: folha na largura da tela, sem rolagem lateral; e escuro.
  estado.naoLidas = AVISOS.map((a) => ({ ...a, lida: false }))
  const celular = await novaPagina({ width: 390, height: 844, escuro: true })
  await celular.page.goto(origem + '/#/m/atendimento/balcao')
  const sinoCel = sinoVisivel(celular.page)
  await sinoCel.waitFor()
  await celular.page.waitForFunction(() => [...document.querySelectorAll('button')].some((b) => /Avisos, 4 não lidas/.test(b.getAttribute('aria-label') ?? '')), null, { timeout: 8000 })
  await sinoCel.click()
  await painel(celular.page).waitFor({ state: 'visible' })
  const caixa = await painel(celular.page).boundingBox()
  assert.ok(caixa.x >= 0 && caixa.x + caixa.width <= 390, `a folha sai da tela: ${JSON.stringify(caixa)}`)
  assert.ok(await celular.page.evaluate(() => document.documentElement.scrollWidth <= 390), 'rolagem lateral no celular')
  await assentar(celular.page)
  await celular.page.screenshot({ path: `${evidencias}/celular-escuro.png` })
  console.log('ok 7: celular 390 px sem rolagem lateral, escuro')
  await celular.context.close()

  assert.deepEqual(erros, [], 'erros no console')
  console.log('prova-3394-avisos-no-sino: ok')
} catch (erro) {
  // Falhou: a foto da tela no momento vai junto, para quem lê o CI não ter de adivinhar.
  if (erros.length > 0) console.log("erros da página:", erros)
  await ultimaPagina?.screenshot({ path: `${evidencias}/falha.png` }).catch(() => {})
  throw erro
} finally {
  await browser.close()
}
