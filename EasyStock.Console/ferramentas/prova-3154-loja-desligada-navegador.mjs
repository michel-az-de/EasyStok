// #3154 (re-pontuação da #3076): loja online desligada no app real com HTTP controlado, 1440 e 390.
// A. Pedidos: o "Novo pedido" fica desligado e diz antes de abrir que a loja está desligada e quem liga.
// B. A faixa do rodapé diz quem liga, sem "vitrine", e não fica mais alta que os 84 px medidos a 390 com o texto antigo.
// C. Buscar na Casa "loja online" leva a Itens do cardápio.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/3154')
mkdirSync(pasta, { recursive: true })
const URL_CONSOLE = process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'
const AVISO = 'Loja online desligada: sem cardápio. Peça ao suporte do EasyStok para ligar.'

const browser = await chromium.launch({ headless: true })
const erros = []
async function preparar({ largura = 1440, altura = 900, hash = '#/m/cadastros/pedidos' } = {}) {
  const contexto = await browser.newContext({ viewport: { width: largura, height: altura }, isMobile: largura < 700, hasTouch: largura < 700, locale: 'pt-BR', timezoneId: 'America/Sao_Paulo', reducedMotion: 'reduce' })
  await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa', nome: 'Loja desligada' }, usuario: { id: 'dona', nome: 'Dona de teste' } })))
  const acoes = Object.fromEntries(['verPedidos', 'atenderConversas', 'editarAtendimento', 'verCadastrosProdutos', 'verCadastrosFornecedores', 'verCadastrosFinanceiros'].map((n) => [n, true]))
  await contexto.route('**/api/**', async (route) => {
    const req = route.request(), url = new URL(req.url()), path = url.pathname
    if (!path.startsWith('/api/')) return route.continue() // módulos do Vite em src/infra/api/
    const ok = (data, extra = {}) => route.fulfill({ json: { data, ...extra } })
    assert.equal(req.method(), 'GET', `Escrita inesperada: ${req.method()} ${path}`)
    if (path === '/api/auth/me/modulos') return ok({ modulos: ['atendimento', 'cadastros', 'cardapio', 'financeiro'].map((id) => ({ id, liberado: true })), acoes })
    if (path === '/api/atendimento/configuracao') return ok({ slaRespostaMinutos: 5 })
    if (path === '/api/atendimento/agenda') return ok({ lojaDisponivel: false, dias: [] })
    // #2598: sem loja online a leitura responde 200 vazio com meta.lojaDisponivel=false.
    if (path.startsWith('/api/atendimento/comanda/cardapio')) return ok({ itens: [], porcoes: {} }, { meta: { lojaDisponivel: false } })
    if (path === '/api/notificacoes/badge') return ok({ count: 0 })
    return ok([], { meta: { totalCount: 0, total: 0, page: 1, limit: 20 } })
  })
  const page = await contexto.newPage()
  page.setDefaultTimeout(12000)
  page.on('pageerror', (e) => erros.push(e.message))
  page.on('console', (m) => { if (m.type() === 'error' && !m.text().startsWith('Failed to load resource')) erros.push('console: ' + m.text()) })
  await page.goto(`${URL_CONSOLE}/${hash}`)
  return { contexto, page }
}

const falhas = []
const item = async (nome, corpo) => {
  try { await corpo(); console.log(`ok    ${nome}`) } catch (e) { falhas.push(`${nome}: ${e.message.split('\n')[0]}`); console.log(`FALHA ${nome}: ${e.message.split('\n')[0]}`) }
}
const altura = (largura) => (largura === 390 ? 844 : 900)

for (const largura of [1440, 390]) {
  await item(`A ${largura}: Novo pedido desligado diz o motivo antes de abrir`, async () => {
    const { contexto, page } = await preparar({ largura, altura: altura(largura) })
    const novo = page.getByRole('button', { name: 'Novo pedido' })
    await novo.waitFor()
    await page.waitForFunction(() => [...document.querySelectorAll('button')].some((b) => b.textContent.includes('Novo pedido') && b.disabled))
    assert.equal(await novo.getAttribute('title'), AVISO, 'title')
    const descrito = await novo.getAttribute('aria-describedby')
    assert.equal(await page.locator(`[id="${descrito}"]`).innerText(), AVISO, 'motivo visível e no aria-describedby')
    await page.screenshot({ path: `${pasta}/pedidos-${largura}.png` })
    await contexto.close()
  })

  await item(`B ${largura}: a faixa diz quem liga e não cresce`, async () => {
    const { contexto, page } = await preparar({ largura, altura: altura(largura) })
    const faixa = page.getByText(AVISO, { exact: true }).last()
    await faixa.waitFor()
    assert.equal(await page.getByText(/vitrine/i).count(), 0, 'sem "vitrine" na tela')
    const alturaFaixa = await faixa.evaluate((el) => {
      let no = el
      while (no && no !== document.body && !/(^|_)faixa_/.test(String(no.className))) no = no.parentElement
      return Math.round((no && no !== document.body ? no : el).getBoundingClientRect().height)
    })
    console.log(`      altura da faixa a ${largura}: ${alturaFaixa} px`)
    assert.ok(alturaFaixa <= 84, `altura ${alturaFaixa} px (texto antigo: 84 px a 390)`)
    await contexto.close()
  })

  await item(`C ${largura}: Buscar na Casa "loja online" leva a Itens do cardápio`, async () => {
    const { contexto, page } = await preparar({ largura, altura: altura(largura) })
    await page.getByRole('button', { name: 'Buscar na Casa' }).first().click()
    const dialogo = page.getByRole('dialog')
    await dialogo.locator('input').first().fill('loja online')
    await dialogo.getByText('Itens do cardápio').first().waitFor()
    assert.equal(await dialogo.getByText('Nenhum resultado nas categorias consultadas.').count(), 0)
    await page.screenshot({ path: `${pasta}/busca-${largura}.png` })
    await contexto.close()
  })
}

await browser.close()
assert.deepEqual(erros, [], 'sem erro de página')
assert.equal(falhas.length, 0, `itens que falharam:\n${falhas.join('\n')}`)
console.log(`prova-3154-loja-desligada-navegador: ok (capturas em ${pasta})`)
