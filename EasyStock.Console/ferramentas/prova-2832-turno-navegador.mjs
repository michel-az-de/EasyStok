// #2832: o painel Agora da Gestão da loja no Console real, com API controlada. Abre a Gestão no Balcão, confere os
// quatro cartões (cardápio, caixa, pedidos, precisa de você) com os números da API, o Esgotou gravando na rota do
// cardápio, o monte de pedidos abrindo a tela de Pedidos, o "Você mais usa" e o desenho no claro, no escuro e a 390 px.
// PLAYWRIGHT_MODULE aponta uma instalação existente; CONSOLE_URL aponta o Vite em modo API. Não usa dado da loja.
import assert from 'node:assert/strict'
import { createRequire, registerHooks } from 'node:module'
import { mkdirSync } from 'node:fs'
import { join } from 'node:path'
const require = createRequire(import.meta.url)
registerHooks({ resolve(spec, contexto, proximo) {
  if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
    try { return proximo(spec + '.js', contexto) } catch { /* resolução normal */ }
  }
  return proximo(spec, contexto)
} })
const { MODULOS } = await import('../src/dominio/modulos.js')
const { GRUPOS_DE_PEDIDOS } = await import('../src/dominio/turnoDeHoje.js')
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const origem = process.env.CONSOLE_URL ?? 'http://127.0.0.1:5186'
const evidencias = process.env.EVIDENCIAS_UX ?? 'builds/prova-2832'
mkdirSync(evidencias, { recursive: true })

const empresaId = '11111111-1111-4111-8111-111111111111'
const hoje = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo' }).format(new Date())
const instante = `${hoje}T12:00:00-03:00`
const acoes = Object.fromEntries(['gerenciarCaixa', 'editarProducao', 'editarCardapio', 'editarAtendimento', 'verPedidos',
  'verCadastrosProdutos', 'verCadastrosFornecedores', 'verCadastrosFinanceiros', 'visualizarContasPagar', 'visualizarContasReceber'].map((n) => [n, true]))
const itens = [
  { id: 'aaaaaaaa-0000-4000-8000-000000000001', nome: 'Lasanha da casa', precoCentavos: 4500, estoqueAtual: 5, disponivel: true, linha: 'paraServir' },
  { id: 'aaaaaaaa-0000-4000-8000-000000000002', nome: 'Nhoque ao sugo', precoCentavos: 3900, estoqueAtual: 0, disponivel: true, linha: 'paraServir' },
  { id: 'aaaaaaaa-0000-4000-8000-000000000003', nome: 'Rondelli congelado', precoCentavos: 5200, estoqueAtual: 8, disponivel: false, linha: 'prepararEmCasa' },
]
// Total do dia e cada monte; a API conta, o Console só lê `meta.totalCount`.
const contagemPorStatus = { '': 7, [GRUPOS_DE_PEDIDOS[0].status]: 2, [GRUPOS_DE_PEDIDOS[1].status]: 3, [GRUPOS_DE_PEDIDOS[2].status]: 1, [GRUPOS_DE_PEDIDOS[3].status]: 1 }
const escritas = []
const erros = []
const pedidosSemDesde = []

const browser = await chromium.launch({ headless: true })
try {
  const context = await browser.newContext({ viewport: { width: 1440, height: 960 }, reducedMotion: 'reduce' })
  await context.grantPermissions(['local-network-access'], { origin: origem })
  await context.addInitScript(({ empresaId }) => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'teste-ui-controlado', expiraEm: Date.now() + 3600000, empresa: { id: empresaId, nome: 'Empresa de teste' }, usuario: { id: '22222222-2222-4222-8222-222222222222', nome: 'Dona de teste' } })), { empresaId })
  await context.route('**/api/**', async (route) => {
    const request = route.request(), url = new URL(request.url()), caminho = url.pathname, metodo = request.method()
    if (!caminho.startsWith('/api/')) return route.continue()
    const ok = (data, meta = {}) => route.fulfill({ json: { data, meta } })
    if (metodo !== 'GET') {
      escritas.push({ caminho, metodo, corpo: request.postDataJSON() })
      const item = itens.find((i) => caminho === `/api/atendimento/comanda/cardapio/${i.id}/disponivel`)
      if (item) item.disponivel = request.postDataJSON().disponivel
      return ok({})
    }
    if (caminho === '/api/auth/me/modulos') return ok({ modulos: MODULOS.map(({ id }) => ({ id, liberado: true })), acoes })
    if (caminho === '/api/atendimento/comanda/cardapio') return ok({ itens, porcoes: {} }, { lojaDisponivel: true })
    if (caminho === '/api/caixa/dia') return ok({ data: hoje, aberto: true, fechado: false, saldoInicial: 100, saldoEsperado: 150, movimentos: [],
      linhasExtras: [
        { hora: instante, tipo: 'pagamento', valor: 40, metodo: 'pix', origem: 'Pedido', pedidoNumeroDoDia: 3 },
        { hora: instante, tipo: 'venda', valor: 50, metodo: 'dinheiro', origem: 'Venda' },
      ] })
    if (caminho === '/api/pedidos') {
      if (url.searchParams.get('desde') !== hoje) pedidosSemDesde.push(url.search)
      const total = contagemPorStatus[url.searchParams.get('status') ?? ''] ?? 0
      return route.fulfill({ json: { data: [], meta: { totalCount: total, page: 1, pageSize: Number(url.searchParams.get('pageSize')) || 20 } } })
    }
    if (caminho === '/api/atendimento/configuracao') return ok({})
    if (caminho === '/api/atendimento/agenda') return ok({ lojaDisponivel: false, dias: [] })
    if (caminho === '/api/atendimento/comanda/cardapio/secoes') return ok([])
    if (caminho === '/api/notificacoes/badge') return ok({ count: 0 })
    if (/nao-lidas|não-lidas|count/.test(caminho)) return ok({ total: 0 })
    return ok([])
  })
  const page = await context.newPage()
  page.on('pageerror', (erro) => erros.push(erro.message))
  page.on('console', (m) => { if (m.type() === 'error' && !/Failed to load resource/.test(m.text())) erros.push(m.text()) })

  const abrirGestao = async ({ celular = false } = {}) => {
    if (!celular) await page.getByRole('button', { name: 'Gestão', exact: true }).locator('visible=true').first().click()
    else {
      await page.getByRole('button', { name: 'Mais opções', exact: true }).click()
      await page.getByRole('menuitem', { name: 'Gestão', exact: true }).click()
    }
    const gestao = page.getByRole('dialog', { name: 'Gestão da loja', exact: true })
    await gestao.waitFor()
    return gestao
  }
  const assentar = () => page.evaluate(async () => {
    await document.fonts.ready
    await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))
  })

  await page.goto(origem + '/#/m/atendimento/balcao')
  let gestao = await abrirGestao()
  // A Gestão no modo API abre no Agora, com os quatro cartões.
  await gestao.getByRole('button', { name: 'Agora', exact: true, pressed: true }).waitFor()
  const cartao = (nome) => gestao.getByRole('region', { name: nome, exact: true })
  await cartao('Cardápio do dia').getByText('2 de 3 no ar', { exact: true }).waitFor()
  await cartao('Caixa do dia').getByText(/150,00/).waitFor()
  await cartao('Caixa do dia').getByText('Aberto', { exact: true }).waitFor()
  const caixaTexto = await cartao('Caixa do dia').innerText()
  assert.match(caixaTexto, /Dinheiro\s+R\$\s*50,00/, 'recebido em dinheiro')
  assert.match(caixaTexto, /Pix\s+R\$\s*40,00/, 'recebido no Pix')
  await cartao('Pedidos de hoje').getByText('7 no total', { exact: true }).waitFor()
  const pedidosTexto = await cartao('Pedidos de hoje').innerText()
  assert.match(pedidosTexto, /2\s+esperando pagamento/)
  assert.match(pedidosTexto, /3\s+na cozinha/)
  assert.match(pedidosTexto, /1\s+saindo para entrega/)
  assert.match(pedidosTexto, /1\s+entregues/)
  const precisa = cartao('Precisa de você')
  await precisa.getByText('2 pedidos esperando pagamento', { exact: true }).waitFor()
  await precisa.getByText('1 prato sem saldo no ar', { exact: true }).waitFor()
  assert.match(await precisa.innerText(), /Nhoque ao sugo/)
  assert.equal((await precisa.locator('header').innerText()).replace(/\s+/g, ' ').trim(), 'Precisa de você 2', 'o contador mostra o número')
  assert.deepEqual(pedidosSemDesde, [], 'toda contagem é do dia da loja')
  await assentar()
  await page.screenshot({ path: join(evidencias, 'agora-1440-claro.png') })

  // Esgotou grava na rota do cardápio e o cartão relê.
  await cartao('Cardápio do dia').getByRole('button', { name: 'Marcar como esgotado: Lasanha da casa', exact: true }).click()
  await cartao('Cardápio do dia').getByText('1 de 3 no ar', { exact: true }).waitFor()
  assert.deepEqual(escritas.map((e) => [e.metodo, e.caminho, e.corpo]), [['POST', `/api/atendimento/comanda/cardapio/${itens[0].id}/disponivel`, { disponivel: false }]])
  await cartao('Cardápio do dia').getByRole('button', { name: 'Voltar ao cardápio: Lasanha da casa', exact: true }).click()
  await cartao('Cardápio do dia').getByText('2 de 3 no ar', { exact: true }).waitFor()

  // O monte de pedidos abre a tela de Pedidos; de volta ao Agora, a faixa "Você mais usa" mostra a tela aberta.
  await cartao('Pedidos de hoje').getByRole('button', { name: /esperando pagamento/ }).click()
  await gestao.getByRole('tab', { selected: true, name: /Pedidos/ }).first().waitFor()
  await gestao.getByRole('button', { name: 'Gestão da loja', exact: true }).click()
  await gestao.getByRole('button', { name: 'Agora', exact: true, pressed: true }).waitFor()
  await gestao.getByRole('navigation', { name: 'Você mais usa', exact: true }).getByRole('button').first().waitFor()

  // Escuro, pelo menu da conta (o mesmo caminho da prova do financeiro).
  await page.keyboard.press('Escape')
  await gestao.waitFor({ state: 'hidden' })
  await page.getByRole('button', { name: /^Conta de / }).locator('visible=true').first().click()
  await page.getByRole('group', { name: 'Conta', exact: true }).getByRole('radio', { name: 'Escuro', exact: true }).click()
  await page.keyboard.press('Escape')
  gestao = await abrirGestao()
  await cartao('Caixa do dia').getByText(/150,00/).waitFor()
  await assentar()
  await page.screenshot({ path: join(evidencias, 'agora-1440-escuro.png') })
  await page.keyboard.press('Escape')

  // 390 px: uma coluna, sem rolagem lateral.
  await page.setViewportSize({ width: 390, height: 844 })
  await page.reload()
  gestao = await abrirGestao({ celular: true })
  await cartao('Pedidos de hoje').getByText('7 no total', { exact: true }).waitFor()
  await assentar()
  const larguras = await gestao.evaluate((no) => ({ rolagem: no.scrollWidth, visivel: no.clientWidth, pagina: document.documentElement.scrollWidth, janela: innerWidth }))
  assert.ok(larguras.rolagem <= larguras.visivel + 1, `a Gestão não rola de lado: ${JSON.stringify(larguras)}`)
  assert.ok(larguras.pagina <= larguras.janela + 1, `a página não rola de lado: ${JSON.stringify(larguras)}`)
  const [c1, c2] = await Promise.all([cartao('Cardápio do dia').boundingBox(), cartao('Caixa do dia').boundingBox()])
  assert.ok(c2.y >= c1.y + c1.height - 1, 'no estreito os cartões empilham')
  await page.screenshot({ path: join(evidencias, 'agora-390-escuro.png'), fullPage: true })

  assert.deepEqual(erros, [])
  console.log('prova-2832-turno-navegador: ok (cartões, Esgotou, pedidos, mais usadas, claro, escuro, 390 px)')
} finally {
  await browser.close()
}
