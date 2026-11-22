// #3152 (achados da re-pontuação da #3076): Cadastros no app real com HTTP controlado, 1440 e 390.
// A. Faixas do pé: com a API de pé só o aviso do aparelho (som), na fila e dispensável; conversas fora vira UMA faixa de falha, na fila.
// B. "Salvar cadastro" desligado com o formulário vazio diz o que falta (o nome), visível, no title e no aria-describedby.
// C. Sem título nem "Novo cliente" repetido na mesma tela (cabeçalho e lista vazia, H1 da casca e H2 do painel).
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/3152')
mkdirSync(pasta, { recursive: true })
const URL_CONSOLE = process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'
const HASH = '#/m/cadastros/clientes'
const agora = new Date()

const browser = await chromium.launch({ headless: true })
const erros = []
async function preparar({ largura = 1440, altura = 900, hash = HASH, conversasFalham = false } = {}) {
  const contexto = await browser.newContext({ viewport: { width: largura, height: altura }, isMobile: largura < 700, hasTouch: largura < 700, locale: 'pt-BR', timezoneId: 'America/Sao_Paulo', reducedMotion: 'reduce' })
  await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa', nome: 'Loja dos cadastros' }, usuario: { id: 'dona', nome: 'Dona de teste' } })))
  const acoes = Object.fromEntries(['verPedidos', 'editarAtendimento', 'verCadastrosProdutos', 'verCadastrosFornecedores', 'verCadastrosFinanceiros'].map((n) => [n, true]))
  await contexto.route('**/api/**', async (route) => {
    const req = route.request(), url = new URL(req.url()), path = url.pathname
    if (!path.startsWith('/api/')) return route.continue() // módulos do Vite em src/infra/api/
    const ok = (data, extra = {}) => route.fulfill({ json: { data, ...extra } })
    assert.equal(req.method(), 'GET', `Escrita inesperada: ${req.method()} ${path}`)
    if (path === '/api/auth/me/modulos') return ok({ modulos: ['atendimento', 'cadastros', 'financeiro'].map((id) => ({ id, liberado: true })), acoes })
    if (conversasFalham && path === '/api/atendimento/conversas') return route.fulfill({ status: 500, json: { error: 'fora do ar' } })
    if (path === '/api/atendimento/configuracao') return ok({ slaRespostaMinutos: 5 })
    if (path === '/api/atendimento/agenda') return ok({ lojaDisponivel: true, dias: [] })
    if (path === '/api/atendimento/comanda/cardapio') return ok({ itens: [], porcoes: {} })
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
// Faixas do pé: a do aparelho (som e notificações) e a de falha da sincronização.
const faixasDoPe = (page) => page.evaluate(() => {
  const achadas = []
  for (const el of document.querySelectorAll('div, output')) {
    const r = el.getBoundingClientRect(), cls = String(el.className)
    if (r.height > 0 && /(^|_)(faixa|aparelho)_/.test(cls)) achadas.push({ cls: cls.split(' ')[0], pos: getComputedStyle(el).position, altura: Math.round(r.height) })
  }
  return achadas
})

for (const largura of [1440, 390]) {
  await item(`A1 API de pé, ${largura}: só o aviso do aparelho, na fila, até 52 px, dispensável`, async () => {
    const { contexto, page } = await preparar({ largura, altura: altura(largura) })
    await page.getByText('Som e avisos bloqueados').first().waitFor()
    const faixas = await faixasDoPe(page)
    assert.equal(faixas.length, 1, `uma faixa (veio ${JSON.stringify(faixas)})`)
    assert.notEqual(faixas[0].pos, 'fixed')
    assert.ok(faixas[0].altura <= 52, `altura ${faixas[0].altura}`)
    await page.getByRole('button', { name: 'Dispensar avisos do aparelho' }).click()
    assert.equal((await faixasDoPe(page)).length, 0, 'dispensado, a área útil volta inteira')
    await contexto.close()
  })
  await item(`A2 conversas fora do ar, ${largura}: a falha entra na fila, sem sobrepor o conteúdo`, async () => {
    const { contexto, page } = await preparar({ largura, altura: altura(largura), conversasFalham: true })
    await page.getByText(/^Conversas sem atualização/).first().waitFor()
    const faixas = await faixasDoPe(page)
    assert.ok(faixas.length >= 1 && faixas.every((f) => f.pos !== 'fixed'), `nenhuma fixa (veio ${JSON.stringify(faixas)})`)
    await contexto.close()
  })

  await item(`B ${largura}: Salvar desligado diz o que falta`, async () => {
    const { contexto, page } = await preparar({ largura, altura: altura(largura) })
    await page.getByRole('button', { name: 'Novo cliente' }).first().click()
    const salvar = page.getByRole('button', { name: 'Salvar cadastro' })
    await salvar.waitFor()
    assert.equal(await salvar.isDisabled(), true)
    const esperado = 'Informe o nome para salvar.'
    assert.equal(await salvar.getAttribute('title'), esperado, 'title')
    assert.ok(await page.getByText(esperado, { exact: true }).isVisible(), 'o motivo está visível antes do botão')
    const descrito = await salvar.getAttribute('aria-describedby')
    assert.equal(await page.locator(`[id="${descrito}"]`).innerText(), esperado, 'aria-describedby')
    await page.getByRole('textbox', { name: /^Nome/ }).fill('Ana')
    assert.equal(await salvar.isEnabled(), true, 'com o nome, liga')
    await page.screenshot({ path: `${pasta}/salvar-${largura}.png` })
    await contexto.close()
  })

  for (const [hash, titulo] of [['#/m/cadastros/clientes', 'Leads e clientes'], ['#/m/cadastros/pedidos', 'Pedidos']]) {
    await item(`C1 ${largura} ${titulo}: o título aparece uma vez (a casca mantém o H1 para o leitor de tela)`, async () => {
      const { contexto, page } = await preparar({ largura, altura: altura(largura), hash })
      await page.getByRole('heading', { name: titulo, exact: true }).first().waitFor({ state: 'attached' })
      await page.waitForTimeout(600)
      const visiveis = await page.evaluate((t) => [...document.querySelectorAll('h1, h2')].filter((h) => h.innerText.trim() === t && h.getBoundingClientRect().height > 1).length, titulo)
      assert.equal(visiveis, 1, `"${titulo}" visível ${visiveis} vezes`)
      assert.equal(await page.getByRole('heading', { level: 1, name: titulo, exact: true }).count(), 1, 'H1 continua para o leitor de tela')
      await page.screenshot({ path: `${pasta}/titulo-${titulo.split(' ')[0]}-${largura}.png` })
      await contexto.close()
    })
  }
  await item(`C2 ${largura}: lista vazia de clientes não repete "Novo cliente"`, async () => {
    const { contexto, page } = await preparar({ largura, altura: altura(largura) })
    await page.getByRole('button', { name: 'Novo cliente' }).first().waitFor()
    await page.getByText('Nenhuma pessoa cadastrada').waitFor()
    assert.equal(await page.getByRole('button', { name: 'Novo cliente' }).count(), 1)
    await contexto.close()
  })
}

await browser.close()
assert.deepEqual(erros, [], 'sem erro de página')
assert.equal(falhas.length, 0, `itens que falharam:\n${falhas.join('\n')}`)
console.log(`prova-3152-cadastros-navegador: ok (capturas em ${pasta})`)
