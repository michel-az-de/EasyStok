// #3281: a linha de Cadastros > Leads e clientes enxuta, depois da crítica de 24/11/2026. Dados fictícios.
// 6 colunas (Nome, Estágio, Gasto, Última compra, Sinais, Ações); o nome abre a ficha e é o alvo do teclado; "Novo pedido"
// é a ação da linha; contato sem nome ganha o selo "Sem nome" sem repetir o telefone; legenda do estágio em texto;
// ação com o alvo do token --toque. 1920 e 1440 sem rolagem horizontal; 390 sem rolagem da página.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(process.env.PASTA_PRINTS_3281 ?? resolve(import.meta.dirname, '../.build/3281-linha'))
mkdirSync(pasta, { recursive: true })
const URL_CONSOLE = process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'
const idDe = (n) => `b${String(n).padStart(7, '0')}-1111-4111-8111-111111111111`
const nomeDe = (n) => `Cliente Ficticio ${String(n).padStart(2, '0')}`
const base = { ocorrenciasAbertas: 0, ocorrenciasTotal: 0, notasTotal: 0, ultimaNota: null, tags: [], emRisco: false }
const REL = [
  { ...base, estagio: 'sem_pedido', criadoEm: '2026-10-05T15:00:00Z', gastoTotal: 0, compras: 0, ticketMedio: null, ultimaCompraEm: null },
  { ...base, estagio: 'recorrente', criadoEm: '2025-12-20T15:00:00Z', gastoTotal: 444, compras: 6, ticketMedio: 74, ultimaCompraEm: '2026-09-20T15:00:00Z', ocorrenciasAbertas: 1, ocorrenciasTotal: 1, notasTotal: 3, ultimaNota: 'Ligar antes.', tags: ['whatsapp', 'restaurante'], emRisco: true },
  { ...base, estagio: 'fiel', criadoEm: '2025-06-01T15:00:00Z', gastoTotal: 1020, compras: 12, ticketMedio: 85, ultimaCompraEm: '2026-11-20T15:00:00Z', tags: ['cliente_vip'] },
]
const TELEFONE_SEM_NOME = '11911112222'
const cliente = (n, extra = {}) => ({ id: idDe(n), revisao: 1, nome: nomeDe(n), telefone: `1191111${1000 + n}`, email: '', documento: '', tipoPessoa: 'fisica', ativo: true, orderCount: 0, ...extra })
const clientes = [...REL.map((r, i) => cliente(i + 1, { ...r, orderCount: r.compras })), cliente(4, { ...REL[0], nome: 'Cliente WhatsApp', telefone: TELEFONE_SEM_NOME })]
const browser = await chromium.launch({ headless: true })
const erros = []
const falhas = []

async function preparar(largura) {
  const movel = largura < 700
  const contexto = await browser.newContext({ viewport: { width: largura, height: movel ? 844 : 1080 }, isMobile: movel, hasTouch: movel, locale: 'pt-BR', timezoneId: 'America/Sao_Paulo', reducedMotion: 'reduce' })
  await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa', nome: 'Loja' }, usuario: { id: 'dona', nome: 'Dona de teste' } })))
  const acoes = Object.fromEntries(['verPedidos', 'atenderConversas', 'editarAtendimento', 'verCadastrosClientes', 'verCadastrosProdutos', 'verCadastrosFornecedores', 'verCadastrosFinanceiros'].map((n) => [n, true]))
  await contexto.route('**/api/**', async (route) => {
    const path = new URL(route.request().url()).pathname
    if (!path.startsWith('/api/')) return route.continue()
    const ok = (data, extra = {}) => route.fulfill({ json: { data, ...extra } })
    if (path === '/api/auth/me/modulos') return ok({ modulos: ['atendimento', 'cadastros', 'cardapio', 'financeiro'].map((id) => ({ id, liberado: true })), acoes })
    if (path === '/api/atendimento/configuracao') return ok({ slaRespostaMinutos: 5 })
    if (path === '/api/atendimento/agenda') return ok({ lojaDisponivel: true, dias: [] })
    if (path === '/api/notificacoes/badge') return ok({ count: 0 })
    if (path === '/api/clientes') return ok(clientes, { meta: { total: clientes.length, totalCount: clientes.length, page: 1, limit: 20, gastoTotalDaCarteira: 50000 } })
    return ok([], { meta: { totalCount: 0, total: 0, page: 1, limit: 20 } })
  })
  const page = await contexto.newPage()
  page.setDefaultTimeout(15000)
  page.setDefaultNavigationTimeout(60000)
  page.on('pageerror', (e) => erros.push(e.message))
  await page.goto(`${URL_CONSOLE}/#/m/cadastros/clientes`)
  return { contexto, page }
}
const linha = (page, n) => page.locator('tbody tr').filter({ hasText: nomeDe(n) })
const item = async (nome, corpo) => {
  try { await corpo(); console.log(`ok    ${nome}`) } catch (e) { const motivo = e.message.split('\n').slice(0, 4).join(' | '); falhas.push(`${nome}: ${motivo}`); console.log(`FALHA ${nome}: ${motivo}`) }
}
const rolagem = (page) => page.evaluate(() => {
  const exc = (el) => el.scrollWidth - el.clientWidth
  const tabela = document.querySelector('tbody')?.closest('table')
  return { pagina: exc(document.documentElement), tabela: tabela ? tabela.getBoundingClientRect().width - tabela.parentElement.clientWidth : 0 }
})
const transbordo = (page) => page.evaluate(() => {
  const problemas = []
  document.querySelectorAll('tbody tr').forEach((tr, i) => [...tr.children].filter((td) => td.getClientRects().length).forEach((td, j) => {
    if (td.scrollWidth > td.clientWidth) problemas.push(`linha ${i + 1} célula ${j + 1}: ${td.scrollWidth - td.clientWidth}px além`)
  }))
  return problemas
})

await Promise.all([1920, 1440, 390].map(async (largura) => {
  const largo = largura > 700
  await item(`${largura}: 6 colunas, nome abre a ficha, Novo pedido é a ação, Sem nome, legenda e alvo`, async () => {
    const { contexto, page } = await preparar(largura)
    await linha(page, 3).waitFor()
    const cabecalhos = await page.locator('thead th').evaluateAll((ths) => ths.map((th) => th.textContent.trim()))
    assert.deepEqual(cabecalhos, ['Nome', 'Estágio', 'Gasto', 'Última compra', 'Sinais', 'Ações'], 'seis colunas, na ordem')
    // O nome é o botão da ficha: mesmo nome acessível de antes, e o rótulo visível é o nome.
    const nome = page.getByRole('button', { name: `Abrir ficha de ${nomeDe(2)}`, exact: true })
    assert.equal(await nome.count(), 1, 'um alvo de ficha por linha')
    assert.equal((await nome.textContent()).trim(), nomeDe(2), 'o botão da ficha é o nome')
    assert.equal(await page.locator('tbody').getByRole('button', { name: 'Abrir ficha', exact: true }).count(), 0, 'sem botão "Abrir ficha" solto')
    assert.equal(await page.locator('tbody').getByText('Abrir ficha', { exact: true }).count(), 0, 'sem rótulo "Abrir ficha" na linha')
    assert.equal(await page.locator('tbody').getByRole('button', { name: 'Conversa', exact: true }).count(), 0, 'Conversa saiu da linha')
    assert.equal(await linha(page, 2).getByRole('button', { name: `Novo pedido de ${nomeDe(2)}` }).count(), 1, 'Novo pedido segue')
    // Sem nome: o telefone formatado é o nome, com o selo, e não aparece de novo na linha.
    const semNome = page.locator('tbody tr').filter({ hasText: 'Sem nome' })
    assert.equal(await semNome.count(), 1, 'selo Sem nome no contato sem nome')
    const textoSemNome = await semNome.innerText()
    assert.equal(textoSemNome.match(/\(11\) 91111-2222/g)?.length, 1, 'telefone uma vez só')
    assert.equal(await semNome.getByRole('button', { name: /^Abrir ficha de (\+55 )?\(11\) 91111-2222$/ }).count(), 1, 'nome acessível com o telefone')
    // Ticket e cliente desde dentro das células de gasto e última compra.
    if (largo) {
      const tres = await linha(page, 3).innerText()
      assert.match(tres, /ticket R\$ 85/i, 'ticket junto do gasto')
      assert.match(tres, /desde junho de 2025/i, 'cliente desde junto da última compra')
      assert.match(tres, /cliente vip/, 'tag na coluna Sinais')
    }
    // Legenda em texto, não só em title.
    const legenda = await page.getByText(/Fiel: 5 compras ou mais/).first().innerText()
    assert.match(legenda, /Recorrente: 2 a 4/, 'legenda do estágio')
    assert.match(legenda, /da carteira/, 'legenda do percentual da carteira')
    const alvo = await linha(page, 2).getByRole('button', { name: `Novo pedido de ${nomeDe(2)}` }).boundingBox()
    assert.ok(alvo.height >= (largo ? 40 : 48) - 0.5, `alvo da ação ${alvo.height}px`)
    const m = await rolagem(page)
    assert.ok(m.pagina <= 0, `página sem rolagem horizontal (${m.pagina}px)`)
    if (largo) assert.ok(m.tabela <= 1, `tabela cabe (${m.tabela}px)`)
    if (largo) assert.deepEqual(await transbordo(page), [], 'texto de cada célula cabe nela')
    await page.screenshot({ path: `${pasta}/clientes-${largura}.png` })
    await page.getByRole('tab', { name: 'Recompra', exact: true }).click().catch(() => {})
    await page.screenshot({ path: `${pasta}/recompra-${largura}.png` })
    await contexto.close()
  })
  await item(`${largura}: teclado chega à ficha pelo nome`, async () => {
    const { contexto, page } = await preparar(largura)
    await linha(page, 3).waitFor()
    const nome = page.getByRole('button', { name: `Abrir ficha de ${nomeDe(3)}`, exact: true })
    await nome.focus()
    assert.equal(await nome.evaluate((b) => document.activeElement === b), true, 'o nome recebe foco')
    assert.notEqual(await nome.evaluate((b) => getComputedStyle(b).outlineStyle), 'none', 'foco visível')
    await contexto.close()
  })
}))
await browser.close()
if (erros.length) falhas.push(`erros de página: ${erros.slice(0, 3).join(' | ')}`)
if (falhas.length) { console.log(`\n${falhas.length} falha(s)`); process.exit(1) }
console.log('\nLinha enxuta de Leads e clientes aprovada em 1920, 1440 e 390.')
