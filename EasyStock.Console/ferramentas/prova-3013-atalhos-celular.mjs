// Prova da #3013 (crítica UX de 20/11, Início no celular): faixa de atalhos dos módulos logo abaixo da busca, com a
// linha de estado do cartão. Comportamento no Início renderizado (Chromium, 390 px, API controlada, nada é gravado):
//   a  a faixa some com texto na busca e no modo Organizar, e volta ao limpar;
//   b  a faixa vem antes do painel do dia (PulsoDoHall) na ordem do DOM;
//   c  cada atalho é link e repete nome, href e linha de estado do cartão do mesmo módulo;
//   d  a faixa tem um atalho por cartão de módulo da grade, e só links.
//
// Roda com: node ferramentas/prova-3013-atalhos-celular.mjs   (sobe o vite da própria árvore; CONSOLE_URL usa um já no ar)
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { spawn } from 'node:child_process'
import { fileURLToPath } from 'node:url'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const raiz = fileURLToPath(new URL('..', import.meta.url))
const catalogo = ['cardapio', 'producao', 'atendimento', 'cozinha', 'financeiro', 'campanhas', 'configuracoes', 'entregas']
const empresa = '11111111-1111-4111-8111-111111111111'
const usuario = '22222222-2222-4222-8222-222222222222'

let servidor = null
let origem = process.env.CONSOLE_URL
if (!origem) {
  origem = 'http://127.0.0.1:5197'
  servidor = spawn(process.execPath, [fileURLToPath(new URL('../node_modules/vite/bin/vite.js', import.meta.url)), '--port', '5197', '--strictPort', '--host', '127.0.0.1'],
    { cwd: raiz, env: { ...process.env, VITE_FONTE_DADOS: 'api' }, stdio: 'ignore' })
  for (let i = 0; i < 120; i++) {
    try { if ((await fetch(origem)).ok) break } catch { /* ainda subindo */ }
    await new Promise((r) => setTimeout(r, 500))
  }
}
const browser = await chromium.launch({ headless: true })
let passou = 0
let falhou = false
try {
  const context = await browser.newContext({ viewport: { width: 390, height: 844 }, reducedMotion: 'reduce', hasTouch: true })
  await context.addInitScript(({ empresa, usuario }) => {
    sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'prova-controlada', expiraEm: Date.now() + 3600000, empresa: { id: empresa }, usuario: { id: usuario, nome: 'Felipe' } }))
  }, { empresa, usuario })
  await context.route('**/api/**', (route) => {
    const caminho = new URL(route.request().url()).pathname
    if (!caminho.startsWith('/api/')) return route.continue()
    const ok = (data) => route.fulfill({ json: { data } })
    if (caminho === '/api/auth/me/modulos') return ok({ modulos: catalogo.map((id) => ({ id, liberado: true })), acoes: {} })
    if (caminho === '/api/atendimento/conversas') return ok([{ id: 'a', situacao: 'Assumida', aguardaResposta: true }, { id: 'b', situacao: 'Assumida', aguardaResposta: true }])
    if (caminho === '/api/kds/pedidos') return ok([{ id: 'a', status: 'aguardando' }, { id: 'b', status: 'preparando' }, { id: 'c', status: 'pronto' }])
    if (caminho === '/api/caixa/dia') return ok({ saldoEsperado: 350, saldoInicial: 300, aberto: true, fechado: false, linhasExtras: [] })
    if (caminho === '/api/atendimento/configuracao') return ok({})
    return ok([])
  })
  const page = await context.newPage()
  const erros = []
  page.on('pageerror', (erro) => erros.push(erro.message))
  await page.goto(origem + '/#/')
  await page.getByRole('heading', { level: 1 }).waitFor()
  const faixa = page.getByRole('navigation', { name: 'Atalhos dos módulos' })
  const confere = async (descricao, fn) => {
    try { await fn(); passou += 1; console.log('ok    ' + descricao) } catch (erro) { falhou = true; console.log('FALHA ' + descricao + '\n      ' + String(erro.message).split('\n')[0]) }
  }

  await confere('d: um atalho por cartão de módulo da grade, todos links, e a faixa aparece no celular', async () => {
    await faixa.waitFor({ state: 'visible', timeout: 8000 })
    const cartoes = await page.locator('li[data-modulo]:has(a)').count()
    assert.ok(cartoes >= 3, `${cartoes} cartões com link na grade`)
    assert.equal(await faixa.locator('li').count(), cartoes, 'um item por cartão')
    assert.equal(await faixa.locator('a[href]').count(), cartoes, 'todo item é link')
    assert.equal(await faixa.locator('li > *').count(), cartoes, 'o item só traz o link')
    assert.equal(await faixa.locator('button, input, select').count(), 0, 'só links')
  })

  await confere('b: a faixa vem antes do painel do dia no DOM', async () => {
    const pulso = page.locator('[data-indicador]').first()
    await pulso.waitFor({ timeout: 8000 })
    const antes = await faixa.evaluate((nav, alvo) => Boolean(nav.compareDocumentPosition(alvo) & Node.DOCUMENT_POSITION_FOLLOWING), await pulso.elementHandle())
    assert.equal(antes, true, 'a faixa precede o PulsoDoHall')
  })

  await confere('c: cada atalho repete nome, href e linha de estado do cartão do módulo', async () => {
    await page.locator('li[data-modulo] span[data-atencao]').first().waitFor({ timeout: 8000 })
    const ler = (alvo) => alvo.evaluateAll((itens) => itens.map((item) => {
      const spans = [...item.querySelectorAll('span')].filter((s) => !s.closest('[aria-hidden="true"]') && s.children.length === 0)
      const estado = item.querySelector('span[data-atencao]') ?? spans.find((s) => /pendencia|estado/.test(s.className))
      return { nome: spans[0]?.textContent.trim(), href: item.querySelector('a').getAttribute('href'), estado: estado?.textContent ?? null, atencao: estado?.hasAttribute('data-atencao') ?? false }
    }))
    const atalhos = await ler(faixa.locator('li'))
    const cartoes = await ler(page.locator('li[data-modulo]:has(a)'))
    assert.equal(atalhos.length, cartoes.length)
    assert.ok(cartoes.some((c) => c.estado), 'há ao menos uma linha de estado lida')
    assert.ok(cartoes.some((c) => c.atencao), 'há ao menos um estado com atenção')
    for (let i = 0; i < cartoes.length; i++) assert.deepEqual(atalhos[i], cartoes[i], `atalho ${i} = cartão ${i}`)
  })

  await confere('a: a faixa some com texto na busca e volta ao limpar', async () => {
    const busca = page.getByLabel('Buscar módulo ou tela')
    await busca.fill('caixa')
    await page.getByRole('region', { name: 'Resultados da busca' }).waitFor({ timeout: 8000 })
    assert.equal(await faixa.count(), 0, 'sem faixa durante a busca')
    await busca.fill('')
    await faixa.waitFor({ state: 'visible', timeout: 8000 })
  })

  await confere('a: a faixa some no modo Organizar e volta ao concluir', async () => {
    await page.getByRole('button', { name: 'Ajustar tela inicial', exact: true }).click()
    await page.getByRole('button', { name: 'Organizar áreas e favoritos' }).click()
    await page.locator('[data-alca-modulo]').first().waitFor({ timeout: 8000 })
    assert.equal(await faixa.count(), 0, 'sem faixa ao organizar')
    await page.getByRole('button', { name: /Concluir organização/ }).click()
    await faixa.waitFor({ state: 'visible', timeout: 8000 })
  })
  await confere('sem erro de página', async () => assert.deepEqual(erros, []))
} finally {
  await browser.close()
  servidor?.kill()
}
if (falhou) { console.log('\nprova reprovada'); process.exit(1) }
console.log(`\n${passou} verificações, todas passaram.`)
