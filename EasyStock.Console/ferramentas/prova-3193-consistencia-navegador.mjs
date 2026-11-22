// #3193 (re-pontuação da #3076): Console real, HTTP controlado, a 1440 e a 390.
// A. Financeiro > Contas a pagar > Nova conta > Nova categoria: um único "Cancelar" na janela.
// B. O título visível do Financeiro mede o mesmo que o de Cadastros (2rem no computador, 1.5rem no celular) e não repete.
// C. "Emitir a conta ao salvar" não existe mais; o selo diz "Sem pedido ainda" e explica no title.
// D. Com a loja desligada, o "Novo pedido" da linha de Clientes fica desligado com o motivo de Pedidos e não chama a API de conversa.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/3193')
mkdirSync(pasta, { recursive: true })
const URL_CONSOLE = process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'
const AVISO = 'Loja online desligada: sem cardápio. Peça ao suporte do EasyStok para ligar.'
const browser = await chromium.launch({ headless: true })
const erros = []
const chamadas = []

async function preparar({ largura, hash, lojaDesligada = false }) {
  const movel = largura < 700
  const contexto = await browser.newContext({ viewport: { width: largura, height: movel ? 844 : 900 }, isMobile: movel, hasTouch: movel, locale: 'pt-BR', timezoneId: 'America/Sao_Paulo', reducedMotion: 'reduce' })
  await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa', nome: 'Loja' }, usuario: { id: 'dona', nome: 'Dona de teste' } })))
  const acoes = Object.fromEntries(['verPedidos', 'atenderConversas', 'editarAtendimento', 'verCadastrosClientes', 'verCadastrosProdutos', 'verCadastrosFornecedores', 'verCadastrosFinanceiros', 'visualizarContasPagar', 'gerenciarContasPagar', 'gerenciarCategoriasFinanceiras', 'visualizarContasReceber'].map((n) => [n, true]))
  const categorias = [{ id: 'K1', nome: 'Aluguel', tipo: 'Despesa', ativa: true }]
  await contexto.route('**/api/**', async (route) => {
    const req = route.request(), path = new URL(req.url()).pathname
    if (!path.startsWith('/api/')) return route.continue()
    chamadas.push(`${req.method()} ${path}`)
    const ok = (data, extra = {}) => route.fulfill({ json: { data, ...extra } })
    if (path === '/api/auth/me/modulos') return ok({ modulos: ['atendimento', 'cadastros', 'cardapio', 'financeiro'].map((id) => ({ id, liberado: true })), acoes })
    if (path === '/api/atendimento/configuracao') return ok({ slaRespostaMinutos: 5 })
    if (path === '/api/atendimento/agenda') return ok({ lojaDisponivel: !lojaDesligada, dias: [] })
    if (path.startsWith('/api/atendimento/comanda/cardapio')) return ok({ itens: lojaDesligada ? [] : [{ sku: 'a', nome: 'Lasanha', preco: 10 }], porcoes: {} }, { meta: { lojaDisponivel: !lojaDesligada } })
    if (path === '/api/notificacoes/badge') return ok({ count: 0 })
    if (path === '/api/financeiro/categorias') return ok(categorias)
    if (path === '/api/clientes') return ok([{ id: 'A', revisao: 1, nome: 'Pessoa Lead', telefone: '11911112222', email: '', documento: '', tipoPessoa: 'fisica', ativo: true, orderCount: 0 }], { meta: { total: 1 } })
    return ok([], { meta: { totalCount: 0, total: 0, page: 1, limit: 20 } })
  })
  const page = await contexto.newPage()
  page.setDefaultTimeout(12000)
  page.on('pageerror', (e) => erros.push(e.message))
  await page.goto(`${URL_CONSOLE}/${hash}`)
  return { contexto, page }
}
const falhas = []
const item = async (nome, corpo) => {
  try { await corpo(); console.log(`ok    ${nome}`) } catch (e) { falhas.push(`${nome}: ${e.message.split('\n')[0]}`); console.log(`FALHA ${nome}: ${e.message.split('\n')[0]}`) }
}
const tamanhoDo = (alvo) => alvo.evaluate((el) => parseFloat(getComputedStyle(el).fontSize))

for (const largura of [1440, 390]) {
  const esperado = largura === 390 ? 24 : 32
  await item(`A ${largura}: Criar categoria tem um único Cancelar`, async () => {
    const { contexto, page } = await preparar({ largura, hash: '#/m/financeiro/pagar' })
    await page.getByRole('button', { name: 'Nova conta a pagar', exact: true }).first().click()
    const janela = page.getByRole('dialog', { name: 'Nova conta a pagar', exact: true }); await janela.waitFor()
    await janela.getByRole('button', { name: '+ Nova categoria', exact: true }).click()
    await janela.getByRole('button', { name: 'Criar categoria', exact: true }).waitFor()
    assert.equal(await janela.getByRole('button', { name: 'Cancelar', exact: true }).count(), 1, 'botões Cancelar na janela')
    assert.equal(await janela.getByText('Deixar a conta aberta ao salvar').count(), 1, 'C: texto de loja')
    assert.equal(await janela.getByText(/Emitir a conta/).count(), 0, 'C: sem jargão')
    await page.screenshot({ path: `${pasta}/categoria-${largura}.png` })
    await contexto.close()
  })
  await item(`B ${largura}: título do Financeiro mede ${esperado}px, um só visível`, async () => {
    const { contexto, page } = await preparar({ largura, hash: '#/m/financeiro/pagar' })
    const h1 = page.getByRole('heading', { level: 1, name: /Contas a pagar/ }); await h1.waitFor()
    assert.equal(await tamanhoDo(h1), esperado, 'font-size do título visível')
    const visiveis = await page.locator('h1, h2').evaluateAll((els) => els.filter((el) => el.getClientRects().length && el.getBoundingClientRect().width > 1 && !el.classList.contains('sr') && /Contas a pagar/.test(el.textContent)).length)
    assert.equal(visiveis, 1, 'título repetido')
    await page.screenshot({ path: `${pasta}/financeiro-${largura}.png` })
    await contexto.close()
  })
  await item(`B ${largura}: Cadastros mostra o título de ${esperado}px`, async () => {
    const { contexto, page } = await preparar({ largura, hash: '#/m/cadastros/clientes' })
    const h2 = page.getByRole('heading', { level: 2, name: /clientes/i }).first(); await h2.waitFor()
    assert.equal(await tamanhoDo(h2), esperado, 'referência de Cadastros')
    await contexto.close()
  })
  await item(`C/D ${largura}: Lead explicado e Novo pedido da linha desligado com a loja desligada`, async () => {
    const { contexto, page } = await preparar({ largura, hash: '#/m/cadastros/clientes', lojaDesligada: true })
    const selo = page.getByText('Sem pedido ainda', { exact: true }).first(); await selo.waitFor()
    assert.equal(await page.getByText('Lead', { exact: true }).count(), 0, 'sem "Lead" visível na lista')
    assert.match(await selo.getAttribute('title') ?? '', /ainda não fez nenhum pedido/i, 'selo explica')
    const novo = page.getByRole('button', { name: 'Novo pedido de Pessoa Lead' })
    await novo.waitFor()
    await page.waitForFunction(() => [...document.querySelectorAll('button')].some((b) => b.getAttribute('aria-label') === 'Novo pedido de Pessoa Lead' && b.disabled))
    assert.equal(await novo.getAttribute('title'), AVISO, 'title com o motivo de Pedidos')
    assert.equal(await page.getByText(AVISO, { exact: true }).count() >= 1, true, 'motivo visível')
    await novo.click({ force: true }).catch(() => {})
    await page.waitForTimeout(300)
    assert.equal(chamadas.some((c) => c.includes('/do-cliente')), false, 'não chamou a API de conversa')
    await page.screenshot({ path: `${pasta}/clientes-${largura}.png` })
    await contexto.close()
  })
}
assert.deepEqual(erros, [], 'sem erro de página')
if (falhas.length) { console.error(`\n${falhas.length} falha(s):\n- ${falhas.join('\n- ')}`); await browser.close(); process.exit(1) }
await browser.close()
console.log('ok prova-3193-consistencia-navegador')
