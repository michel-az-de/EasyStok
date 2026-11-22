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
  const clientes = [{ id: 'A', revisao: 1, nome: 'Pessoa Lead', telefone: '11911112222', email: '', documento: '', tipoPessoa: 'fisica', ativo: true, orderCount: 0 }, { id: 'B', revisao: 1, nome: 'Pessoa Dois', telefone: '11933334444', email: '', documento: '', tipoPessoa: 'fisica', ativo: true, orderCount: 0 }]
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
    if (path === '/api/clientes/buscar') return ok(clientes)
    if (path === '/api/clientes/A') return ok({ cliente: clientes[0], enderecos: [], telefones: [] })
    if (path === '/api/clientes') return ok(clientes, { meta: { total: 2 } })
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
  try { await corpo(); console.log(`ok    ${nome}`) } catch (e) { const motivo = e.message.split('\n').slice(0, 4).join(' | '); falhas.push(`${nome}: ${motivo}`); console.log(`FALHA ${nome}: ${motivo}`) }
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
    assert.equal(await janela.getByText('Lançar a conta ao salvar', { exact: true }).count(), 1, 'I2: texto que mantém o sentido de Emitir')
    assert.equal(await janela.getByText('Desmarcado, ela fica como rascunho e não entra nas contas a pagar.', { exact: true }).count(), 1, 'I2: apoio diz o que o desmarcado faz')
    assert.equal(await janela.getByText(/Deixar a conta aberta|Emitir a conta/).count(), 0, 'I2: sem o texto que contradiz Já foi paga')
    await page.screenshot({ path: `${pasta}/categoria-${largura}.png` })
    await contexto.close()
  })
  for (const [nome, hash, nomeDoTitulo] of [['Financeiro', '#/m/financeiro/pagar', /Contas a pagar/], ['Cadastros', '#/m/cadastros/clientes', /clientes/i], ['Pedidos', '#/m/cadastros/pedidos', /^Pedidos$/]]) {
    await item(`B ${largura}: título visível de ${nome} é h2 de ${esperado}px`, async () => {
      const { contexto, page } = await preparar({ largura, hash })
      const titulo = page.getByRole('heading', { level: 2, name: nomeDoTitulo }).first(); await titulo.waitFor()
      assert.equal(await tamanhoDo(titulo), esperado, 'font-size do título visível')
      if (nome === 'Financeiro') {
        const visiveis = await page.locator('h1, h2').evaluateAll((els) => els.filter((el) => el.getClientRects().length && el.getBoundingClientRect().width > 1 && !el.classList.contains('sr') && /Contas a pagar/.test(el.textContent)).length)
        assert.equal(visiveis, 1, 'título repetido')
      }
      await page.screenshot({ path: `${pasta}/${nome.toLowerCase()}-${largura}.png` })
      await contexto.close()
    })
  }
  await item(`C/D ${largura}: Sem pedido ainda na lista, na aba e na Ficha; Novo pedido desligado com o motivo mostrado uma vez`, async () => {
    const { contexto, page } = await preparar({ largura, hash: '#/m/cadastros/clientes', lojaDesligada: true })
    await page.getByText('Sem pedido ainda', { exact: true }).first().waitFor()
    assert.equal(await page.getByText('Lead', { exact: true }).count(), 0, 'I3: sem "Lead" visível na lista e nas abas')
    assert.equal(await page.getByRole('tab', { name: 'Leads' }).count(), 0, 'I3: a aba não se chama Leads')
    assert.equal(await page.getByRole('tab', { name: 'Sem pedido', exact: true }).count(), 1, 'I3: aba com o termo do selo')
    assert.match(await page.getByText('Sem pedido ainda', { exact: true }).first().getAttribute('title') ?? '', /ainda não fez nenhum pedido/i, 'selo explica')
    await page.waitForFunction(() => document.querySelectorAll('button[aria-label^="Novo pedido de"]').length === 2)
    for (const nome of ['Pessoa Lead', 'Pessoa Dois']) {
      const novo = page.getByRole('button', { name: `Novo pedido de ${nome}` })
      await page.waitForFunction((n) => [...document.querySelectorAll('button')].some((b) => b.getAttribute('aria-label') === `Novo pedido de ${n}` && b.disabled), nome)
      assert.equal(await novo.getAttribute('title'), AVISO, 'title com o motivo de Pedidos')
      const alvo = await novo.getAttribute('aria-describedby')
      assert.ok(alvo, 'I4: botão ligado ao aviso por aria-describedby')
      assert.equal(await page.locator(`[id="${alvo.split(' ')[0]}"]`).textContent(), AVISO, 'I4: aria-describedby aponta para o motivo')
    }
    assert.equal(await page.getByText(AVISO, { exact: true }).count(), 1, 'I4: o motivo aparece uma vez, com 2 clientes')
    // I1: a Ficha aberta pela lista segue a mesma regra.
    await page.getByRole('button', { name: 'Abrir ficha de Pessoa Lead' }).click()
    const ficha = page.getByRole('dialog'); await ficha.waitFor()
    const naFicha = ficha.getByRole('button', { name: /^(Novo|Continuar) pedido/ })
    await naFicha.waitFor()
    assert.equal(await naFicha.isDisabled(), true, 'I1: Novo pedido da Ficha desligado')
    assert.equal(await naFicha.getAttribute('title'), AVISO, 'I1: Ficha com o motivo')
    assert.equal(await ficha.getByText('Sem pedido ainda', { exact: true }).count() >= 1, true, 'I3: Ficha com o termo do selo')
    assert.equal(await ficha.getByText('Lead', { exact: true }).count(), 0, 'I3: Ficha sem "Lead"')
    await page.screenshot({ path: `${pasta}/clientes-${largura}.png` })
    await contexto.close()
  })
  await item(`D ${largura}: busca do Atendimento com a loja desligada: Novo pedido desligado com o motivo`, async () => {
    const { contexto, page } = await preparar({ largura, hash: '#/m/atendimento/balcao', lojaDesligada: true })
    const busca = page.locator('input[type="search"], input[placeholder*="uscar" i]').first(); await busca.waitFor()
    await busca.fill('Pessoa')
    const novo = page.getByRole('button', { name: 'Novo pedido de Pessoa Lead' }); await novo.waitFor()
    assert.equal(await novo.isDisabled(), true, 'I1: Novo pedido da busca desligado')
    assert.equal(await novo.getAttribute('title'), AVISO, 'I1: busca com o motivo')
    assert.ok(await novo.getAttribute('aria-describedby'), 'I1: motivo acessível')
    await contexto.close()
  })
  assert.equal(chamadas.some((c) => c.startsWith('POST') && c.includes('/do-cliente')), false, `nenhum POST /do-cliente (${largura})`)
}
assert.deepEqual(erros, [], 'sem erro de página')
if (falhas.length) { console.error(`\n${falhas.length} falha(s):\n- ${falhas.join('\n- ')}`); await browser.close(); process.exit(1) }
await browser.close()
console.log('ok prova-3193-consistencia-navegador')
