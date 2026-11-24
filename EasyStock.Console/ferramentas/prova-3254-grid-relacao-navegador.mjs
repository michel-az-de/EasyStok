// #3254 (S2 da spec 3237): Cadastros > Clientes mostra a relação do cliente na linha (estágio com alerta Em risco, gasto em % da
// carteira, ticket, última compra, cliente desde, sinais e tags). O estágio é o código da API. API antiga (sem os campos) mostra neutro.
// Dados fictícios. 1920 e 1440 sem rolagem horizontal nem sobreposição; 390 não quebra; carregando, vazio e erro.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/3254')
mkdirSync(pasta, { recursive: true })
const URL_CONSOLE = process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'
const idDe = (n) => `a${String(n).padStart(7, '0')}-1111-4111-8111-111111111111`
// 1: sem pedido; 2: primeira compra; 3: recorrente em risco, reclamação aberta, notas; 4: fiel com mais de 3 tags.
const REL = [
  { estagio: 'sem_pedido', gastoTotal: 0, compras: 0, ticketMedio: null, primeiraCompraEm: null, ultimaCompraEm: null, ocorrenciasAbertas: 0, ocorrenciasTotal: 0, notasTotal: 0, ultimaNota: null, tags: [], emRisco: false },
  { estagio: 'primeira_compra', gastoTotal: 38, compras: 1, ticketMedio: 38, primeiraCompraEm: '2026-11-15T15:00:00Z', ultimaCompraEm: '2026-11-15T15:00:00Z', ocorrenciasAbertas: 0, ocorrenciasTotal: 0, notasTotal: 1, ultimaNota: 'Pede entrega depois das 18h.', tags: ['Instagram'], emRisco: false },
  { estagio: 'recorrente', gastoTotal: 444, compras: 6, ticketMedio: 74, primeiraCompraEm: '2026-01-10T15:00:00Z', ultimaCompraEm: '2026-09-20T15:00:00Z', ocorrenciasAbertas: 1, ocorrenciasTotal: 2, notasTotal: 3, ultimaNota: 'Ligar antes de repor.', tags: ['WhatsApp', 'Restaurante', 'recompra_convidada'], emRisco: true },
  { estagio: 'fiel', gastoTotal: 1020, compras: 12, ticketMedio: 85, primeiraCompraEm: '2025-06-10T15:00:00Z', ultimaCompraEm: '2026-11-20T15:00:00Z', ocorrenciasAbertas: 0, ocorrenciasTotal: 0, notasTotal: 0, ultimaNota: null, tags: ['Fiel', 'Instagram', 'Evento', 'Atacado'], emRisco: false },
]
const base = (n) => ({ id: idDe(n), revisao: 1, nome: `Cliente Ficticio ${String(n).padStart(2, '0')}`, telefone: `1191111${String(1000 + n)}`, email: '', documento: '', tipoPessoa: 'fisica', ativo: true, orderCount: REL[n - 1].compras })
const comRelacao = REL.map((r, i) => ({ ...base(i + 1), ...r }))
const semRelacao = REL.map((r, i) => base(i + 1))
const browser = await chromium.launch({ headless: true })
const erros = []
let ultima = null

async function preparar(largura, clientes, meta = {}, atraso = 0) {
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
    if (path === '/api/clientes' && clientes === 'erro') return route.fulfill({ status: 500, json: { error: { message: 'Falha de teste' } } })
    if (path === '/api/clientes') { if (atraso) await new Promise((r) => setTimeout(r, atraso)); return ok(clientes, { meta: { total: clientes.length, totalCount: clientes.length, page: 1, limit: 20, ...meta } }) }
    return ok([], { meta: { totalCount: 0, total: 0, page: 1, limit: 20 } })
  })
  const page = await contexto.newPage()
  page.setDefaultTimeout(12000)
  page.on('pageerror', (e) => erros.push(e.message)); ultima = page
  await page.goto(`${URL_CONSOLE}/#/m/cadastros/clientes`)
  return { contexto, page }
}
const linha = (page, n) => page.locator('tbody tr').filter({ hasText: `Cliente Ficticio ${String(n).padStart(2, '0')}` })
const falhas = []
const item = async (nome, corpo) => {
  try { await corpo(); console.log(`ok    ${nome}`) } catch (e) { const motivo = e.message.split('\n').slice(0, 4).join(' | ') + (process.env.DEPURAR ? ' || ' + (await ultima?.innerText('body').catch(() => '')).slice(0, 500) + ' || ' + erros.slice(0, 3).join(' ; ') : ''); falhas.push(`${nome}: ${motivo}`); console.log(`FALHA ${nome}: ${motivo}`) }
}
// Sem rolagem lateral e sem célula que transborda para a vizinha.
const medida = (page) => page.evaluate(() => ({
  rolagem: document.documentElement.scrollWidth - document.documentElement.clientWidth,
  ruins: [...document.querySelectorAll('tbody td')].filter((td) => td.getClientRects().length && td.scrollWidth > td.clientWidth + 1).length,
}))

for (const largura of [1920, 1440, 390]) {
  await item(`${largura}: estágio, gasto em % da carteira, ticket, última compra, sinais e tags`, async () => {
    const { contexto, page } = await preparar(largura, comRelacao, { gastoTotalDaCarteira: 2000 })
    await page.getByText('Cliente Ficticio 01', { exact: true }).waitFor()
    assert.equal(await linha(page, 1).getByText('Sem pedido ainda', { exact: true }).count(), 1, 'selo de sem pedido segue na linha')
    assert.equal(await page.getByText('Em risco', { exact: true }).count(), 1, 'só o cliente em risco tem o alerta')
    assert.equal(await linha(page, 3).getByText('Em risco', { exact: true }).count(), 1)
    assert.equal(await linha(page, 3).getByRole('img', { name: 'Estágio 3 de 4: Recorrente' }).count(), 1, 'barra de estágio vem do código da API')
    assert.equal(await linha(page, 4).getByRole('img', { name: 'Estágio 4 de 4: Fiel' }).count(), 1)
    for (const botao of ['Abrir ficha de', 'Novo pedido de']) assert.equal(await page.getByRole('button', { name: `${botao} Cliente Ficticio 03` }).count(), 1, `ação ${botao} segue`)
    if (largura > 700) {
      const texto = await linha(page, 4).innerText()
      assert.match(texto, /R\$\s*1\.020,00/, 'gasto')
      assert.match(texto, /51% da carteira/, 'gasto em % da carteira (1020 de 2000)')
      assert.match(texto, /R\$\s*85,00/, 'ticket')
      assert.match(texto, /há \d+ dias?|hoje/, 'última compra')
      const tres = await linha(page, 3).innerText()
      assert.match(tres, /Reclamação/, 'sinal de reclamação')
      assert.match(tres, /3 notas/, 'sinal de notas')
      assert.doesNotMatch(tres, /recompra_convidada/, 'tag interna não aparece')
      assert.equal(await linha(page, 4).locator('ul li').count(), 3, 'até 3 tags')
    }
    const { rolagem, ruins } = await medida(page)
    assert.ok(rolagem <= 0, `sem rolagem horizontal (${rolagem}px)`)
    assert.equal(ruins, 0, 'nenhuma célula transborda para a vizinha')
    await page.screenshot({ path: `${pasta}/grid-${largura}.png` })
    await contexto.close()
  })
  await item(`${largura}: API antiga (sem os campos da relação) mostra neutro e não quebra`, async () => {
    const { contexto, page } = await preparar(largura, semRelacao)
    await page.getByText('Cliente Ficticio 02', { exact: true }).waitFor()
    assert.equal(await page.getByText('Em risco', { exact: true }).count(), 0)
    if (largura > 700) assert.ok(await linha(page, 2).getByRole('img', { name: 'Sem dado' }).count() >= 5, 'traço no lugar do dado ausente')
    const { rolagem } = await medida(page)
    assert.ok(rolagem <= 0, `sem rolagem horizontal (${rolagem}px)`)
    await contexto.close()
  })
  await item(`${largura}: carregando, vazio e erro`, async () => {
    let c = await preparar(largura, comRelacao, {}, 2500)
    await c.page.getByText('Carregando cadastros…').first().waitFor()
    await c.contexto.close()
    c = await preparar(largura, [])
    await c.page.getByText('Nenhuma pessoa cadastrada', { exact: true }).waitFor()
    await c.contexto.close()
    c = await preparar(largura, 'erro')
    await c.page.getByRole('alert').first().waitFor()
    await c.contexto.close()
  })
}
await browser.close()
if (erros.length) falhas.push(`erros de página: ${erros.slice(0, 3).join(' | ')}`)
if (falhas.length) { console.log(`\n${falhas.length} falha(s)`); process.exit(1) }
console.log('\nGrid de clientes com a relação aprovado em 1920, 1440 e 390.')
