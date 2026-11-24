// #3249: a Ficha do cliente em Cadastros vira perfil em duas colunas. Cliente rico e fictício (12 pedidos, 3 notas,
// 3 tags, 2 endereços, 3 telefones, 5 conversas). Em 1920x1080, 1440x900 e 1366x768: o cartão do cliente fica à
// esquerda das abas, as abas aparecem sem rolar e o miolo do modal fica bem mais curto que no master. Em 390: sem
// rolagem horizontal e com as ações da ficha. As abas trocam o painel e o painel mantém a região com o título.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/3249')
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
    if (path === '/api/clientes') return ok([cliente], { meta: { total: 1, totalCount: 1, page: 1, limit: 200 } })
    if (path === `/api/clientes/${ID}`) return ok({ cliente, enderecos, telefones, documentos: [], alteracoes })
    if (path === `/api/clientes/${ID}/dossie`) return ok(dossie)
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
  await ficha.getByText(/Costuma pedir Feijoada/).first().waitFor()
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

for (const [largura, altura] of [[1920, 1080], [1440, 900], [1366, 768]]) {
  await item(`${largura}x${altura}: cartão à esquerda das abas, abas sem rolar, miolo curto`, async () => {
    const { contexto, page, ficha } = await abrirFicha(largura, altura)
    const medida = await miolo(ficha)
    console.log(`      miolo ${largura}x${altura}: ${medida.scrollHeight}/${medida.clientHeight} px = ${(medida.scrollHeight / medida.clientHeight).toFixed(2)} telas`)
    await page.screenshot({ path: `${pasta}/ficha-${largura}.png` })
    const abas = ficha.getByRole('tablist', { name: 'Partes da ficha', exact: true })
    assert.equal(await abas.count(), 1, 'a ficha tem a barra de abas')
    const cartao = await ficha.getByRole('region', { name: `Resumo de ${cliente.nome}`, exact: true }).boundingBox()
    const barra = await abas.boundingBox()
    assert.ok(cartao.x + cartao.width <= barra.x + 1, `cartão (${Math.round(cartao.x + cartao.width)}) à esquerda das abas (${Math.round(barra.x)})`)
    assert.ok(barra.y + barra.height <= medida.base, `abas visíveis sem rolar (${Math.round(barra.y + barra.height)} > ${Math.round(medida.base)})`)
    const telas = medida.scrollHeight / medida.clientHeight
    assert.ok(telas <= TELAS_NO_MASTER[largura] * 0.6, `miolo em ${telas.toFixed(2)} telas; o limite é ${(TELAS_NO_MASTER[largura] * 0.6).toFixed(2)}`)
    await contexto.close()
  })
}

await item('1440: as abas trocam o painel e cada painel mantém a região com o título', async () => {
  const { contexto, ficha } = await abrirFicha(1440, 900, { alteracoes: HISTORICO })
  const aba = (nome) => ficha.getByRole('tab', { name: nome })
  assert.equal(await aba(/^Pedidos, 12$/).getAttribute('aria-selected'), 'true', 'Pedidos abre selecionada, com o total do cadastro')
  await ficha.getByRole('region', { name: 'Pedidos', exact: true }).getByText('Valor total R$ 1.480,50 · Costuma pedir Feijoada', { exact: true }).waitFor()
  assert.equal(await ficha.getByRole('region', { name: 'Endereços', exact: true }).count(), 0, 'Endereços só com a aba ativa')
  await aba(/^Endereços, 2$/).click()
  const enderecosNaTela = ficha.getByRole('region', { name: 'Endereços', exact: true })
  assert.equal(await enderecosNaTela.getByRole('listitem').count(), 2)
  await enderecosNaTela.getByRole('button', { name: 'Adicionar endereço', exact: true }).waitFor()
  assert.equal(await ficha.getByRole('region', { name: 'Pedidos', exact: true }).count(), 0, 'um painel por vez')
  await aba(/^Telefones, 3$/).click()
  assert.equal(await ficha.getByRole('region', { name: 'Telefones', exact: true }).getByRole('listitem').count(), 3)
  await aba(/^Conversas$/).click()
  assert.equal(await ficha.getByRole('region', { name: 'Conversas recentes', exact: true }).getByRole('listitem').count(), 5)
  const painel = ficha.getByRole('tabpanel')
  assert.equal(await painel.getAttribute('aria-labelledby'), await aba(/^Conversas$/).getAttribute('id'), 'o painel diz qual aba o rotula')
  // Notas e tags não vão para trás de aba: a restrição fica à vista em qualquer aba.
  await ficha.getByRole('region', { name: 'Tags', exact: true }).getByText('sem lactose', { exact: true }).waitFor()
  // O Histórico do cadastro fica na coluna da direita, depois das abas.
  const cartao = await ficha.getByRole('region', { name: `Resumo de ${cliente.nome}`, exact: true }).boundingBox()
  const historico = await ficha.getByText('Histórico do cadastro', { exact: true }).boundingBox()
  assert.ok(historico.x >= cartao.x + cartao.width, `Histórico na coluna da direita (x ${historico.x}, cartão até ${cartao.x + cartao.width})`)
  // Com a ficha em leitura, Novo pedido é a ação principal; com o formulário aberto, volta a secundário (o principal é o Salvar).
  const fundo = (nome) => ficha.getByRole('button', { name: nome, exact: true }).last().evaluate(el => getComputedStyle(el).backgroundColor)
  assert.notEqual(await fundo('Novo pedido'), await fundo('Fechar'), 'Novo pedido em destaque na leitura')
  await ficha.getByRole('button', { name: 'Editar dados', exact: true }).click()
  await ficha.getByRole('button', { name: 'Salvar cadastro', exact: true }).waitFor()
  assert.equal(await fundo('Novo pedido'), await fundo('Cancelar'), 'Com o formulário aberto, Novo pedido volta a secundário')
  await contexto.close()
})

await item('390: sem rolagem horizontal e com as ações da ficha', async () => {
  const { contexto, page, ficha } = await abrirFicha(390, 844)
  const medida = await miolo(ficha)
  const pagina = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
  await page.screenshot({ path: `${pasta}/ficha-390.png` })
  assert.ok(pagina <= 0, `página sem rolagem horizontal (${pagina}px)`)
  assert.ok(medida.scrollWidth <= medida.clientWidth, `miolo sem rolagem horizontal (${medida.scrollWidth} > ${medida.clientWidth})`)
  for (const nome of ['Editar dados', 'Baixar PDF', 'Fechar', 'Inativar']) assert.equal(await ficha.getByRole('button', { name: nome, exact: true }).count(), 1, nome)
  assert.equal(await ficha.getByRole('tablist', { name: 'Partes da ficha', exact: true }).count(), 1, 'abas no celular')
  await contexto.close()
})

await browser.close()
if (erros.length) falhas.push(`erros de página: ${erros.slice(0, 3).join(' | ')}`)
if (falhas.length) { console.log(`\n${falhas.length} falha(s)`); process.exit(1) }
console.log('\nFicha do cliente como perfil: duas colunas, abas e 390 aprovados.')
