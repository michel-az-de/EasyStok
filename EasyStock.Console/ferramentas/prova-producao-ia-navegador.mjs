// Navegador real com APIs controladas; nenhum dado da loja é consultado ou gravado.
/* eslint-disable no-console */
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'C:/rep/EasyStok/design/ux-console/node_modules/playwright')
const origem = process.env.CONSOLE_URL ?? 'http://127.0.0.1:5187'
const evidencias = process.env.EVIDENCIAS_UX ?? 'C:/rep/EasyStok/builds/ux-producao-calculo-ia'
mkdirSync(evidencias, { recursive: true })
const empresaId = '11111111-1111-4111-8111-111111111111'
const produtoId = '33333333-3333-4333-8333-333333333333'
const insumoId = '44444444-4444-4444-8444-444444444444'
const nome = 'Ravioli de teste'
const fonte = { titulo: 'Receita de massa', url: 'https://pt.wikibooks.org/wiki/Livro_de_receitas/Massa', texto: 'Referência controlada para a prova', consultadaEm: '2026-10-11T15:00:00Z' }
const fonteInsumo = { titulo: 'Farinha de trigo', url: 'https://pt.wikipedia.org/wiki/Farinha_de_trigo', texto: 'Referência controlada para a prova', consultadaEm: fonte.consultadaEm }
const sugestao = { rendimento: 12, unidadeRendimento: 'Un', linhas: [{ nome: 'Farinha de trigo', quantidade: 300, unidade: 'G' }], observacoes: 'Confira a referência antes de gravar.', fontes: [fonte] }
const desconhecida = { ...sugestao, rendimento: null, unidadeRendimento: null, linhas: [{ nome: 'Farinha de trigo', quantidade: null, unidade: null }] }
const acoes = { editarProducao: true, editarCardapio: true, gerenciarCaixa: true }
const requests = [], passos = [], errosPagina = [], errosConsole = [], abortos = []
let modoReceita = 'ok', modoInsumo = 'ok', liberarReceita, liberarInsumo
const browser = await chromium.launch({ headless: true })
let page
const salvarRegistro = () => writeFileSync(join(evidencias, 'resultado-navegador.json'), JSON.stringify({ origem, passos, requests, errosPagina, errosConsole, abortos }, null, 2))
const teste = async (descricao, executar) => {
  try { await executar(); passos.push({ descricao, ok: true }); console.log(`ok ${descricao}`) }
  catch (erro) { passos.push({ descricao, ok: false, erro: erro.message }); throw erro }
}
const puts = () => requests.filter((r) => r.metodo === 'PUT')
const editor = () => page.locator('dialog').filter({ has: page.locator('h2', { hasText: /^Receita de Ravioli de teste$/ }) })
const pesquisa = () => page.getByRole('dialog', { name: 'Pesquisar receita com IA', exact: true })
const abrirEditor = async () => {
  await page.goto(origem + '/#/m/producao/receitas')
  await page.getByRole('button', { name: 'Editar receita', exact: true }).click()
  await editor().getByLabel('Rendimento', { exact: true }).waitFor()
}
const abrirPesquisa = async () => {
  await editor().getByRole('button', { name: 'Pesquisar receita com IA', exact: true }).click()
  await pesquisa().getByLabel('Nome para pesquisar', { exact: true }).waitFor()
}
const revisarSugestao = () => pesquisa().getByRole('region', { name: 'Receita sugerida', exact: true })
const iniciarPesquisa = async () => {
  await pesquisa().getByRole('button', { name: 'Pesquisar nas fontes públicas', exact: true }).click()
}
const rascunho = async () => ({
  rendimento: await editor().getByLabel('Rendimento', { exact: true }).inputValue(),
  quantidade: await editor().getByLabel('Quantidade', { exact: true }).inputValue(),
  unidade: await editor().getByLabel('Unidade', { exact: true }).inputValue(),
  unidadeRendimento: await editor().getByLabel('Rende em', { exact: true }).inputValue(),
})
try {
  const context = await browser.newContext({ viewport: { width: 1440, height: 960 }, reducedMotion: 'reduce' })
  await context.addInitScript(({ empresaId }) => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'teste-ui-controlado', expiraEm: Date.now() + 3600000, empresa: { id: empresaId, nome: 'Empresa de teste' }, usuario: { id: '22222222-2222-4222-8222-222222222222', nome: 'Dona de teste' } })), { empresaId })
  await context.route('**/api/**', async (route) => {
    const request = route.request(), url = new URL(request.url()), caminho = url.pathname, metodo = request.method()
    if (!caminho.startsWith('/api/')) return route.continue()
    requests.push({ caminho, metodo, corpo: metodo === 'GET' ? null : request.postDataJSON(), texto: url.searchParams.get('texto') })
    const ok = (data, meta = {}) => route.fulfill({ json: { data, meta } })
    if (caminho === '/api/auth/me/modulos') return ok({ modulos: ['cardapio', 'producao', 'atendimento', 'cozinha', 'financeiro', 'campanhas', 'configuracoes', 'entregas'].map((id) => ({ id, liberado: true })), acoes })
    if (caminho === '/api/atendimento/producao/receitas') return ok([{ cardapioItemId: 'sku-teste', produtoId, nome, rendimentoBase: 10, rendimentoUnidade: 'Un', linhas: 1, custoTotal: 1, custoPorRendimento: 0.1, baixaAutomatica: false }])
    if (caminho === `/api/atendimento/producao/receitas/${produtoId}`) return ok({ produtoId, nome, rendimentoBase: 10, rendimentoUnidade: 'Un', unidadeMedidaBase: 'Un', linhas: [{ insumoId, insumo: 'Farinha de trigo', quantidade: 100, unidade: 'G', custo: 1, custoReferencia: 0.01, unidadeDoInsumo: 'G' }], custoTotal: 1, custoPorRendimento: 0.1 })
    if (caminho === '/api/atendimento/producao/insumos') return ok([{ produtoId: insumoId, nome: 'Farinha de trigo', unidade: 'G', saldo: 1000, minimo: 100, custo: 0.01, receitas: 1 }])
    if (caminho === `/api/produtos/${produtoId}/composicao` && metodo === 'PUT') return route.fulfill({ status: 204, body: '' })
    if (caminho === '/api/atendimento/producao/sugerir-receita') {
      if (modoReceita === 'erro') return route.fulfill({ status: 503, json: { error: { message: 'Pesquisa controlada indisponível' } } })
      if (modoReceita === 'pendente') return new Promise((resolve) => { liberarReceita = async () => { await ok(sugestao).catch(() => {}); resolve() } })
      return ok(modoReceita === 'desconhecida' ? desconhecida : modoReceita === 'longa' ? { ...sugestao, linhas: Array.from({ length: 30 }, (_, i) => ({ nome: `Ingrediente de referência ${i + 1}`, quantidade: null, unidade: null })) } : sugestao)
    }
    if (caminho === '/api/atendimento/producao/pesquisa') {
      if (modoInsumo === 'erro') return route.fulfill({ status: 503, json: { error: { message: 'Fonte controlada indisponível' } } })
      if (modoInsumo === 'pendente') return new Promise((resolve) => { liberarInsumo = async () => { await ok([{ ...fonteInsumo, titulo: 'Resposta antiga' }]).catch(() => {}); resolve() } })
      return ok([fonteInsumo])
    }
    if (caminho === '/api/atendimento/producao/calcular') return ok({ produtoFinalId: produtoId, produtoFinalNome: nome, quantidadeDesejada: 0.0001, unidadeDesejada: 'Kg', fatorMultiplicador: 0.0001, custoEstimadoTotal: null, tudoDisponivel: false, insumos: [{ insumoId, insumoNome: 'Farinha de trigo', quantidadeNecessaria: 0.0001, unidadeReceita: 'G', saldoAtual: 0, unidadeSaldo: 'G', falta: 0.0001, custoEstimadoLinha: null, conversaoFalhou: false }] })
    if (metodo !== 'GET') return route.fulfill({ status: 400, json: { error: { message: `Escrita inesperada na prova: ${caminho}` } } })
    if (/nao-lidas|count/.test(caminho)) return ok({ total: 0 })
    if (caminho === '/api/atendimento/configuracao') return ok({})
    return ok([])
  })
  page = await context.newPage()
  page.setDefaultTimeout(8000)
  page.on('pageerror', (erro) => errosPagina.push(erro.message))
  page.on('requestfailed', (request) => abortos.push({ caminho: new URL(request.url()).pathname, erro: request.failure()?.errorText }))
  page.on('console', (mensagem) => { if (mensagem.type() === 'error') errosConsole.push({ texto: mensagem.text(), esperada: /Failed to load resource: the server responded with a status of 503/.test(mensagem.text()) }) })
  await teste('custo do rascunho acompanha quantidade e rendimento', async () => {
    await abrirEditor()
    await editor().getByLabel('Quantidade', { exact: true }).fill('200')
    await editor().getByLabel('Rendimento', { exact: true }).fill('20')
    const custos = editor().getByRole('region', { name: 'Custo estimado do rascunho' })
    await custos.getByText(/2,00/).waitFor()
    assert.match(await custos.innerText(), /0,10/)
    assert.equal(puts().length, 0)
  })
  const anterior = await rascunho()
  await teste('pesquisa por botão e Enter não grava; Escape só fecha filha e devolve foco', async () => {
    await abrirPesquisa(); await iniciarPesquisa(); await revisarSugestao().waitFor()
    assert.equal(await revisarSugestao().getByRole('link', { name: fonte.titulo }).getAttribute('href'), fonte.url)
    assert.equal(puts().length, 0)
    await pesquisa().getByLabel('Nome para pesquisar').press('Enter')
    await revisarSugestao().waitFor()
    assert.equal(puts().length, 0)
    await page.keyboard.press('Escape'); await pesquisa().waitFor({ state: 'hidden' })
    assert.equal(await editor().isVisible(), true)
    assert.deepEqual(await rascunho(), anterior)
    assert.equal(await editor().getByRole('button', { name: 'Pesquisar receita com IA' }).evaluate((no) => no === document.activeElement), true)
  })
  await teste('503, nova tentativa e cancelamento conservam o rascunho', async () => {
    modoReceita = 'erro'; await abrirPesquisa(); await iniciarPesquisa()
    await pesquisa().getByRole('alert').filter({ hasText: 'Pesquisa controlada indisponível' }).waitFor()
    assert.deepEqual(await rascunho(), anterior)
    modoReceita = 'ok'; await iniciarPesquisa(); await revisarSugestao().waitFor()
    await pesquisa().getByRole('button', { name: 'Cancelar', exact: true }).click()
    assert.deepEqual(await rascunho(), anterior); assert.equal(puts().length, 0)
  })
  await teste('fechar pesquisa pendente aborta e a resposta tardia não altera o editor', async () => {
    modoReceita = 'pendente'; await abrirPesquisa(); await iniciarPesquisa()
    await page.waitForFunction(() => !!document.querySelector('dialog[open] button[form]:disabled'))
    for (let i = 0; i < 100 && !liberarReceita; i += 1) await page.waitForTimeout(10)
    assert.equal(typeof liberarReceita, 'function')
    const abortou = page.waitForEvent('requestfailed', { predicate: (request) => new URL(request.url()).pathname.endsWith('/sugerir-receita') })
    await page.keyboard.press('Escape'); await pesquisa().waitFor({ state: 'hidden' })
    assert.match((await abortou).failure().errorText, /abort/i)
    await liberarReceita(); modoReceita = 'ok'
    await page.waitForTimeout(100)
    assert.deepEqual(await rascunho(), anterior); assert.equal(puts().length, 0)
  })
  await teste('aplicação explícita mantém medidas desconhecidas e exige revisão antes de um único PUT', async () => {
    modoReceita = 'desconhecida'; await abrirPesquisa(); await iniciarPesquisa(); await revisarSugestao().waitFor()
    assert.match(await revisarSugestao().innerText(), /não informado pela fonte/)
    assert.match(await revisarSugestao().innerText(), /quantidade a conferir/)
    await pesquisa().getByRole('button', { name: 'Aplicar à receita para revisar', exact: true }).click()
    assert.deepEqual(await rascunho(), { rendimento: '', quantidade: '', unidade: '', unidadeRendimento: '' })
    assert.equal(puts().length, 0)
    const salvar = editor().getByRole('button', { name: 'Salvar receita', exact: true })
    await salvar.click(); await editor().getByRole('alert').filter({ hasText: 'Diga quanto a receita rende' }).waitFor()
    await editor().getByLabel('Rendimento', { exact: true }).fill('10')
    await salvar.click(); await editor().getByRole('alert').filter({ hasText: 'Confira a unidade do rendimento' }).waitFor()
    await editor().getByLabel('Rende em', { exact: true }).selectOption('Un')
    await salvar.click(); await editor().getByRole('alert').filter({ hasText: 'Quantidade de cada insumo' }).waitFor()
    await editor().getByLabel('Quantidade', { exact: true }).fill('100')
    await salvar.click(); await editor().getByRole('alert').filter({ hasText: 'Confira a unidade de cada insumo' }).waitFor()
    assert.equal(puts().length, 0)
    await editor().getByLabel('Unidade', { exact: true }).selectOption('G')
    await salvar.click(); await editor().waitFor({ state: 'hidden' })
    assert.equal(puts().length, 1)
    assert.equal(puts()[0].corpo.rendimentoUnidade, 'Un'); assert.equal(puts()[0].corpo.linhas[0].quantidade, 100)
  })
  await teste('autocomplete fornece datalist e fonte; atraso e 503 preservam nome e custo manual', async () => {
    await page.goto(origem + '/#/m/producao/insumos')
    await page.getByRole('button', { name: 'Cadastrar insumo', exact: true }).click()
    const cadastro = page.getByRole('dialog', { name: 'Cadastrar insumo', exact: true })
    const campo = cadastro.getByLabel('Nome do insumo', { exact: true })
    await cadastro.getByLabel('Custo por unidade', { exact: true }).fill('9,99')
    await campo.fill('Farinha')
    await cadastro.getByRole('link', { name: fonteInsumo.titulo }).waitFor()
    assert.ok(await campo.getAttribute('list')); assert.equal(await cadastro.locator('datalist option').first().getAttribute('value'), fonteInsumo.titulo)
    assert.equal(await cadastro.getByRole('link', { name: fonteInsumo.titulo }).getAttribute('href'), fonteInsumo.url)
    modoInsumo = 'pendente'; await campo.fill('Farinha antiga')
    await page.waitForRequest((r) => new URL(r.url()).searchParams.get('texto') === 'Farinha antiga')
    for (let i = 0; i < 100 && !liberarInsumo; i += 1) await page.waitForTimeout(10)
    assert.equal(typeof liberarInsumo, 'function')
    modoInsumo = 'ok'; await campo.fill('Farinha atual')
    await cadastro.getByRole('link', { name: fonteInsumo.titulo }).waitFor()
    await liberarInsumo(); await page.waitForTimeout(100)
    assert.equal(await campo.inputValue(), 'Farinha atual')
    assert.equal(await cadastro.getByText('Resposta antiga', { exact: true }).count(), 0)
    assert.equal(await cadastro.getByLabel('Custo por unidade', { exact: true }).inputValue(), '9,99')
    modoInsumo = 'erro'; await campo.fill('Nome sem fonte')
    await cadastro.getByRole('status').filter({ hasText: 'Pesquisa indisponível' }).waitFor()
    assert.equal(await campo.isEnabled(), true); assert.equal(await cadastro.getByRole('button', { name: 'Cadastrar insumo', exact: true }).isEnabled(), true)
    await campo.fill('Cadastro manual permitido'); assert.equal(await campo.inputValue(), 'Cadastro manual permitido')
    await page.keyboard.press('Escape'); modoInsumo = 'ok'
  })
  await teste('calculadora é somente simulação e conserva precisão 0,0001', async () => {
    await page.goto(origem + '/#/m/producao/calculadora')
    await page.getByLabel('Receita', { exact: true }).selectOption(produtoId)
    await page.getByLabel('Quantidade desejada', { exact: true }).fill('0,0001')
    await page.getByLabel('Unidade desejada', { exact: true }).selectOption('Kg')
    await page.getByRole('button', { name: 'Calcular insumos e custo', exact: true }).click()
    const calculo = page.getByRole('dialog', { name: `Cálculo de ${nome}`, exact: true })
    await calculo.waitFor(); assert.match(await calculo.innerText(), /0,0001 Kg · multiplicador 0,0001/)
    assert.match(await calculo.innerText(), /Precisa 0,0001 G/)
    assert.match(await calculo.innerText(), /Falta 0,0001 G/)
    assert.match(await calculo.innerText(), /Custo incompleto/)
    assert.equal(requests.filter((r) => r.caminho.endsWith('/calcular')).length, 1)
    assert.equal(puts().length, 1)
    await page.screenshot({ path: join(evidencias, 'calculadora-desktop.png') })
    await page.keyboard.press('Escape')
  })
  await teste('390 px e tema escuro: modal longa sem overflow e rodapé acessível', async () => {
    await page.setViewportSize({ width: 390, height: 844 })
    await page.evaluate(() => localStorage.setItem('casa-da-baba:tema', 'dark'))
    await page.reload()
    assert.equal(await page.locator('html').getAttribute('data-theme'), 'dark')
    await abrirEditor(); modoReceita = 'longa'; await abrirPesquisa(); await iniciarPesquisa(); await revisarSugestao().waitFor()
    const aplicar = pesquisa().getByRole('button', { name: 'Aplicar à receita para revisar', exact: true })
    const caixa = await aplicar.boundingBox()
    assert.ok(caixa && caixa.y >= 0 && caixa.y + caixa.height <= 844, 'rodapé fica dentro da área visível')
    assert.equal(await pesquisa().evaluate((no) => no.scrollWidth <= no.clientWidth + 1), true)
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true)
    await page.screenshot({ path: join(evidencias, 'pesquisa-celular-escuro.png') })
    await page.keyboard.press('Escape'); assert.equal(await editor().isVisible(), true)
    await page.screenshot({ path: join(evidencias, 'receita-celular-escuro.png') })
    await page.keyboard.press('Escape'); modoReceita = 'ok'
  })
  await teste('perfil somente leitura não mostra IA, cadastro ou inclusão de insumo', async () => {
    acoes.editarProducao = false
    await page.goto(origem + '/#/m/producao/receitas'); await page.reload()
    await page.getByRole('button', { name: 'Ver receita', exact: true }).click()
    await editor().waitFor()
    assert.equal(await editor().getByRole('button', { name: 'Pesquisar receita com IA' }).count(), 0)
    assert.equal(await editor().getByRole('button', { name: 'Mais um insumo' }).count(), 0)
    assert.equal(await editor().getByRole('button', { name: 'Salvar receita' }).count(), 0)
    await page.keyboard.press('Escape')
    await page.goto(origem + '/#/m/producao/insumos')
    await page.getByText('O cadastro e os ajustes ficam com a dona ou gerente.', { exact: false }).waitFor()
    assert.equal(await page.getByRole('button', { name: 'Cadastrar insumo', exact: true }).count(), 0)
  })
  assert.deepEqual(errosPagina, [])
  assert.deepEqual(errosConsole.filter((erro) => !erro.esperada), [])
  assert.deepEqual(requests.filter((r) => r.metodo !== 'GET').map((r) => r.caminho).filter((c) => !c.endsWith('/sugerir-receita') && !c.endsWith('/calcular') && c !== `/api/produtos/${produtoId}/composicao`), [])
  salvarRegistro()
  console.log(`OK: ${passos.length} cenários no Chromium; 1 PUT manual; simulação e pesquisa sem gravação; sem erros inesperados de página ou console.`)
} catch (erro) {
  salvarRegistro()
  if (page) {
    await page.screenshot({ path: join(evidencias, 'falha-producao-ia.png') }).catch(() => {})
    console.error((await page.locator('body').innerText()).slice(0, 1200))
  }
  throw erro
} finally { await browser.close() }
