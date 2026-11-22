// #3076 (E4, depois do pedido): Pedidos de Cadastros e Financeiro no app real com HTTP controlado.
// L1: Registrar pagamento no detalhe (ligado, desligado com motivo, sem botão no cancelado, no máximo 4 cliques).
// L2: coluna e filtro Entrega, ordem pela entrega, "Abrir conversa" só da conversa DESTE pedido (#3130), celular.
// L3: linha "Pedidos a receber" no Financeiro, que some sem o campo (API antiga) ou com zero.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/3076/e4')
mkdirSync(pasta, { recursive: true })
const URL_CONSOLE = process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'
const HASH = '#/m/cadastros/pedidos'
const HASH_FINANCEIRO = '#/m/financeiro/visao-geral'
const agora = new Date()
const diaDaLoja = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo' }).format(agora)
const norm = (t) => t.replace(/\s/g, ' ')
const ate = async (condicao, msg) => {
  for (let i = 0; i < 80; i++) { if (condicao()) return; await new Promise((r) => setTimeout(r, 100)) }
  assert.fail(msg)
}

const base = (i, extra) => ({
  id: extra.id, numeroDoDia: i, clienteId: extra.clienteId ?? `cli-${i}`, clienteNome: extra.nome, clienteTelefone: '11999990000',
  status: extra.status, total: extra.total ?? 80, totalPago: extra.totalPago ?? 0, aReceber: extra.aReceber ?? 80, origem: 'whatsapp',
  criadoEm: new Date(agora.getTime() - i * 60000).toISOString(), agendadoParaEm: extra.agendadoParaEm ?? null,
  enderecoEntrega: extra.enderecoEntrega ?? null,
})
const modelo = () => [
  base(1, { id: 'p-pronto', nome: 'Ana Paula', status: 'pronto', aReceber: 80, agendadoParaEm: new Date(agora.getTime() + 3600000).toISOString(), enderecoEntrega: 'Rua das Flores, 10' }),
  base(2, { id: 'p-rascunho', nome: 'Bruno Lima', status: 'rascunho' }),
  base(3, { id: 'p-baba', nome: 'Carla Souza', status: 'aguardando_aprovacao_baba' }),
  base(4, { id: 'p-aguardando', nome: 'Diego Alves', status: 'aguardando_pagamento' }),
  // Cancelado com R$ 50,00 recebido e a API ainda dizendo aReceber: o detalhe não mostra saldo (revisão da #3144).
  base(5, { id: 'p-cancelado', nome: 'Elisa Prado', status: 'cancelado', total: 132.3, totalPago: 50, aReceber: 132.3 }),
  base(6, { id: 'p-bia', nome: 'Bia Rocha', status: 'pronto', clienteId: 'cli-bia' }),
]
const detalheDe = (p) => ({
  pedido: { ...p, observacoes: '' },
  itens: [{ id: 'i1', nome: 'Lasanha de berinjela', quantidade: 2, unidade: 'un', precoUnitario: 40, subtotal: 80 }],
  pagamentos: p.totalPago ? [{ metodo: 'pix', valor: p.totalPago, pagoEm: p.criadoEm, registradoPorNome: 'Dona de teste' }] : [],
  eventos: [{ tipo: 'criado', criadoEm: p.criadoEm, origem: p.origem }],
})

const browser = await chromium.launch({ headless: true })
const erros = []
async function preparar({ largura = 1440, altura = 900, hash = HASH, dashboard = null } = {}) {
  const estado = { pedidos: modelo(), consultas: [], leituras: [], posts: [] }
  const contexto = await browser.newContext({ viewport: { width: largura, height: altura }, isMobile: largura < 700, hasTouch: largura < 700, locale: 'pt-BR', timezoneId: 'America/Sao_Paulo', reducedMotion: 'reduce' })
  await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa', nome: 'Loja dos pedidos' }, usuario: { id: 'dona', nome: 'Dona de teste' } })))
  const acoes = Object.fromEntries(['verPedidos', 'verContas', 'verContasPagar', 'verContasReceber', 'visualizarContasPagar', 'visualizarContasReceber', 'gerenciarContasPagar', 'gerenciarContasReceber', 'gerenciarCaixa', 'editarAtendimento'].map((n) => [n, true]))
  await contexto.route('**/api/**', async (route) => {
    const req = route.request(), url = new URL(req.url()), path = url.pathname, metodo = req.method()
    if (!path.startsWith('/api/')) return route.continue() // módulos do Vite em src/infra/api/
    const ok = (data, extra = {}) => route.fulfill({ json: { data, ...extra } })
    if (metodo !== 'GET') {
      const pago = path.match(/^\/api\/pedidos\/([^/]+)\/pagamentos$/)
      assert.ok(pago || path.endsWith('/marcar-lida'), `Escrita inesperada: ${metodo} ${path}`)
      if (pago) {
        const corpo = req.postDataJSON()
        estado.posts.push({ id: pago[1], corpo, chave: req.headers()['idempotency-key'] })
        const p = estado.pedidos.find((x) => x.id === pago[1])
        p.totalPago += corpo.valor; p.aReceber = Math.max(0, p.aReceber - corpo.valor)
        return ok({ id: 'pag-1' })
      }
      return ok(null)
    }
    if (path === '/api/auth/me/modulos') return ok({ modulos: ['atendimento', 'cadastros', 'financeiro'].map((id) => ({ id, liberado: true })), acoes })
    if (path === '/api/pedidos') {
      estado.consultas.push(Object.fromEntries(url.searchParams))
      return ok(estado.pedidos, { meta: { totalCount: estado.pedidos.length } })
    }
    const unico = path.match(/^\/api\/pedidos\/([^/]+)$/)
    if (unico) { estado.leituras.push(unico[1]); return ok(detalheDe(estado.pedidos.find((p) => p.id === unico[1]))) }
    if (path === '/api/cadastros/metodos-pagamento') return ok([{ codigo: 'pix', nome: 'Pix', ativo: true }, { codigo: 'dinheiro', nome: 'Dinheiro', ativo: true }])
    // #3130: a conversa da Bia está carregada, mas o pedido em andamento dela é OUTRO (p-antigo), não o p-bia da lista.
    if (path === '/api/atendimento/conversas') return ok([{ id: 'conversa-bia', contatoNome: 'Bia Rocha', contatoIdExterno: '5511999990001', clienteId: 'cli-bia', canal: 'WhatsApp', situacao: 'Assumida', ultimaMensagemEm: agora.toISOString(), naoLidas: 0, pedidoEmAndamentoId: 'p-antigo', pedidoStatus: 'aguardando_pagamento' }])
    if (path.endsWith('/mensagens')) return ok([])
    if (path.endsWith('/pedido')) return ok({ pedidoId: 'p-antigo', numeroComanda: '099', status: 'aguardando_pagamento', total: 40, totalPago: 0, itens: [], observacoes: '' })
    if (path === '/api/atendimento/configuracao') return ok({ slaRespostaMinutos: 5 })
    if (path === '/api/atendimento/agenda') return ok({ lojaDisponivel: true, dias: [] })
    if (path === '/api/atendimento/comanda/cardapio') return ok({ itens: [], porcoes: {} })
    if (path === '/api/notificacoes/badge') return ok({ count: 0 })
    if (path === '/api/financeiro/dashboard') return ok({ totalAVencer30dPagar: 0, totalAVencer30dReceber: 50, totalVencidoPagar: 0, totalVencidoReceber: 0, totalPagoMes: 0, totalRecebidoMes: 0, qtdContasPagarAVencer30d: 0, qtdContasReceberAVencer30d: 1, ...dashboard })
    if (/^\/api\/contas-a-(pagar|receber)$/.test(path)) return ok([], { meta: { total: 0, page: 1, limit: 20 } })
    return ok([])
  })
  const page = await contexto.newPage()
  page.setDefaultTimeout(12000)
  page.on('pageerror', (e) => erros.push(e.message))
  page.on('console', (m) => { if (m.type() === 'error' && !m.text().startsWith('Failed to load resource')) erros.push('console: ' + m.text()) })
  await page.goto(`${URL_CONSOLE}/${hash}`)
  return { contexto, page, estado }
}
const linhas = (page) => page.getByRole('table').locator('tbody tr')
const abrirDe = (painel, numero) => painel.getByRole('button', { name: new RegExp(`^Abrir Pedido Nº 00${numero} de`) })

// a. Pronto com saldo 80: Entrega e Pagamento no detalhe; formulário com o saldo; POST com idempotência; releitura.
// T3: da linha "Abrir" à confirmação, no máximo 4 cliques.
{
  const { contexto, page, estado } = await preparar()
  const painel = page.getByRole('region', { name: 'Pedidos', exact: true })
  await linhas(page).nth(5).waitFor()
  let cliques = 0
  await abrirDe(painel, 1).click(); cliques++
  const detalhe = page.getByRole('dialog', { name: /^Pedido Nº 001 de/ })
  await detalhe.waitFor()
  const texto = norm(await detalhe.innerText())
  for (const esperado of ['Entrega', 'Pagamento', 'Rua das Flores, 10', 'Falta receber R$ 80,00']) assert.ok(texto.includes(esperado), `detalhe mostra "${esperado}"`)
  assert.match(texto, /Hoje, \d\d:\d\d/, 'quando entregar no cabeçalho')
  const botao = detalhe.getByRole('button', { name: 'Registrar pagamento', exact: true })
  assert.equal(await botao.isEnabled(), true, 'Registrar pagamento ligado')
  await page.screenshot({ path: `${pasta}/detalhe-pronto-1440.png` })
  await botao.click(); cliques++
  assert.equal(norm(await detalhe.getByLabel('Valor recebido').inputValue()), '80,00', 'valor preenchido com o saldo')
  await detalhe.getByLabel('Como recebeu').selectOption('pix'); cliques++
  const consultasAntes = estado.consultas.length, leiturasAntes = estado.leituras.length
  await detalhe.getByRole('button', { name: 'Confirmar pagamento', exact: true }).click(); cliques++
  await ate(() => estado.posts.length === 1, 'um POST de pagamento')
  const post = estado.posts[0]
  assert.equal(post.id, 'p-pronto'); assert.equal(post.corpo.valor, 80); assert.equal(post.corpo.metodo, 'pix')
  assert.ok(post.chave && post.chave.length > 8, `chave de idempotência (${post.chave})`)
  await ate(() => estado.leituras.length > leiturasAntes, 'releu GET /api/pedidos/{id}')
  assert.equal(estado.leituras.at(-1), 'p-pronto')
  await ate(() => estado.consultas.length > consultasAntes, 'a lista foi pedida de novo')
  assert.ok(cliques <= 4, `da linha Abrir à confirmação em ${cliques} cliques (T3: no máximo 4)`)
  await page.waitForFunction(() => !document.body.innerText.includes('Confirmar pagamento'))
  assert.equal(await detalhe.getByRole('button', { name: 'Registrar pagamento' }).count(), 0, 'sem saldo, sem botão')
  await contexto.close()
}

// b. Pré-operacional: botão desligado e o motivo visível (sem passar o mouse).
// c. Cancelado: nenhum botão. d. Conversa de OUTRO pedido: sem "Abrir conversa".
{
  const { contexto, page } = await preparar()
  const painel = page.getByRole('region', { name: 'Pedidos', exact: true })
  await linhas(page).nth(5).waitFor()
  const casos = [[2, 'A cliente ainda não terminou este pedido no cardápio.'], [3, 'Aprove o pedido em Entregas, no dia da entrega, antes de registrar o pagamento.'], [4, 'Este pedido espera o pagamento do link ou o sinal combinado.']]
  for (const [numero, motivo] of casos) {
    await abrirDe(painel, numero).click()
    const detalhe = page.getByRole('dialog', { name: new RegExp(`^Pedido Nº 00${numero} de`) })
    await detalhe.waitFor()
    const botao = detalhe.getByRole('button', { name: 'Registrar pagamento', exact: true })
    assert.equal(await botao.isDisabled(), true, `pedido ${numero}: botão desligado`)
    assert.ok(norm(await detalhe.innerText()).includes(motivo), `pedido ${numero}: motivo visível`)
    await page.keyboard.press('Escape'); await detalhe.waitFor({ state: 'hidden' })
  }
  await abrirDe(painel, 5).click()
  const cancelado = page.getByRole('dialog', { name: /^Pedido Nº 005 de/ })
  await cancelado.waitFor()
  assert.equal(await cancelado.getByRole('button', { name: 'Registrar pagamento' }).count(), 0, 'cancelado sem botão')
  const textoCancelado = norm(await cancelado.innerText())
  assert.doesNotMatch(textoCancelado, /Falta receber/, 'cancelado sem saldo a receber, nunca R$ 132,30')
  assert.match(textoCancelado, /R\$ 50,00/, 'cancelado mostra o que foi recebido')
  await page.keyboard.press('Escape'); await cancelado.waitFor({ state: 'hidden' })
  await abrirDe(painel, 6).click()
  const bia = page.getByRole('dialog', { name: /^Pedido Nº 006 de/ })
  await bia.waitFor()
  assert.equal(await bia.getByRole('button', { name: 'Abrir conversa' }).count(), 0, 'conversa de outro pedido: sem Abrir conversa')
  await contexto.close()
}

// e. 1440x900: coluna Entrega visível. 390x844: coluna escondida, "Entrega:" na linha, sem rolagem lateral.
{
  const { contexto, page } = await preparar({ largura: 1440, altura: 900 })
  await linhas(page).nth(5).waitFor()
  await page.getByRole('columnheader', { name: 'Entrega', exact: true }).waitFor()
  const primeira = linhas(page).first()
  assert.equal(await primeira.locator('td').nth(3).isVisible(), true, 'coluna Entrega visível no desktop')
  assert.match(norm(await primeira.locator('td').nth(3).innerText()), /Hoje, \d\d:\d\d/)
  assert.equal(norm(await linhas(page).nth(1).locator('td').nth(3).innerText()), 'Na hora', 'sem agendamento: Na hora')
  await page.screenshot({ path: `${pasta}/lista-1440.png`, fullPage: true })
  await contexto.close()
}
{
  const { contexto, page } = await preparar({ largura: 390, altura: 844 })
  await linhas(page).nth(5).waitFor()
  const primeira = linhas(page).first()
  assert.equal(await primeira.locator('td').nth(3).isVisible(), false, 'coluna Entrega escondida no celular')
  assert.ok(await primeira.locator('td').first().getByText(/^Entrega: /).isVisible(), '"Entrega:" na linha do número')
  const rolagem = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
  assert.ok(rolagem <= 1, `sem rolagem lateral (${rolagem}px)`)
  const pai = await page.evaluate(() => { const p = document.querySelector('table').parentElement; return [p.scrollWidth, p.clientWidth] })
  assert.ok(pai[0] <= pai[1] + 1, `contêiner da tabela sem rolagem (scrollWidth ${pai[0]} <= clientWidth ${pai[1]})`)
  await page.screenshot({ path: `${pasta}/lista-390.png`, fullPage: true })
  await contexto.close()
}

// f. Filtro Entrega = Hoje e ordem "Entrega (mais cedo)" no contrato do `GET api/pedidos`.
{
  const { contexto, page, estado } = await preparar()
  const painel = page.getByRole('region', { name: 'Pedidos', exact: true })
  await linhas(page).nth(5).waitFor()
  await painel.getByRole('combobox', { name: 'Entrega' }).click()
  await page.getByRole('option', { name: 'Hoje', exact: true }).click()
  await ate(() => estado.consultas.some((q) => q.entregaDesde), 'consulta com entregaDesde')
  const q = estado.consultas.findLast((x) => x.entregaDesde)
  assert.equal(q.entregaDesde, diaDaLoja); assert.equal(q.entregaAte, diaDaLoja)
  await painel.getByRole('combobox', { name: 'Ordem' }).click()
  await page.getByRole('option', { name: 'Entrega (mais cedo)', exact: true }).click()
  await ate(() => estado.consultas.some((x) => x.sort === 'entrega'), 'consulta com sort=entrega')
  assert.equal(estado.consultas.findLast((x) => x.sort === 'entrega').order, 'asc')
  await contexto.close()
}

// g. Financeiro: a linha "Pedidos a receber" só com `qtdPedidosAReceber` maior que zero.
for (const [rotulo, dashboard, esperado] of [
  ['com pedidos', { totalPedidosAReceber: 132.3, qtdPedidosAReceber: 1 }, 'Pedidos a receber: R$ 132,30 (1 pedido)'],
  ['API antiga, sem os campos', {}, null],
  ['quantidade zero', { totalPedidosAReceber: 0, qtdPedidosAReceber: 0 }, null],
]) {
  const { contexto, page } = await preparar({ hash: HASH_FINANCEIRO, dashboard })
  const card = page.getByText('A receber nos próximos 30 dias', { exact: true }).locator('xpath=..')
  await card.waitFor()
  await card.getByText(/^1 conta$/).waitFor()
  const texto = norm(await card.innerText())
  if (esperado) {
    assert.ok(texto.includes(esperado), `${rotulo}: "${esperado}" (veio "${texto}")`)
    assert.ok(await card.getByText(/^Pedidos a receber/).getAttribute('title'), 'a linha explica de onde vem (title)')
    await page.screenshot({ path: `${pasta}/financeiro-1440.png` })
  } else assert.equal(texto.includes('Pedidos a receber'), false, `${rotulo}: sem a linha`)
  await contexto.close()
}

await browser.close()
assert.deepEqual(erros, [], 'sem erro de página')
console.log(`prova-3076-e4-navegador: ok (capturas em ${pasta})`)
