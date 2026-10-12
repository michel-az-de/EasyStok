// JS/CSS reais com API controlada, sem alterar os dados da loja.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'
import { join } from 'node:path'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const origem = process.env.CONSOLE_URL ?? 'http://127.0.0.1:5193'
const evidencias = process.env.EVIDENCIAS_UX ?? 'builds/prova-hub'
mkdirSync(evidencias, { recursive: true })
const catalogo = ['cardapio', 'producao', 'atendimento', 'cozinha', 'financeiro', 'campanhas', 'configuracoes', 'entregas']
const empresa = '11111111-1111-4111-8111-111111111111'
const usuario = '22222222-2222-4222-8222-222222222222'
const chave = `easystok.hall.v1:${empresa}:${usuario}`
const browser = await chromium.launch({ headless: true })
let ultimaPagina
async function preparar({ permitidos = catalogo, modo = 'normal', demora = 0, bloqueiaStorage = false, saldoEsperado = 300.03 } = {}) {
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce', hasTouch: true })
  const controle = { modo, demora, chamadas: [] }
  await context.addInitScript(({ empresa, usuario, bloqueiaStorage }) => {
    sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'prova-controlada', expiraEm: Date.now() + 3600000, empresa: { id: empresa }, usuario: { id: usuario, nome: 'Felipe' } }))
    if (bloqueiaStorage) {
      const original = Storage.prototype.setItem
      Storage.prototype.setItem = function (key, value) {
        if (key.startsWith('easystok.hall')) throw new Error('Storage bloqueado na prova')
        return original.call(this, key, value)
      }
    }
  }, { empresa, usuario, bloqueiaStorage })
  await context.route('**/api/**', async (route) => {
    const request = route.request(), url = new URL(request.url()), path = url.pathname
    if (!path.startsWith('/api/')) return route.continue()
    controle.chamadas.push({ path, metodo: request.method() })
    assert.equal(request.method(), 'GET', 'Hub não pode alterar operações')
    const ok = (data) => route.fulfill({ json: { data } })
    if (path === '/api/auth/me/modulos') return ok({ modulos: catalogo.map((id) => ({ id, liberado: permitidos.includes(id) })), acoes: {} })
    if (['/api/atendimento/conversas', '/api/kds/pedidos', '/api/caixa/dia'].includes(path) && controle.demora) await new Promise((resolve) => setTimeout(resolve, controle.demora))
    if (path === '/api/atendimento/conversas') {
      if (controle.modo === 'erro' || controle.modo === 'revogado') return route.fulfill({ status: controle.modo === 'revogado' ? 403 : 503, json: { error: { message: 'Leitura indisponível na prova' } } })
      if (controle.modo === 'parcial') return ok(Array.from({ length: 100 }, (_, i) => ({ id: `${url.searchParams.get('pagina')}-${i}`, situacao: 'Assumida', aguardaResposta: true })))
      return ok(controle.modo === 'vazio' ? [] : [
        { id: 'humano-1', situacao: 'Assumida', aguardaResposta: true, assumidaPorUsuarioId: usuario },
        { id: 'humano-2', situacao: 'Assumida', aguardaResposta: false },
        { id: 'automatico', situacao: 'Automatica', aguardaResposta: true },
        { id: 'encerrada', situacao: 'Encerrada', aguardaResposta: true },
      ])
    }
    if (path === '/api/kds/pedidos') return ok(controle.modo === 'parcial'
      ? Array.from({ length: 200 }, (_, i) => ({ id: String(i), status: 'pronto' }))
      : [{ id: 'a', status: 'aguardando' }, { id: 'b', status: 'preparando' }, { id: 'c', status: 'pronto' }])
    if (path === '/api/caixa/dia') return ok({ saldoEsperado, aberto: true, fechado: false })
    if (path === '/api/atendimento/configuracao') return ok({})
    return ok([])
  })
  const page = await context.newPage()
  await page.clock.install()
  ultimaPagina = page
  const erros = []
  page.on('pageerror', (erro) => { erros.push(erro.message); console.error('Erro de página:', erro.message) })
  await page.goto(origem + '/#/')
  await page.getByRole('heading', { name: /Felipe/ }).waitFor()
  return { context, page, controle, erros }
}
const ordem = (page) => page.locator('[data-modulo]').evaluateAll((itens) => itens.map((item) => item.dataset.modulo))
const ordemIndicadores = (page) => page.locator('[data-indicador]').evaluateAll((itens) => itens.map((item) => item.dataset.indicador))
async function conferirDobra(page) {
  await page.evaluate(() => { document.querySelector('main').parentElement.scrollTop = 0 })
  const primeiraAcao = await page.locator('[data-indicador] a').first().boundingBox()
  assert.ok(primeiraAcao.y >= 0 && primeiraAcao.y + primeiraAcao.height <= page.viewportSize().height, 'A primeira ação deve caber sem rolagem')
  assert.ok(primeiraAcao.height >= 44, 'A ação deve preservar o alvo de toque')
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
  assert.ok(await page.locator('[data-indicador]').evaluateAll((itens) => itens.every((el) => el.scrollWidth <= el.clientWidth)), 'Os cartões não podem ocultar conteúdo lateralmente')
}
async function foto(page, nome) {
  await page.evaluate(async () => { await document.fonts.ready; await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve))) })
  await page.screenshot({ path: join(evidencias, nome + '.png'), fullPage: true })
}
try {
  const { page, context, controle, erros } = await preparar({ demora: 900 })
  const atendimento = page.locator('[data-indicador="atendimento"]')
  await atendimento.getByText('Leitura em andamento').waitFor()
  assert.equal(await atendimento.locator('strong').innerText(), '—')
  await atendimento.getByText('Conferido às', { exact: false }).waitFor()
  assert.equal(await atendimento.locator('strong').innerText(), '2')
  assert.equal(await page.locator('[data-indicador="cozinha"] strong').innerText(), '2')
  assert.equal(await page.locator('[data-indicador="entregas"] strong').innerText(), '1')
  assert.match(await page.locator('[data-indicador="financeiro"]').innerText(), /300,03[\s\S]*incluindo Pix e cartão/)
  assert.deepEqual(await ordemIndicadores(page), ['atendimento', 'entregas', 'cozinha', 'financeiro'])
  assert.equal(await page.getByRole('progressbar').count(), 0)
  await page.setViewportSize({ width: 390, height: 844 })
  await conferirDobra(page)
  await foto(page, 'hub-cliente-celular')
  await page.setViewportSize({ width: 1440, height: 1000 })
  await foto(page, 'hub-cliente-desktop')
  const personalizar = page.getByRole('button', { name: 'Personalizar painel', exact: true })
  await personalizar.click(); await page.getByRole('dialog').waitFor()
  await page.keyboard.press('Escape')
  assert.equal(await personalizar.evaluate((el) => el === document.activeElement), true)
  await personalizar.click()
  await page.getByLabel('Gestão da casa', { exact: true }).check()
  await page.getByRole('status').filter({ hasText: 'Ordem dos indicadores: Financeiro → Atendimento → Entregas → Cozinha.' }).waitFor()
  await page.getByRole('button', { name: 'Aplicar personalização' }).click()
  assert.deepEqual(await ordemIndicadores(page), ['financeiro', 'atendimento', 'entregas', 'cozinha'])
  await page.getByRole('button', { name: 'Desfazer', exact: true }).click()
  assert.deepEqual(await ordemIndicadores(page), ['atendimento', 'entregas', 'cozinha', 'financeiro'])
  await personalizar.click()
  await page.getByLabel('Produção e entregas', { exact: true }).check()
  await page.getByRole('status').filter({ hasText: 'Ordem dos indicadores: Cozinha → Entregas → Atendimento → Financeiro.' }).waitFor()
  await page.getByLabel('Módulos compactos, com telas recolhidas').check()
  await page.getByLabel('Movimentos suaves').uncheck()
  await foto(page, 'hub-personalizacao')
  await page.getByRole('button', { name: 'Aplicar personalização' }).click()
  assert.equal((await ordem(page))[0], 'producao')
  assert.deepEqual(await ordemIndicadores(page), ['cozinha', 'entregas', 'atendimento', 'financeiro'])
  await page.reload(); await page.getByRole('button', { name: 'Personalizar painel' }).waitFor()
  assert.equal((await ordem(page))[0], 'producao')
  assert.deepEqual(await ordemIndicadores(page), ['cozinha', 'entregas', 'atendimento', 'financeiro'])
  assert.equal(await page.locator('[data-modulo="producao"] details').getAttribute('open'), null)
  await page.locator('[data-modulo="producao"] summary').click()
  await page.getByRole('link', { name: 'Calculadora', exact: true }).waitFor({ state: 'visible' })
  await page.getByRole('button', { name: 'Organizar módulos', exact: true }).click()
  const antes = await ordem(page)
  const origemDrag = page.locator('[data-alca-modulo]').nth(0), destinoDrag = page.locator('[data-alca-modulo]').nth(1)
  await origemDrag.scrollIntoViewIfNeeded()
  const a = await origemDrag.boundingBox(), b = await destinoDrag.boundingBox()
  await page.mouse.move(a.x + a.width / 2, a.y + a.height / 2); await page.mouse.down()
  await page.mouse.move(b.x + b.width / 2, b.y + b.height / 2, { steps: 16 }); await page.mouse.up()
  await page.waitForFunction((primeiro) => document.querySelector('[data-modulo]')?.dataset.modulo !== primeiro, antes[0])
  // O sensor de ponteiro do dnd-kit suprime clicks por 50 ms após soltar.
  await page.clock.runFor(60)
  await page.getByRole('button', { name: 'Desfazer', exact: true }).click()
  await page.waitForFunction((esperado) => JSON.stringify([...document.querySelectorAll('[data-modulo]')].map((el) => el.dataset.modulo)) === JSON.stringify(esperado), antes)
  assert.deepEqual(await ordem(page), antes)
  await page.setViewportSize({ width: 390, height: 844 })
  const alca = page.locator('[data-alca-modulo]').first()
  await alca.scrollIntoViewIfNeeded(); await alca.focus()
  await page.evaluate(() => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve))))
  await page.keyboard.press('Space')
  await page.waitForFunction(() => document.querySelector('[data-alca-modulo][aria-pressed="true"]'))
  await page.keyboard.press('ArrowDown')
  await page.waitForFunction(() => [...document.querySelectorAll('[role="status"]')].some((el) => el.textContent.includes('sobre a posição 2')))
  await page.keyboard.press('Space')
  await page.waitForFunction((primeiro) => document.querySelector('[data-modulo]')?.dataset.modulo !== primeiro, antes[0])
  const teclado = await ordem(page)
  await page.locator('[data-alca-modulo]').first().focus(); await page.keyboard.press('Space')
  await page.waitForFunction(() => document.querySelector('[data-alca-modulo][aria-pressed="true"]'))
  await page.keyboard.press('ArrowDown'); await page.keyboard.press('Escape')
  assert.deepEqual(await ordem(page), teclado)
  const aberto = page.locator('[data-modulo] details[open] summary')
  if (await aberto.count()) await aberto.first().click()
  const touchOrigem = page.locator('[data-alca-modulo]').nth(0), touchDestino = page.locator('[data-alca-modulo]').nth(1)
  await touchOrigem.evaluate((el) => el.scrollIntoView({ block: 'center' }))
  const toqueA = await touchOrigem.boundingBox(), toqueB = await touchDestino.boundingBox()
  assert.ok(toqueB.y + toqueB.height < 844, 'Alvo do toque deve estar visível')
  const cdp = await context.newCDPSession(page)
  const pontoA = { x: toqueA.x + toqueA.width / 2, y: toqueA.y + toqueA.height / 2 }
  const pontoB = { x: toqueB.x + toqueB.width / 2, y: toqueB.y + toqueB.height / 2 }
  await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [pontoA] })
  await page.waitForFunction(() => document.querySelector('[data-alca-modulo][aria-pressed="true"]'))
  for (let passo = 1; passo <= 12; passo++) await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x: pontoA.x + (pontoB.x - pontoA.x) * passo / 12, y: pontoA.y + (pontoB.y - pontoA.y) * passo / 12 }] })
  await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] })
  await page.waitForFunction((primeiro) => document.querySelector('[data-modulo]')?.dataset.modulo !== primeiro, teclado[0])
  await page.clock.runFor(60)
  await cdp.detach()
  await page.getByRole('button', { name: 'Concluir organização' }).click()
  await page.evaluate(() => document.querySelector('main').scrollIntoView())
  await foto(page, 'hub-celular-compacto')
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
  await page.getByRole('radio', { name: 'Escuro', exact: true }).click()
  await foto(page, 'hub-celular-escuro')
  await conferirDobra(page)
  await page.setViewportSize({ width: 320, height: 844 })
  await conferirDobra(page)
  await page.setViewportSize({ width: 390, height: 844 })
  await personalizar.click(); await foto(page, 'hub-modal-celular')
  assert.equal(await page.getByRole('dialog').evaluate((el) => el.scrollWidth <= innerWidth), true)
  await page.getByLabel('Movimentos suaves').focus()
  assert.equal(await page.getByLabel('Movimentos suaves').evaluate((el) => el.getBoundingClientRect().bottom < innerHeight), true)
  await foto(page, 'hub-modal-celular-rolado')
  await page.keyboard.press('Escape')
  await personalizar.click()
  // Reaplicar inclusive o modelo já selecionado depois do arraste.
  await page.getByLabel('Produção e entregas', { exact: true }).click()
  await page.getByRole('button', { name: 'Aplicar personalização' }).click()
  assert.equal((await ordem(page))[0], 'producao')
  const antesOculto = controle.chamadas.filter((item) => item.path === '/api/atendimento/conversas').length
  await page.evaluate(() => Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'hidden' }))
  await page.clock.runFor(61000)
  assert.equal(controle.chamadas.filter((item) => item.path === '/api/atendimento/conversas').length, antesOculto)
  await page.evaluate(() => { Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' }); document.dispatchEvent(new Event('visibilitychange')) })
  await atendimento.getByText(/Conferido às/).waitFor()
  assert.ok(controle.chamadas.filter((item) => item.path === '/api/atendimento/conversas').length > antesOculto)
  controle.demora = 0
  controle.demora = 0; controle.modo = 'erro'
  await page.getByRole('button', { name: 'Atualizar números' }).click()
  await atendimento.getByText(/Desatualizado/).waitFor()
  assert.equal(await atendimento.locator('strong').innerText(), '2')
  controle.modo = 'revogado'; await page.getByRole('button', { name: 'Atualizar números' }).click()
  await atendimento.getByText(/Não foi possível ler/).waitFor()
  assert.equal(await atendimento.locator('strong').innerText(), '—')
  controle.modo = 'vazio'; await page.getByRole('button', { name: 'Atualizar números' }).click()
  await atendimento.getByText(/Conferido às/).waitFor()
  assert.equal(await atendimento.locator('strong').innerText(), '0')
  assert.equal(await page.getByRole('progressbar').count(), 0)
  controle.demora = 900
  await page.getByRole('button', { name: 'Atualizar números' }).click()
  await atendimento.getByText(/Atualizando · última leitura/).waitFor()
  assert.equal(await atendimento.locator('strong').innerText(), '0')
  assert.equal(await page.getByRole('progressbar').count(), 0)
  await atendimento.getByText(/Conferido às/).waitFor()
  controle.demora = 0
  controle.modo = 'parcial'; await page.getByRole('button', { name: 'Atualizar números' }).click()
  await atendimento.getByText(/Leitura parcial/).waitFor()
  assert.equal(await atendimento.locator('strong').innerText(), '1000+')
  assert.equal(await page.locator('[data-indicador="entregas"] strong').innerText(), '200+')
  assert.equal(await page.getByRole('progressbar').count(), 0)
  // Troca de pessoa e de empresa remonta a leitura e não herda a personalização.
  controle.demora = 900; controle.modo = 'normal'
  await page.evaluate(({ empresa, usuario }) => {
    const sessao = JSON.parse(sessionStorage.getItem('easystok.sessao'))
    sessao.empresa.id = empresa + '-outra'; sessao.usuario.id = usuario + '-outro'; sessao.usuario.nome = 'Outra pessoa'
    sessionStorage.setItem('easystok.sessao', JSON.stringify(sessao)); window.dispatchEvent(new Event('easystok:sessao-alterada'))
  }, { empresa, usuario })
  await page.getByRole('heading', { name: /Outra/ }).waitFor()
  assert.equal((await ordem(page))[0], 'cardapio')
  assert.equal(await atendimento.locator('strong').innerText(), '—')
  await atendimento.getByText(/Conferido às/).waitFor()
  assert.equal(await atendimento.locator('strong').innerText(), '2')
  assert.equal(await page.evaluate((chave) => JSON.parse(localStorage.getItem(chave)).ordem[0], chave), 'producao')
  assert.equal(erros.length, 0, erros.join('\n'))
  await context.close()
  const restrito = await preparar({ permitidos: ['cozinha'] })
  await restrito.page.locator('[data-indicador="cozinha"]').getByText(/Conferido às/).waitFor()
  assert.deepEqual(await ordem(restrito.page), ['cozinha'])
  assert.ok(!restrito.controle.chamadas.some((item) => ['/api/atendimento/conversas', '/api/caixa/dia'].includes(item.path)))
  assert.equal(await restrito.page.locator('[data-indicador]').count(), 1)
  await restrito.context.close()
  const falha = await preparar({ permitidos: ['atendimento'], modo: 'revogado', bloqueiaStorage: true })
  await falha.page.locator('[data-indicador="atendimento"]').getByText(/Não foi possível ler/).waitFor()
  assert.equal(await falha.page.locator('[data-indicador="atendimento"] strong').innerText(), '—')
  await falha.page.getByRole('button', { name: 'Adicionar Atendimento aos favoritos' }).click()
  await falha.page.getByText(/personalização vale só nesta visita/).waitFor()
  assert.ok(!falha.controle.chamadas.some((item) => ['/api/kds/pedidos', '/api/caixa/dia'].includes(item.path)))
  await falha.context.close()
  const ampliado = await preparar({ permitidos: ['financeiro'], saldoEsperado: 123456.78 })
  await ampliado.page.locator('[data-indicador="financeiro"]').getByText(/Conferido às/).waitFor()
  await ampliado.page.setViewportSize({ width: 320, height: 844 })
  await conferirDobra(ampliado.page)
  await ampliado.page.evaluate(() => { document.documentElement.style.fontSize = '200%' })
  const geometria = await ampliado.page.locator('[data-indicador="financeiro"]').evaluate((el) => {
    const titulo = el.querySelector('h3').getBoundingClientRect(), valor = el.querySelector('strong').getBoundingClientRect()
    return { cabe: el.scrollWidth <= el.clientWidth, separados: titulo.bottom <= valor.top || valor.bottom <= titulo.top || titulo.right <= valor.left || valor.right <= titulo.left }
  })
  assert.deepEqual(geometria, { cabe: true, separados: true }, 'Moeda longa e texto 200% devem caber sem sobreposição')
  await ampliado.page.locator('[data-indicador="financeiro"]').scrollIntoViewIfNeeded()
  await foto(ampliado.page, 'hub-financeiro-texto-200')
  await ampliado.context.close()
  console.log('Hub UI: números, erro/zero/parcial, modelos, persistência por pessoa/empresa, drag com mouse/toque, teclado/Esc, polling visível, celular/escuro, modal/foco e permissões aprovados. Nenhuma escrita operacional.')
} catch (erro) {
  if (ultimaPagina && !ultimaPagina.isClosed()) console.error('Diagnóstico:', await ultimaPagina.evaluate(() => ({ foco: document.activeElement?.outerHTML.slice(0, 350), anuncios: [...document.querySelectorAll('[aria-live], [role="status"]')].map((el) => el.textContent.slice(0, 180)), ordem: [...document.querySelectorAll('[data-modulo]')].map((el) => el.dataset.modulo) })))
  if (ultimaPagina && !ultimaPagina.isClosed()) await foto(ultimaPagina, 'falha')
  throw erro
} finally { await browser.close() }
