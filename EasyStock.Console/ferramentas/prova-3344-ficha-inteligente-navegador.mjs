// #3344: a Ficha do cliente lê o cliente (estágio, risco, última compra), sugere pratos de compra real e o Assistente
// age só no clique, devolvendo texto para revisar. Em 1440 e 390, sem erro de página e sem rolagem horizontal.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/3344')
mkdirSync(pasta, { recursive: true })
const URL_CONSOLE = process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'
// Medido no master 6afdd5c2a (uma coluna, 760 px) com este cliente: 1843 px de miolo, em telas (scrollHeight sobre a
// altura visível). A ficha nova tem de rolar no máximo 60% disso em cada tamanho.
const TELAS_NO_MASTER = { 1920: 2.28, 1440: 2.82, 1366: 3.42 }
const ID = 'b0000001-1111-4111-8111-111111111111'
const cliente = { id: ID, revisao: 1, nome: 'Marina Perfil', telefone: '+5511999998888', email: 'marina.perfil@exemplo.com', documento: '12345678909', tipoPessoa: 'fisica', ativo: true, observacoes: '', orderCount: 12, lastOrderAt: '2026-11-10T15:30:00Z', fotoUrl: null }
const enderecos = [
  { id: 'E1', clienteId: ID, logradouro: 'Rua das Flores', numero: '120', complemento: 'Apto 21', bairro: 'Centro', cidade: 'São Paulo', estado: 'SP', cep: '01001000', padrao: true },
  { id: 'E2', clienteId: ID, logradouro: 'Avenida Brasil', numero: '900', complemento: '', bairro: 'Jardins', cidade: 'São Paulo', estado: 'SP', cep: '01430000', padrao: false },
]
const telefones = [
  { id: 'T1', clienteId: ID, numero: '+5511999998888', whatsapp: true, principal: true },
  { id: 'T2', clienteId: ID, numero: '+5511955554444', whatsapp: false, principal: false },
  { id: 'T3', clienteId: ID, numero: '+5511944443333', whatsapp: true, principal: false },
]
const notas = [
  { id: 'N3', texto: 'Alergia a camarão. Prefere entrega depois das 18h.', autor: 'Operadora', pedidoId: null, mensagemId: null, criadoEm: '2026-11-12T21:05:00' },
  { id: 'N2', texto: 'Pede troco para 100.', autor: 'Operadora', pedidoId: null, mensagemId: null, criadoEm: '2026-11-11T15:00:00' },
  { id: 'N1', texto: 'Cliente desde a inauguração.', autor: 'Dona', pedidoId: null, mensagemId: null, criadoEm: '2026-11-01T12:00:00' },
]
const conversas = Array.from({ length: 5 }, (_, i) => ({ id: `C${i}`, canal: i % 2 ? 'WhatsApp' : 'ChatSite', situacao: 'Encerrada', iniciadaEm: `2026-11-0${i + 1}T12:00:00`, ultimaMensagemEm: `2026-11-0${i + 1}T13:00:00` }))
const dossie = {
  cliente: { id: ID, nome: cliente.nome, telefone: cliente.telefone, email: cliente.email, observacoes: '', motivoBloqueio: null, fotoUrl: null },
  enderecos, tags: ['sem_lactose', 'mora_perto', 'vip'].map(tag => ({ tag, origem: 'manual', criadoEm: '2026-11-10T12:00:00' })), notas, ultimosPedidos: [],
  itemFavorito: { nome: 'Feijoada', pedidos: 5 }, ultimaCompraEm: '2026-11-10T15:30:00', totalPedidos: 12, bloqueado: false,
  conversasRecentes: conversas, pedidoEmAndamento: null, contatoInformado: null, totalGasto: 1480.5,
}
// Só a prova de 1440 usa o histórico; as medidas de rolagem seguem com o mesmo cliente medido no master.
const HISTORICO = [{ id: 'H1', campo: 'E-mail', alteradoPorNome: 'Operadora', valorAntigo: 'marina@antigo.com', valorNovo: 'marina.perfil@exemplo.com', alteradoEm: '2026-11-09T12:00:00' }]
const pedidos = Array.from({ length: 5 }, (_, i) => ({ id: `P${i + 1}`, numeroDoDia: 10 + i, dataNumero: `2026-11-0${i + 1}`, criadoEm: `2026-11-0${i + 1}T12:00:00Z`, status: 'Entregue', total: 90 + i * 10, clienteNome: cliente.nome }))
const perguntas = []
const browser = await chromium.launch({ headless: true })
const erros = []

async function abrirFicha(largura, altura, { alteracoes = [] } = {}) {
  const movel = largura < 700
  const contexto = await browser.newContext({ viewport: { width: largura, height: altura }, isMobile: movel, hasTouch: movel, locale: 'pt-BR', timezoneId: 'America/Sao_Paulo', reducedMotion: 'reduce' })
  await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa', nome: 'Loja' }, usuario: { id: 'dona', nome: 'Dona de teste' } })))
  const acoes = Object.fromEntries(['verPedidos', 'atenderConversas', 'editarAtendimento', 'verCadastrosClientes', 'editarCadastrosClientes', 'verCadastrosProdutos', 'verCadastrosFornecedores', 'verCadastrosFinanceiros'].map((n) => [n, true]))
  await contexto.route('**/api/**', async (route) => {
    const req = route.request(), path = new URL(req.url()).pathname
    const ok = (data, extra = {}) => route.fulfill({ json: { data, ...extra } })
    if (!path.startsWith('/api/')) return route.continue()
    if (path === '/api/auth/me/modulos') return ok({ modulos: ['atendimento', 'cadastros', 'cardapio', 'financeiro'].map((id) => ({ id, liberado: true })), acoes })
    if (path === '/api/atendimento/configuracao') return ok({ slaRespostaMinutos: 5 })
    if (path === '/api/atendimento/agenda') return ok({ lojaDisponivel: true, dias: [] })
    if (path === '/api/notificacoes/badge') return ok({ count: 0 })
    if (path === '/api/clientes') return ok([{ ...cliente, estagio: 'recorrente', emRisco: true, compras: 12, gastoTotal: 1480.5, ticketMedio: 123.38, ultimaCompraEm: '2026-10-20T15:30:00Z', criadoEm: '2026-08-01T12:00:00Z', ocorrenciasAbertas: 1, ocorrenciasTotal: 2, notasTotal: 3, tags: [] }], { meta: { total: 1, totalCount: 1, page: 1, limit: 200 } })
    if (path === `/api/clientes/${ID}`) return ok({ cliente, enderecos, telefones, documentos: [], alteracoes })
    if (path === `/api/clientes/${ID}/dossie`) return ok(dossie)
    if (path === '/api/pedidos/P1') return ok({ pedido: { ...pedidos[0] }, itens: [{ id: 'I1', nome: 'Lasanha de frango', quantidade: 2, unidade: 'un', precoUnitario: 45 }, { id: 'I2', nome: 'Feijoada', quantidade: 1, unidade: 'un', precoUnitario: 30 }, { id: 'I3', nome: 'Pudim', quantidade: 1, unidade: 'un', precoUnitario: 12 }], eventos: [], pagamentos: [] })
    if (path === '/api/atendimento/assistente' && req.method() === 'POST') { perguntas.push(JSON.parse(req.postData() ?? '{}')); return route.fulfill({ json: { data: { resposta: 'Oi, Marina! Sentimos sua falta. A Feijoada e a Lasanha estão saindo hoje, quer que eu reserve?', acoes: [] } } }) }
    if (path === '/api/pedidos') return ok(pedidos, { meta: { totalCount: 12, total: 12, page: 1, limit: 5 } })
    return ok([], { meta: { totalCount: 0, total: 0, page: 1, limit: 20 } })
  })
  const page = await contexto.newPage()
  page.setDefaultTimeout(12000)
  page.on('pageerror', (e) => erros.push(e.message))
  await page.goto(`${URL_CONSOLE}/#/m/cadastros/clientes`)
  await page.locator('tbody tr').filter({ hasText: cliente.nome }).getByRole('button', { name: /^Abrir ficha/ }).click()
  const ficha = page.getByRole('dialog', { name: 'Ficha do cliente', exact: true })
  await ficha.getByRole('region', { name: `Resumo de ${cliente.nome}`, exact: true }).waitFor()
  // O dossiê e os pedidos chegaram: a medida vale para a ficha completa.
  await ficha.getByText('Alergia a camarão. Prefere entrega depois das 18h.', { exact: true }).waitFor()
  // #3387: o favorito chega em "O que oferecer" (a aba Pedidos não repete mais "Costuma pedir").
  await ficha.getByRole('listitem').filter({ hasText: 'Feijoada' }).filter({ hasText: 'Costuma pedir' }).first().waitFor()
  return { contexto, page, ficha }
}
// O miolo que rola é o filho do diálogo com rolagem vertical própria.
const miolo = (ficha) => ficha.evaluate((d) => {
  const el = [...d.querySelectorAll('*')].find(e => ['auto', 'scroll'].includes(getComputedStyle(e).overflowY) && e.clientHeight > 100)
  const r = el.getBoundingClientRect()
  return { scrollHeight: el.scrollHeight, clientHeight: el.clientHeight, scrollWidth: el.scrollWidth, clientWidth: el.clientWidth, topo: r.top, base: r.top + el.clientHeight }
})
const falhas = []
const item = async (nome, corpo) => {
  try { await corpo(); console.log(`ok    ${nome}`) } catch (e) { const motivo = e.message.split('\n').slice(0, 4).join(' | '); falhas.push(`${nome}: ${motivo}`); console.log(`FALHA ${nome}: ${motivo}`) }
}


await item('1440: leitura, pratos de compra real e Assistente só no clique', async () => {
  const { contexto, page, ficha } = await abrirFicha(1440, 900)
  const painel = ficha.getByRole('region', { name: 'Leitura do cliente', exact: true })
  await painel.getByText('Recorrente', { exact: true }).waitFor()
  await painel.getByText('Em risco', { exact: true }).waitFor()
  await painel.getByText('Pediu há', { exact: false }).waitFor()
  await painel.getByText('1 reclamação aberta', { exact: true }).waitFor()
  await painel.getByRole('img', { name: 'Estágio 3 de 4: Recorrente' }).waitFor()
  await painel.getByText('Resolva a reclamação antes de oferecer algo.').waitFor()
  await painel.getByRole('listitem').filter({ hasText: 'Lasanha de frango' }).waitFor()
  assert.equal(await painel.getByRole('listitem').filter({ hasText: 'Feijoada' }).count(), 1, 'favorito e último pedido não repetem Feijoada')
  assert.equal(await painel.getByRole('listitem').count(), 3, 'no máximo 3 pratos')
  assert.equal(perguntas.length, 0, 'a IA não roda ao abrir a ficha')
  await painel.getByRole('button', { name: 'Escrever mensagem de recompra', exact: true }).click()
  await painel.getByText('Sentimos sua falta', { exact: false }).waitFor()
  assert.equal(perguntas.length, 1); assert.equal(perguntas[0].conversaId, null, 'sem conversa: o histórico de mensagens não segue'); assert.match(perguntas[0].pergunta, /Marina/); assert.match(perguntas[0].pergunta, /Feijoada/)
  assert.doesNotMatch(perguntas[0].pergunta, /marina\.perfil@|99999|12345678909/, 'a pergunta não leva e-mail, telefone nem documento')
  await painel.getByText('Nada foi enviado ao cliente.', { exact: false }).waitFor()
  await page.screenshot({ path: `${pasta}/leitura-1440.png` })
  await painel.getByRole('button', { name: 'Descartar', exact: true }).click()
  assert.equal(await painel.getByText('Sentimos sua falta', { exact: false }).count(), 0)
  await contexto.close()
})

await item('390: sem rolagem horizontal e com o painel à vista', async () => {
  const { contexto, page, ficha } = await abrirFicha(390, 844)
  await ficha.getByRole('region', { name: 'Leitura do cliente', exact: true }).waitFor()
  const pagina = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
  const medida = await miolo(ficha)
  assert.equal(pagina, 0, 'sem rolagem horizontal na página'); assert.ok(medida.scrollWidth <= medida.clientWidth + 1, 'sem rolagem horizontal no modal')
  const alvo = await ficha.getByRole('button', { name: 'Sugerir oferta', exact: true }).boundingBox()
  assert.ok(alvo.height >= 44, `alvo de toque de ${Math.round(alvo.height)} px`)
  await page.screenshot({ path: `${pasta}/leitura-390.png` })
  await contexto.close()
})

await browser.close()
if (erros.length) falhas.push(`erros de página: ${erros.slice(0, 3).join(' | ')}`)
if (falhas.length) { console.log(`\n${falhas.length} falha(s)`); process.exit(1) }
console.log('\n#3344 ficha inteligente: leitura, pratos e Assistente conferidos.')
