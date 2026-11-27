// #3387: Leads e clientes e a Ficha do cliente redesenhadas. Chromium, Console real, API simulada.
// Em 1440, 1366x768 e 390: a ficha diz quem é, quanto vale, como está e o que fazer agora; a restrição fica no topo;
// o rodapé fica à vista; sem rolagem horizontal; sem erro de página. Grava capturas em .build/3387.
//   CONSOLE_TEST_URL=http://127.0.0.1:5387 node ferramentas/prova-3387-clientes-navegador.mjs [--so-fotos] [--prefixo=antes]
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/3387')
mkdirSync(pasta, { recursive: true })
const URL_CONSOLE = process.env.CONSOLE_TEST_URL ?? 'http://127.0.0.1:5387'
const SO_FOTOS = process.argv.includes('--so-fotos')
const PREFIXO = (process.argv.find(a => a.startsWith('--prefixo=')) ?? '--prefixo=depois').split('=')[1]
const AGORA = Date.parse('2026-11-27T15:00:00-03:00')
const dias = (n) => new Date(AGORA - n * 86400000).toISOString()

const ID = 'b0000001-1111-4111-8111-111111111111'
const cliente = { id: ID, revisao: 1, nome: 'Andreza Apto 21 (Atria)', telefone: '+5511981405122', email: '', documento: '', tipoPessoa: 'fisica', ativo: true, observacoes: '', orderCount: 6, lastOrderAt: dias(9), fotoUrl: null }
const LINHAS = [
  { ...cliente, estagio: 'fiel', emRisco: false, compras: 6, gastoTotal: 642, ticketMedio: 107, ultimaCompraEm: dias(9), criadoEm: '2026-04-02T12:00:00Z', ocorrenciasAbertas: 0, ocorrenciasTotal: 0, notasTotal: 1, ultimaNota: 'Alergia a camarão.', tags: ['sem_lactose'] },
  { id: 'c2', revisao: 1, nome: 'Ana', telefone: '', ativo: true, orderCount: 1, estagio: 'primeira_compra', emRisco: false, compras: 1, gastoTotal: 38, ticketMedio: 38, ultimaCompraEm: dias(140), criadoEm: '2026-07-02T12:00:00Z', tags: [] },
  { id: 'c3', revisao: 1, nome: 'Bruno Carvalho', telefone: '+5511955554444', ativo: true, orderCount: 3, estagio: 'recorrente', emRisco: true, compras: 3, gastoTotal: 315, ticketMedio: 105, ultimaCompraEm: dias(41), criadoEm: '2026-05-10T12:00:00Z', ocorrenciasAbertas: 1, ocorrenciasTotal: 1, tags: [] },
  { id: 'c4', revisao: 1, nome: 'Bhb', telefone: '', ativo: true, orderCount: 0, estagio: 'sem_pedido', compras: 0, gastoTotal: 0, criadoEm: '2026-11-03T12:00:00Z', tags: [] },
  { id: 'c5', revisao: 1, nome: '', telefone: '+555434619500', ativo: true, orderCount: 0, estagio: 'sem_pedido', compras: 0, gastoTotal: 0, criadoEm: '2026-10-21T12:00:00Z', tags: [] },
  { id: 'c6', revisao: 1, nome: 'Carla Mendes', telefone: '+5511944443333', ativo: true, orderCount: 14, estagio: 'fiel', emRisco: false, compras: 14, gastoTotal: 1980, ticketMedio: 141.43, ultimaCompraEm: dias(2), criadoEm: '2026-01-15T12:00:00Z', notasTotal: 2, tags: ['vip', 'mora_perto'] },
]
const GASTO_DA_CARTEIRA = LINHAS.reduce((s, l) => s + (l.gastoTotal ?? 0), 0)
const enderecos = [{ id: 'E1', clienteId: ID, logradouro: 'Rua das Flores', numero: '120', complemento: 'Apto 21', bairro: 'Centro', cidade: 'São Paulo', estado: 'SP', cep: '01001000', padrao: true }]
const telefones = [{ id: 'T1', clienteId: ID, numero: '+5511981405122', whatsapp: true, principal: true }]
const notas = [{ id: 'N1', texto: 'Alergia a camarão. Prefere entrega depois das 18h.', autor: 'Operadora', pedidoId: null, mensagemId: null, criadoEm: dias(20) }]
const itens = [[['Lasanha Bolonhesa Clássica', 1], ['Molho bechamel', 1]], [['Molho bechamel', 2]], [['Lasanha de frango', 1], ['Molho bechamel', 1]], [['Nhoque ao sugo', 2]], [['Molho bechamel', 1], ['Pudim', 1]], [['Lasanha Bolonhesa Clássica', 2]]]
const ultimosPedidos = [9, 23, 37, 52, 66, 80].map((d, i) => ({ id: `P${i + 1}`, status: i === 0 ? 'preparando' : 'entregue', criadoEm: dias(d), total: [90, 70, 115, 96, 121, 150][i], itens: itens[i].map(([nome, quantidade]) => ({ nome, quantidade })) }))
const dossie = {
  cliente: { id: ID, nome: cliente.nome, telefone: cliente.telefone, email: null, observacoes: '', motivoBloqueio: null, fotoUrl: null },
  enderecos, tags: ['sem_lactose', 'mora_perto'].map(tag => ({ tag, origem: 'manual', criadoEm: dias(30) })), notas, ultimosPedidos,
  itemFavorito: { nome: 'Molho bechamel', pedidos: 4, quantidade: 5 }, ultimaCompraEm: dias(9), totalPedidos: 6, bloqueado: false,
  preferencias: { avisosStatusAtivos: true, consentiuMarketing: true, consentimentoEm: dias(200) }, domicilio: [],
  ritmo: { medianaDias: 14, comprasAnalisadas: 6 }, primeiraCompraEm: dias(80),
  conversasRecentes: [{ id: 'C1', canal: 'WhatsApp', situacao: 'Encerrada', iniciadaEm: dias(9), ultimaMensagemEm: dias(9) }], pedidoEmAndamento: ultimosPedidos[0], contatoInformado: null, totalGasto: 642,
}
const pedidos = ultimosPedidos.slice(0, 5).map((p, i) => ({ id: p.id, numeroDoDia: i + 1, dataNumero: p.criadoEm.slice(0, 10), criadoEm: p.criadoEm, status: i === 0 ? 'Preparando' : 'Entregue', total: p.total, clienteNome: cliente.nome }))

const browser = await chromium.launch({ headless: true })
const erros = []
async function abrir(largura, altura) {
  const movel = largura < 700
  const contexto = await browser.newContext({ viewport: { width: largura, height: altura }, isMobile: movel, hasTouch: movel, locale: 'pt-BR', timezoneId: 'America/Sao_Paulo', reducedMotion: 'reduce' })
  await contexto.addInitScript((agora) => {
    const Real = Date; const desvio = agora - Real.now()
    globalThis.Date = class extends Real { constructor(...a) { super(...(a.length ? a : [Real.now() + desvio])) } static now() { return Real.now() + desvio } }
    sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa', nome: 'Casa da Baba' }, usuario: { id: 'dona', nome: 'Dona de teste' } }))
  }, AGORA)
  const acoes = Object.fromEntries(['verPedidos', 'atenderConversas', 'editarAtendimento', 'verCadastrosClientes', 'editarCadastrosClientes', 'verCadastrosProdutos', 'verCadastrosFornecedores', 'verCadastrosFinanceiros', 'criarPedidos'].map((n) => [n, true]))
  await contexto.route('**/api/**', async (route) => {
    const req = route.request(), url = new URL(req.url()), path = url.pathname
    const ok = (data, extra = {}) => route.fulfill({ json: { data, ...extra } })
    if (!path.startsWith('/api/')) return route.continue()
    if (path === '/api/auth/me/modulos') return ok({ modulos: ['atendimento', 'cadastros', 'cardapio', 'financeiro', 'pedidos'].map((id) => ({ id, liberado: true })), acoes })
    if (path === '/api/atendimento/configuracao') return ok({ slaRespostaMinutos: 5 })
    if (path === '/api/atendimento/agenda') return ok({ lojaDisponivel: true, dias: [] })
    if (path === '/api/notificacoes/badge') return ok({ count: 0 })
    if (path === '/api/clientes') return ok(LINHAS, { meta: { total: LINHAS.length, totalCount: LINHAS.length, page: 1, limit: 200, gastoTotalDaCarteira: GASTO_DA_CARTEIRA } })
    if (path === `/api/clientes/${ID}`) return ok({ cliente, enderecos, telefones, documentos: [], alteracoes: [] })
    if (path === `/api/clientes/${ID}/dossie`) return ok(dossie)
    if (path === '/api/pedidos') return ok(pedidos, { meta: { totalCount: 6, total: 6, page: 1, limit: 5 } })
    return ok([], { meta: { totalCount: 0, total: 0, page: 1, limit: 20 } })
  })
  const page = await contexto.newPage()
  page.setDefaultTimeout(12000)
  page.on('pageerror', (e) => erros.push(e.message))
  await page.goto(`${URL_CONSOLE}/#/m/cadastros/clientes`)
  await page.locator('tbody tr').filter({ hasText: 'Carla Mendes' }).waitFor()
  return { contexto, page }
}
async function abrirFicha(page) {
  await page.locator('tbody tr').filter({ hasText: cliente.nome }).getByRole('button', { name: /^Abrir ficha/ }).click()
  const ficha = page.getByRole('dialog', { name: 'Ficha do cliente', exact: true })
  await ficha.getByRole('region', { name: `Resumo de ${cliente.nome}`, exact: true }).waitFor()
  await ficha.getByText('Alergia a camarão. Prefere entrega depois das 18h.', { exact: true }).waitFor()
  await page.waitForTimeout(400)
  return ficha
}
const falhas = []
const item = async (nome, corpo) => {
  try { await corpo(); console.log(`ok    ${nome}`) } catch (e) { const motivo = e.message.split('\n').slice(0, 4).join(' | '); falhas.push(`${nome}: ${motivo}`); console.log(`FALHA ${nome}: ${motivo}`) }
}

for (const [largura, altura] of [[1440, 900], [1366, 768], [390, 844]]) {
  await item(`${largura}x${altura}: lista e ficha`, async () => {
    const { contexto, page } = await abrir(largura, altura)
    await page.screenshot({ path: `${pasta}/${PREFIXO}-lista-${largura}.png` })
    const ficha = await abrirFicha(page)
    await page.screenshot({ path: `${pasta}/${PREFIXO}-ficha-${largura}.png` })
    if (!SO_FOTOS) {
      const transborda = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
      assert.equal(transborda, 0, 'sem rolagem horizontal na página')
      // Quem é, quanto vale, como está e o que fazer: tudo na primeira dobra do miolo.
      const dobra = await ficha.evaluate((d) => {
        const miolo = [...d.querySelectorAll('*')].find(e => ['auto', 'scroll'].includes(getComputedStyle(e).overflowY) && e.clientHeight > 100)
        const base = miolo.getBoundingClientRect().top + miolo.clientHeight
        const topo = (sel) => d.querySelector(sel)?.getBoundingClientRect().top ?? Infinity
        return { restricao: topo('[data-restricao-da-ficha]'), recencia: topo('[data-recencia-da-ficha]'), passo: topo('[data-passo-da-ficha]'), base, rolagemLateral: miolo.scrollWidth - miolo.clientWidth }
      })
      assert.ok(dobra.rolagemLateral <= 1, 'sem rolagem horizontal no modal')
      assert.ok(dobra.restricao < dobra.base, 'a restrição alimentar aparece sem rolar')
      if (largura > 700) {
        assert.ok(dobra.recencia < dobra.base, 'a recência aparece sem rolar')
        assert.ok(dobra.passo < dobra.base, 'o próximo passo aparece sem rolar')
      }
      await ficha.getByText('Sem lactose', { exact: false }).first().waitFor()
      await ficha.getByText(/a cada ~?\d+ dias/).first().waitFor()
      // O total aparece uma vez na ficha.
      assert.equal(await ficha.getByText('R$ 642,00', { exact: true }).count(), 1, 'o total comprado aparece uma vez')
      const rodape = await ficha.getByRole('button', { name: 'Fechar', exact: true }).boundingBox()
      assert.ok(rodape && rodape.y + rodape.height <= altura, 'o rodapé fica à vista')
    }
    await contexto.close()
  })
}

await browser.close()
if (erros.length) falhas.push(`erros de página: ${erros.slice(0, 3).join(' | ')}`)
if (falhas.length) { console.log(`\n${falhas.length} falha(s)`); process.exit(1) }
console.log(`\n#3387 clientes: capturas em ${pasta}`)
