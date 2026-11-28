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
const { agruparAvisos, posicaoDoPainel, destinoDoAviso } = await import('../src/dominio/avisosDaSessao.js')
const { rotaDaHash } = await import('../src/dominio/rota.js')

// --- Domínio puro: para onde o toque no aviso leva (#3402) --------------------------------------------------
{
  assert.equal(typeof destinoDoAviso, 'function', 'falta destinoDoAviso em dominio/avisosDaSessao.js (#3402)')
  const tudo = { permite: () => true, acoes: {} }
  const ESTOQUE = { hash: '#/m/producao/estoque', tela: 'Estoque do dia', chamada: 'Ver no Estoque do dia' }
  const PERDAS = { hash: '#/m/producao/perdas', tela: 'Perdas', chamada: 'Ver em Perdas' }
  // Os nomes do enum TipoAlertaEstoque e os três apelidos (EstoqueBaixo, ProximoVencimento, Reposicao): o banco guarda
  // o texto e Enum.ToString não garante qual dos dois nomes sai.
  for (const tipo of ['EstoqueCritico', 'EstoqueBaixo', 'ReposicaoSugerida', 'Reposicao', 'ValidadeProxima', 'ProximoVencimento', 'ProdutoParado']) {
    assert.deepEqual(destinoDoAviso({ tipo, referenciaId: 'r' }, tudo), ESTOQUE, `${tipo} leva ao Estoque do dia`)
  }
  assert.deepEqual(destinoDoAviso({ tipo: 'ProdutoVencido', referenciaId: 'r' }, tudo), PERDAS, 'o lote vencido leva a Perdas')
  for (const tipo of ['PedidoAtrasado', 'PedidoRecebido', 'AvisoOperacional', 'Sistema', 'constructor', '', undefined]) {
    assert.equal(destinoDoAviso({ tipo }, tudo), null, `${tipo}: sem tela no Console, sem destino`)
  }
  assert.equal(destinoDoAviso(null, tudo), null, 'sem aviso, sem destino')
  assert.equal(destinoDoAviso({ tipo: 'EstoqueCritico' }, { permite: (id) => id !== 'producao', acoes: {} }), null, 'perfil sem Produção não ganha link')
  assert.equal(destinoDoAviso({ tipo: 'EstoqueCritico' }, { permite: () => true, acoes: { verDesacertosEstoque: false } }), null, 'perfil sem estoque não ganha link')
  assert.equal(destinoDoAviso({ tipo: 'EstoqueCritico' }, {}), null, 'sem a regra do perfil, sem link')
  for (const destino of [ESTOQUE, PERDAS]) {
    const rota = rotaDaHash(destino.hash, { fonteApi: true })
    assert.deepEqual([rota.tipo, rota.modulo, rota.tela, rota.aviso], ['modulo', 'producao', destino.hash.split('/').pop(), undefined], `${destino.hash} tem de ser rota que já existe`)
  }
  console.log('ok domínio #3402: estoque e apelidos levam ao Estoque do dia, vencido a Perdas; pedido e aviso operacional sem destino; perfil sem estoque sem link')
}

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
  // Banca: o teto de 640 px do painel vale também com o JS medindo (o estilo inline vencia o CSS).
  assert.ok(topo.maxHeight <= 640, `painel do topo sem teto: ${topo.maxHeight}`)
  const alto = posicaoDoPainel({ ancora: { top: 600, bottom: 660, left: 0, right: 96 }, janela: { largura: 1920, altura: 1200 }, alturaDoPainel: 900 })
  assert.ok(alto.maxHeight <= 640 && alto.top + 640 <= 1200 - 12, `painel do trilho sem teto: ${JSON.stringify(alto)}`)
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
// Mais antigos que os quatro de cima: com eles passam de 10 não lidas, mais que o /recentes devolve.
const ANTIGOS = Array.from({ length: 8 }, (_, i) => ({ id: `x${i + 1}`, tipo: 'Sistema', titulo: `Aviso antigo ${i + 1}`, mensagem: 'Sem pressa.', severidade: 'Informativa', createdAt: minutosAtras(60 * 24 * (6 + i)) }))
// #3402: os tipos e as referências que o backend grava de fato (GeradorNotificacoesAutomaticas, DevicePairingController).
const LOTE = '55555555-5555-4555-8555-555555555555'
const DE_VERDADE = [
  { id: 'e1', tipo: 'EstoqueCritico', titulo: 'Estoque Crítico', mensagem: 'Lasanha da casa: abaixo do mínimo. Considere repor este item.', severidade: 'Alta', referenciaId: '33333333-3333-4333-8333-333333333333', createdAt: minutosAtras(2) },
  { id: 'e2', tipo: 'ValidadeProxima', titulo: 'Validade Próxima', mensagem: 'L-0014 vence amanhã. Estoque atual: 3 un. Priorize a venda ou rotatividade.', severidade: 'Alta', referenciaId: LOTE, createdAt: minutosAtras(4) },
  { id: 'e3', tipo: 'ProdutoVencido', titulo: 'Produto Vencido', mensagem: 'L-0009 venceu há 1 dia(s). Retire do estoque ou descarte conforme procedimento.', severidade: 'Critica', referenciaId: LOTE, createdAt: minutosAtras(6) },
  { id: 'p1', tipo: 'PedidoAtrasado', titulo: 'Pedido Atrasado', mensagem: 'Pedido com previsão em 25/11/2026 ainda não foi recebido. Entre em contato com o fornecedor.', severidade: 'Alta', referenciaId: '44444444-4444-4444-8444-444444444444', createdAt: minutosAtras(8) },
  { id: 'o1', tipo: 'AvisoOperacional', titulo: 'Aparelho reconectado', mensagem: 'A chave do aparelho Caixa 1 foi trocada pelo pareamento automático.', severidade: 'Alta', createdAt: minutosAtras(10) },
]
const estado = { naoLidas: AVISOS.map((a) => ({ ...a, lida: false })), lidas: [...LIDOS], falharLeituraDe: null, atrasoMs: 0, falharMarcarTodas: false, atrasoTodasMs: 0, acoes: {} }
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
      if (estado.falharMarcarTodas) return route.fulfill({ status: 500, json: { success: false, error: { message: 'Falha simulada' } } })
      estado.lidas.unshift(...estado.naoLidas.map((a) => ({ ...a, lida: true })))
      estado.naoLidas = []
      return route.fulfill({ status: 204, body: '' })
    }
    if (metodo !== 'GET') return ok({})
    if (caminho === '/api/auth/me/modulos') return ok({ modulos: MODULOS.map(({ id }) => ({ id, liberado: true })), acoes: estado.acoes })
    if (caminho === '/api/atendimento/producao/estoque-do-dia') return ok({ alertas: [], pratos: [] })
    if (caminho === '/api/notificacoes/badge') return ok({ count: estado.naoLidas.length })
    if (caminho === '/api/notificacoes/recentes') return ok(estado.naoLidas.slice(0, 10))
    if (caminho === '/api/notificacoes') {
      const todas = [...estado.naoLidas, ...estado.lidas].sort((a, b) => b.createdAt.localeCompare(a.createdAt))
      // A resposta se monta agora e só sai depois: é a resposta velha que chega depois de uma leitura.
      if (estado.atrasoTodasMs) await new Promise((r) => setTimeout(r, estado.atrasoTodasMs))
      const pg = Number(url.searchParams.get('page') ?? 1), tam = Number(url.searchParams.get('pageSize') ?? 20)
      return ok(todas.slice((pg - 1) * tam, pg * tam), { total: todas.length, pages: Math.ceil(todas.length / tam), page: pg, limit: tam })
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
// A escrita chega à rota simulada num instante incerto: espera por ela até 3 s, sem dormir um tempo fixo.
const gravou = async (escrita, prazo = 3000) => {
  for (const fim = Date.now() + prazo; !escritas.includes(escrita); await new Promise((r) => setTimeout(r, 50))) {
    if (Date.now() > fim) return false
  }
  return true
}
const esperarContador = (page, n) => page.waitForFunction((n) => [...document.querySelectorAll('button')]
  .some((b) => (b.getAttribute('aria-label') ?? '').includes(`Avisos, ${n} não lidas`)), n, { timeout: 8000 })

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
  // O botão clicado some: o foco vai para a saída segura (Cancelar) e, cancelando, volta para Marcar todas.
  const focado = () => page.evaluate(() => document.activeElement?.textContent?.trim() ?? '')
  assert.equal(await focado(), 'Cancelar', 'ao pedir a confirmação, o foco vai para Cancelar')
  await painel(page).getByRole('button', { name: 'Cancelar', exact: true }).click()
  assert.equal(await focado(), 'Marcar todas como lidas', 'ao cancelar, o foco volta para Marcar todas')
  await painel(page).getByRole('button', { name: 'Marcar todas como lidas', exact: true }).click()
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

  // 8. Achados da banca: mais de 10 não lidas, aba Todas lenta e falha do Marcar todas.
  estado.naoLidas = [...AVISOS, ...ANTIGOS].map((a) => ({ ...a, lida: false }))
  estado.lidas = [...LIDOS]
  const banca = await novaPagina()
  const pagina = banca.page
  await pagina.goto(origem + '/#/m/atendimento/balcao')
  const sinoBanca = sinoVisivel(pagina)
  await sinoBanca.waitFor()
  await pagina.waitForFunction(() => [...document.querySelectorAll('button')].some((b) => /Avisos, 12 não lidas/.test(b.getAttribute('aria-label') ?? '')), null, { timeout: 8000 })
  await pagina.evaluate(() => {
    window.__balancou = false
    new MutationObserver(() => {
      if ([...document.querySelectorAll('button[aria-haspopup="dialog"]')].some((b) => /balanca/.test(b.className))) window.__balancou = true
    }).observe(document.body, { subtree: true, attributes: true, attributeFilter: ['class'] })
  })
  await sinoBanca.click()
  await painel(pagina).waitFor({ state: 'visible' })
  const abaTodas = painel(pagina).getByRole('tab', { name: /^Todas/ })
  const abaNaoLidas = painel(pagina).getByRole('tab', { name: /^Não lidas/ })
  await abaTodas.click()
  await painel(pagina).locator('[data-aviso="x8"]').waitFor()
  await abaNaoLidas.click()
  const pontos = () => painel(pagina).locator('[data-nao-lida]').count()

  // 8a. Marcar todas falha: as 12 voltam, inclusive as 2 que não cabem nos 10 recentes; o sino não balança.
  estado.falharMarcarTodas = true
  await painel(pagina).getByRole('button', { name: 'Marcar todas como lidas', exact: true }).click()
  await painel(pagina).getByRole('button', { name: 'Sim, marcar todas', exact: true }).click()
  await painel(pagina).getByRole('alert').waitFor({ timeout: 4000 })
  estado.falharMarcarTodas = false
  estado.atrasoTodasMs = 1500
  await abaTodas.click()
  assert.equal(await pontos(), 12, 'na volta do Marcar todas, a aba Todas tem de mostrar as 12 não lidas de novo')
  await pagina.waitForTimeout(1700)
  assert.equal(await pagina.evaluate(() => window.__balancou), false, 'o sino não balança quando a leitura falha e os avisos voltam')
  console.log('ok 8a: Marcar todas que falha devolve as 12, sem balançar o sino')

  // 8b. Ler na aba Todas enquanto ela recarrega: a resposta velha não traz o aviso lido de volta.
  await abaNaoLidas.click()
  await abaTodas.click()
  await painel(pagina).getByRole('button', { name: 'Marcar como lido: Estoque baixo: Lasanha da casa', exact: true }).click()
  await pagina.waitForTimeout(1800)
  assert.ok(escritas.includes('PATCH a1'))
  assert.equal(await painel(pagina).locator('[data-aviso="a1"] [data-nao-lida]').count(), 0, 'a resposta velha da aba Todas trouxe o aviso lido de volta')
  estado.atrasoTodasMs = 0
  console.log('ok 8b: resposta velha da aba Todas não desfaz a leitura')

  // 8e. Atualizar a aba Todas com uma leitura ainda pendente: a resposta, que saiu antes da gravação, não traz
  // o aviso de volta nem agora nem depois que a leitura termina.
  estado.atrasoMs = 1500
  const semPontoEm = async (id) => (await painel(pagina).locator(`[data-aviso="${id}"] [data-nao-lida]`).count()) === 0
  await painel(pagina).getByRole('button', { name: 'Marcar como lido: Validade perto: molho branco', exact: true }).click()
  await painel(pagina).getByRole('button', { name: 'Atualizar avisos', exact: true }).click()
  await pagina.waitForTimeout(400)
  assert.ok(await semPontoEm('a2'), 'a aba Todas pedida com leitura pendente trouxe o aviso lido de volta')
  await pagina.waitForTimeout(1800)
  assert.ok(await semPontoEm('a2'), 'depois da leitura, a aba Todas ficou com o aviso lido como não lido')
  estado.atrasoMs = 0
  console.log('ok 8e: aba Todas pedida durante a leitura não desfaz a leitura')

  // 8c. O 11º que sobe para os recentes depois da leitura é antigo: não entra realçado como se tivesse chegado.
  await abaNaoLidas.click()
  await painel(pagina).locator('[data-aviso="x7"]').waitFor()
  assert.equal(await painel(pagina).locator('[class*="chegou"]').count(), 0, 'aviso antigo realçado como novo')
  // 8d. Aviso novo de fato: entra realçado e o sino balança.
  estado.naoLidas.unshift({ id: 'n1', tipo: 'Sistema', titulo: 'Pedido novo no site', mensagem: 'O pedido 0043 chegou.', severidade: 'Media', createdAt: new Date().toISOString(), lida: false })
  await painel(pagina).getByRole('button', { name: 'Atualizar avisos', exact: true }).click()
  await painel(pagina).locator('[data-aviso="n1"][class*="chegou"]').waitFor({ timeout: 3000 })
  await pagina.waitForFunction(() => window.__balancou === true, null, { timeout: 3000 })
  console.log('ok 8c/8d: só o aviso novo de fato realça e balança o sino')

  // 8g. Atualizar pelo teclado: o botão fica desligado enquanto carrega, e o foco volta para ele depois.
  const atualizar = painel(pagina).getByRole('button', { name: 'Atualizar avisos', exact: true })
  await atualizar.focus()
  await pagina.keyboard.press('Enter')
  await pagina.waitForFunction(() => document.activeElement?.getAttribute('aria-label') === 'Atualizar avisos', null, { timeout: 2000 })
    .catch(async () => assert.fail(`depois de Atualizar, o foco foi para ${await pagina.evaluate(() => document.activeElement?.tagName)}`))
  console.log('ok 8g: foco volta para Atualizar depois da carga')

  // 8h. Ver mais da última página: o botão some, e o foco fica dentro do painel (senão sair com Tab não fecha).
  for (let i = 1; i <= 12; i++) estado.lidas.push({ id: `v${i}`, tipo: 'Sistema', titulo: `Aviso velho ${i}`, mensagem: 'Lido.', severidade: 'Informativa', createdAt: minutosAtras(60 * 24 * (30 + i)), lida: true })
  await abaNaoLidas.click()
  await abaTodas.click()
  const verMais = painel(pagina).getByRole('button', { name: /^Ver mais/ })
  await verMais.focus()
  await pagina.keyboard.press('Enter')
  await pagina.waitForFunction(() => document.activeElement?.matches('[data-painel-avisos] [role="tab"][aria-selected="true"]'), null, { timeout: 3000 })
    .catch(async () => assert.fail(`depois do último Ver mais, o foco foi para ${await pagina.evaluate(() => document.activeElement?.tagName)}, não para a aba ativa`))
  console.log('ok 8h: último Ver mais deixa o foco no painel')
  await abaNaoLidas.click()

  // 8f. A consulta sozinha (a de 30 s, aqui pela volta à aba do navegador) não tira o foco de quem está no sino.
  await sinoBanca.focus()
  estado.naoLidas.unshift({ id: 'n2', tipo: 'Sistema', titulo: 'Mesa 4 pediu a conta', mensagem: 'Pedido 0044.', severidade: 'Media', createdAt: new Date().toISOString(), lida: false })
  await pagina.evaluate(() => document.dispatchEvent(new Event('visibilitychange')))
  await painel(pagina).locator('[data-aviso="n2"]').waitFor({ timeout: 3000 })
  await pagina.waitForTimeout(100)
  assert.match(await pagina.evaluate(() => document.activeElement?.getAttribute('aria-label') ?? ''), /^Avisos/, 'a consulta automática puxou o foco do sino para o painel')
  console.log('ok 8f: consulta automática não rouba o foco')
  await banca.context.close()

  // 9. #3402: tocar no aviso leva à tela da coisa avisada, como nas redes. Estoque abre o Estoque do dia, lote vencido
  //    abre Perdas; pedido ao fornecedor e aviso operacional, sem tela no Console, ficam só com a leitura.
  estado.naoLidas = DE_VERDADE.map((a) => ({ ...a, lida: false }))
  estado.lidas = []
  const destinos = await novaPagina()
  const pd = destinos.page
  await pd.goto(origem + '/#/m/atendimento/balcao')
  const sinoD = sinoVisivel(pd)
  await sinoD.waitFor()
  await esperarContador(pd, 5)
  await sinoD.click()
  await painel(pd).waitFor({ state: 'visible' })
  const linha = (id) => painel(pd).locator(`[data-aviso="${id}"]`)
  await linha('o1').waitFor()
  for (const id of ['p1', 'o1']) assert.equal(await linha(id).getByRole('link').count(), 0, `${id}: aviso sem tela no Console não vira link`)
  const linkCritico = linha('e1').getByRole('link', { name: 'Estoque Crítico', exact: true })
  await linkCritico.waitFor({ timeout: 2000 }).catch(() => assert.fail('o aviso de estoque não virou link para a tela dele'))
  assert.equal(await linkCritico.getAttribute('href'), '#/m/producao/estoque')
  assert.match(await linha('e1').innerText(), /Ver no Estoque do dia/, 'o aviso diz para onde leva')
  assert.equal(await linha('e3').getByRole('link', { name: 'Produto Vencido', exact: true }).getAttribute('href'), '#/m/producao/perdas', 'o lote vencido abre Perdas')
  assert.match(await linha('e3').innerText(), /Ver em Perdas/)
  await assentar(pd)
  await pd.screenshot({ path: `${evidencias}/destinos-painel.png` })

  // 9a. A linha toda leva (toque no selo, fora do título): lê, fecha e abre a tela, com o foco nela.
  const caixaE1 = await linha('e1').boundingBox()
  await pd.mouse.click(caixaE1.x + 24, caixaE1.y + caixaE1.height / 2)
  await painel(pd).waitFor({ state: 'detached', timeout: 3000 }).catch(() => assert.fail('tocar no aviso não fechou o painel'))
  await pd.waitForFunction(() => location.hash === '#/m/producao/estoque', null, { timeout: 3000 })
  assert.ok(await gravou('PATCH e1'), 'tocar no aviso marca como lido')
  await esperarContador(pd, 4)
  await pd.waitForFunction(() => document.activeElement?.matches('main[data-inicio-tela][aria-label="Estoque do dia"]'), null, { timeout: 3000 })
    .catch(async () => assert.fail(`depois do toque, o foco foi para ${await pd.evaluate(() => document.activeElement?.outerHTML.slice(0, 80))}`))
  console.log('ok 9a: tocar na linha do aviso de estoque lê, fecha o painel e abre o Estoque do dia com o foco nele')

  // 9b. Já no Estoque do dia (mesmo hash, sem hashchange): o foco vai ao início da tela, não ao body. A leitura que falha
  //     depois do painel fechado devolve o aviso e o contador, e a mensagem aparece ao reabrir.
  estado.falharLeituraDe = 'e2'
  await sinoD.click()
  await painel(pd).waitFor({ state: 'visible' })
  await linha('e2').getByRole('link', { name: 'Validade Próxima', exact: true }).click()
  await painel(pd).waitFor({ state: 'detached', timeout: 3000 })
  await pd.waitForFunction(() => document.activeElement?.matches('[data-inicio-tela]'), null, { timeout: 3000 })
    .catch(async () => assert.fail(`no mesmo hash, o foco foi para ${await pd.evaluate(() => document.activeElement?.tagName)}`))
  assert.ok(await gravou('PATCH e2'), 'tocar no aviso no mesmo hash também lê')
  await esperarContador(pd, 4)
  await sinoD.click()
  await painel(pd).getByRole('alert').filter({ hasText: 'Validade Próxima' }).waitFor({ timeout: 3000 })
  await linha('e2').waitFor()
  estado.falharLeituraDe = null
  console.log('ok 9b: mesmo hash leva o foco à tela; leitura que falha com o painel fechado volta e avisa ao reabrir')

  // 9c. Ctrl+clique abre noutra aba: lê, mas o painel fica aberto, esta aba não troca de tela e o foco, com o link fora
  //     da lista, vai para o próximo não lido, não para o body.
  //     #3413: a espera da aba se arma antes do clique, para não perder o evento, mas o prazo só conta depois dele. Antes
  //     eram 3 s contados antes do clique, que também espera o link ficar clicável; num runner lento o prazo podia acabar
  //     com a aba a caminho (reproduzido com o link lento). Depois do clique a aba chega em menos de 1 s, mesmo com a CPU
  //     60 vezes mais lenta.
  const ESPERA_ABA_MS = 10000
  const abaNova = destinos.context.waitForEvent('page', { timeout: 0 }).catch(() => null)
  await linha('e3').getByRole('link', { name: 'Produto Vencido', exact: true }).click({ modifiers: ['Control'] })
  let prazoDaAba
  const outra = await Promise.race([abaNova, new Promise((resolve) => { prazoDaAba = setTimeout(() => resolve(null), ESPERA_ABA_MS) })])
  clearTimeout(prazoDaAba)
  assert.ok(outra, `o Ctrl+clique chegou ao link, mas nenhuma aba abriu em ${ESPERA_ABA_MS / 1000} s: veja se o abrirDestino do SinoDaSessao passou a chamar preventDefault com Ctrl`)
  await outra.close()
  assert.ok(await painel(pd).isVisible(), 'o Ctrl+clique fechou o painel desta aba')
  assert.equal(await pd.evaluate(() => location.hash), '#/m/producao/estoque', 'o Ctrl+clique trocou a tela desta aba')
  assert.ok(await gravou('PATCH e3'), 'o Ctrl+clique também lê o aviso')
  const ondeFoco = () => pd.evaluate(() => `${document.activeElement?.tagName} ${document.activeElement?.getAttribute('aria-label') ?? document.activeElement?.textContent ?? ''}`)
  await pd.waitForFunction(() => document.activeElement?.matches('[data-aviso="p1"] button'), null, { timeout: 2000 })
    .catch(async () => assert.fail(`depois do Ctrl+clique em Não lidas, o foco foi para ${await ondeFoco()}, não para o próximo não lido`))
  console.log('ok 9c: Ctrl+clique abre outra aba, lê sem fechar o painel nem trocar a tela, e o foco vai ao próximo não lido')

  // 9e. Na aba Todas o aviso lido continua na lista: Ctrl+Enter no link lê e o foco fica no mesmo link, sem pular de
  //     linha (o teclado não perde o lugar).
  estado.naoLidas.unshift({ ...DE_VERDADE[0], id: 'e4', titulo: 'Farinha acabando', lida: false, createdAt: new Date().toISOString() })
  await painel(pd).getByRole('tab', { name: /^Todas/ }).click()
  const linkE4 = linha('e4').getByRole('link', { name: 'Farinha acabando', exact: true })
  await linkE4.waitFor({ timeout: 3000 })
  const abaDoTeclado = destinos.context.waitForEvent('page', { timeout: 3000 }).catch(() => null)
  // Chega ao link pelo teclado (do botão de ler, Shift+Tab), como quem usa Tab, para o foco valer como do teclado.
  await linha('e4').getByRole('button', { name: /^Marcar como lido/ }).focus()
  await pd.keyboard.press('Shift+Tab')
  assert.ok(await linkE4.evaluate((el) => el === document.activeElement), `Shift+Tab do botão de ler foi para ${await ondeFoco()}, não para o link`)
  await pd.keyboard.press('Control+Enter')
  assert.ok(await gravou('PATCH e4'), 'o Ctrl+Enter na aba Todas também lê o aviso')
  await (await abaDoTeclado)?.close()
  await pd.waitForTimeout(300)
  assert.ok(await linkE4.isVisible(), 'na aba Todas o aviso lido tem de continuar na lista com o link')
  assert.ok(await linkE4.evaluate((el) => el === document.activeElement), `depois do Ctrl+Enter em Todas, o foco foi para ${await ondeFoco()}, não ficou no link`)
  // O link esticado não tem contorno próprio: quem mostra o foco do teclado é a linha inteira.
  assert.equal(await linha('e4').evaluate((el) => getComputedStyle(el).outlineStyle), 'solid', 'com o foco do teclado no link, a linha do aviso tem de ganhar o contorno')
  // Aviso já lido não grava de novo, e o Shift (outra janela) também deixa o painel aberto.
  const janela = destinos.context.waitForEvent('page', { timeout: 3000 }).catch(() => null)
  await linkE4.click({ modifiers: ['Shift'] })
  await (await janela)?.close()
  await pd.waitForTimeout(300)
  assert.equal(escritas.filter((e) => e === 'PATCH e4').length, 1, 'tocar com Shift num aviso já lido gravou a leitura de novo')
  assert.ok(await painel(pd).isVisible(), 'o Shift+clique fechou o painel desta aba')
  console.log('ok 9e: Ctrl+Enter na aba Todas lê e o foco fica no mesmo link; aviso lido não grava de novo')
  await destinos.context.close()

  // 9d. Perfil sem estoque (GerenciarEstoque, `verDesacertosEstoque`): nenhum aviso vira link; a leitura continua.
  estado.naoLidas = DE_VERDADE.map((a) => ({ ...a, lida: false }))
  estado.acoes = { verDesacertosEstoque: false }
  const semEstoque = await novaPagina()
  await semEstoque.page.goto(origem + '/#/m/atendimento/balcao')
  const sinoS = sinoVisivel(semEstoque.page)
  await sinoS.waitFor()
  await esperarContador(semEstoque.page, 5)
  await sinoS.click()
  await painel(semEstoque.page).locator('[data-aviso="o1"]').waitFor()
  assert.equal(await painel(semEstoque.page).getByRole('link').count(), 0, 'perfil sem estoque não pode ganhar link para tela que recusa')
  assert.equal(await painel(semEstoque.page).getByRole('button', { name: /^Marcar como lido/ }).count(), 5)
  estado.acoes = {}
  console.log('ok 9d: perfil sem estoque fica só com a leitura')
  await semEstoque.context.close()

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
