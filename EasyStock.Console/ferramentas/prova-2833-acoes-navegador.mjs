// Prova de navegador da #2833: console real em 1280x720 e 390px, HTTP controlado, sem chamadas à loja.
//   1. o botão Emoji insere no cursor, busca por :nome e Enter insere o primeiro;
//   2. o menu da bolha reage (POST /reacao com o emoji) e a bolha mostra a reação;
//   3. apagar para todos pergunta antes, chama DELETE e a bolha vira "Você apagou esta mensagem para todos";
//   4. na mensagem que saiu pela Meta o item de apagar não aparece e a linha explica por quê.
// PLAYWRIGHT_MODULE deve apontar para um runtime instalado de Playwright.
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import assert from 'node:assert/strict'
import { join, resolve } from 'node:path'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(process.env.EVIDENCIAS_UX ?? resolve(import.meta.dirname, '../../.build/2833'))
const origem = process.env.CONSOLE_URL ?? 'http://127.0.0.1:5760'
mkdirSync(pasta, { recursive: true })
const foto = async (page, nome) => {
  await page.evaluate(() => document.fonts.ready)
  await page.screenshot({ path: join(pasta, nome + '.png') })
}
const empresa = '11111111-1111-4111-8111-111111111111', usuario = '22222222-2222-4222-8222-222222222222'
const ha = (min) => new Date(Date.now() - min * 60000).toISOString()
const conversas = [
  { id: 'conversa-ponte', contatoNome: 'Maria', contatoIdExterno: '5511987650000', canal: 'WhatsApp' },
  { id: 'conversa-meta', contatoNome: 'Joana', contatoIdExterno: '5511987650001', canal: 'WhatsApp' },
].map((c, i) => ({ ...c, situacao: 'Assumida', assumidaPorUsuarioId: usuario, assumidaPorNome: 'Dona de teste', ultimaMensagemEm: ha(2 + i), naoLidas: 0 }))
const mensagens = {
  'conversa-ponte': [
    { id: 'p-cliente', direcao: 'Entrada', autor: 'Cliente', status: 'Recebida', tipoConteudo: 'Texto', texto: 'Oi, a lasanha sai hoje?', enviadaEm: ha(4), externoId: 'wpp:loja:false_5511987650000@c.us_AAA' },
    { id: 'p-loja', direcao: 'Saida', autor: 'Dona', status: 'Lida', tipoConteudo: 'Texto', texto: 'Sai sim! Mando o Pix?', enviadaEm: ha(2), externoId: 'wpp:loja:true_5511987650000@c.us_BBB', enviadaPorUsuarioId: usuario },
  ],
  'conversa-meta': [
    { id: 'm-cliente', direcao: 'Entrada', autor: 'Cliente', status: 'Recebida', tipoConteudo: 'Texto', texto: 'Tem nhoque?', enviadaEm: ha(5), externoId: 'wamid.CLIENTE' },
    { id: 'm-loja', direcao: 'Saida', autor: 'Dona', status: 'Lida', tipoConteudo: 'Texto', texto: 'Tem, ao sugo.', enviadaEm: ha(3), externoId: 'wamid.LOJA', enviadaPorUsuarioId: usuario },
  ],
}
const browser = await chromium.launch({ headless: true })
const context = await browser.newContext({ viewport: { width: 1280, height: 720 }, reducedMotion: 'reduce' })
await context.addInitScript(({ empresa, usuario }) => {
  sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'prova-controlada', expiraEm: Date.now() + 3600000,
    empresa: { id: empresa, nome: 'Empresa de teste' }, usuario: { id: usuario, nome: 'Dona de teste' } }))
}, { empresa, usuario })
const chamadas = [], erros = []
await context.route('**/api/**', async (route) => {
  const requisicao = route.request()
  const path = new URL(requisicao.url()).pathname
  if (!path.startsWith('/api/')) return route.continue()
  const metodo = requisicao.method()
  chamadas.push({ path, metodo, corpo: requisicao.postData() })
  const ok = (data) => route.fulfill({ json: { data } })
  if (path === '/api/auth/me/modulos') return ok({ modulos: [{ id: 'atendimento', liberado: true }], acoes: { editarAtendimento: true } })
  if (path === '/api/atendimento/conversas') return ok(conversas)
  if (path === '/api/atendimento/atendentes') return ok([{ id: usuario, nome: 'Dona de teste' }])
  const acaoNaMensagem = /^\/api\/atendimento\/conversas\/([^/]+)\/mensagens\/([^/]+)(\/reacao)?$/.exec(path)
  if (acaoNaMensagem) {
    const [, conversaId, mensagemId, reacao] = acaoNaMensagem
    const mensagem = mensagens[conversaId].find((m) => m.id === mensagemId)
    if (reacao && metodo === 'POST') mensagem.reacao = JSON.parse(requisicao.postData()).emoji ?? null
    else if (!reacao && metodo === 'DELETE') mensagem.apagadaEm = new Date().toISOString()
    return ok(mensagem)
  }
  const lista = /^\/api\/atendimento\/conversas\/([^/]+)\/mensagens$/.exec(path)
  if (lista && metodo === 'GET') return ok(mensagens[lista[1]])
  if (path.endsWith('/dossie')) return ok({ cliente: null, enderecos: [], totalPedidos: 0, notas: [], tags: [] })
  if (path.endsWith('/pedido')) return ok(null)
  if (path === '/api/atendimento/configuracao') return ok({ slaRespostaMinutos: 5 })
  return ok([])
})
const page = await context.newPage()
page.setDefaultTimeout(10000)
page.on('pageerror', (erro) => erros.push(erro.message))
const abrirConversa = async (nome) => {
  await page.getByText(nome, { exact: true }).first().click()
  await page.locator('textarea:visible').first().waitFor()
}
const bolhaDe = (texto) => page.getByText(texto, { exact: true }).locator('xpath=ancestor::div[contains(@class, "balao")][1]')
const abrirMenuDa = async (texto) => {
  await bolhaDe(texto).hover()
  await bolhaDe(texto).getByRole('button', { name: 'Mais opções da mensagem' }).click()
  await page.getByRole('menu', { name: 'Mais opções da mensagem' }).waitFor()
}
// Revisão da #2833: reagir e apagar não mudam a última mensagem, e a mensagem lida não "muda sozinha"; a volta
// seguinte do laço (5 s) usa a lista guardada. A mudança tem de seguir na tela depois dela.
const umaVoltaDoLaco = async () => {
  await page.waitForResponse((r) => new URL(r.url()).pathname === '/api/atendimento/conversas', { timeout: 12000 })
  await page.waitForTimeout(500)
}
try {
  await page.goto(origem + '/#/m/atendimento/balcao')
  await page.getByRole('button', { name: /^Todas/ }).click()
  await abrirConversa('Maria')

  // 1. Emoji no cursor.
  const campo = page.locator('textarea:visible').first()
  await campo.fill('Oi tudo')
  await campo.evaluate((el) => el.setSelectionRange(2, 2))
  await page.getByRole('button', { name: 'Emoji', exact: true }).click()
  await page.getByRole('dialog', { name: 'Emoji' }).waitFor()
  await foto(page, '01-seletor-de-emoji-1280')
  await page.getByRole('button', { name: 'sorriso', exact: true }).click()
  assert.equal(await campo.inputValue(), 'Oi😊 tudo', 'o emoji entra no cursor')
  await page.getByRole('searchbox', { name: 'Buscar emoji' }).fill(':mas')
  assert.equal(await page.getByRole('dialog', { name: 'Emoji' }).getByRole('button').count(), 1, ':mas acha só a massa')
  await page.keyboard.press('Enter')
  assert.equal(await campo.inputValue(), 'Oi😊🍝 tudo', 'Enter insere o primeiro, logo depois do anterior')
  await page.keyboard.press('Escape')
  await page.getByRole('dialog', { name: 'Emoji' }).waitFor({ state: 'detached' })
  await campo.fill('')

  // 2. Reação.
  await abrirMenuDa('Sai sim! Mando o Pix?')
  const menu = page.getByRole('menu', { name: 'Mais opções da mensagem' })
  assert.equal(await menu.getByRole('menuitemradio').count(), 6, 'seis reações')
  await menu.getByRole('menuitem', { name: /^Apagar para todos/ }).waitFor()
  await menu.getByRole('menuitem', { name: 'Copiar texto' }).waitFor()
  await foto(page, '02-menu-da-bolha-ponte-1280')
  await Promise.all([
    page.waitForRequest((r) => r.url().endsWith('/mensagens/p-loja/reacao') && r.method() === 'POST'),
    menu.getByRole('menuitemradio', { name: '❤️' }).click(),
  ])
  assert.equal(JSON.parse(chamadas.findLast((c) => c.path.endsWith('/reacao')).corpo).emoji, '❤️')
  await bolhaDe('Sai sim! Mando o Pix?').getByTitle('Reação da loja').waitFor()
  await umaVoltaDoLaco()
  assert.equal(await bolhaDe('Sai sim! Mando o Pix?').getByTitle('Reação da loja').count(), 1, 'a volta do laço não tira a reação')
  await foto(page, '03-reacao-na-bolha-1280')

  // 3. Apagar para todos.
  await abrirMenuDa('Sai sim! Mando o Pix?')
  // A pergunta é o modal do Console (#3011), nunca o window.confirm nativo; cancelar não chama a API.
  const nativos = []
  page.on('dialog', (dialogo) => { nativos.push(dialogo.message()); dialogo.dismiss() })
  const apagarNoMenu = () => page.getByRole('menu', { name: 'Mais opções da mensagem' }).getByRole('menuitem', { name: /^Apagar para todos/ }).click()
  const pergunta = page.getByRole('dialog', { name: 'Apagar para todos?' })
  const apagou = []
  page.on('request', (r) => { if (r.url().endsWith('/mensagens/p-loja') && r.method() === 'DELETE') apagou.push(r.url()) })
  await apagarNoMenu()
  await pergunta.waitFor()
  assert.match(await pergunta.innerText(), /não se desfaz/, 'pergunta antes de apagar')
  await pergunta.getByRole('button', { name: 'Cancelar' }).click()
  await pergunta.waitFor({ state: 'detached' })
  assert.equal(apagou.length, 0, 'cancelar não apaga')
  await abrirMenuDa('Sai sim! Mando o Pix?')
  await apagarNoMenu()
  await Promise.all([
    page.waitForRequest((r) => r.url().endsWith('/mensagens/p-loja') && r.method() === 'DELETE'),
    pergunta.getByRole('button', { name: 'Apagar para todos' }).click(),
  ])
  assert.deepEqual(nativos, [], 'nenhum diálogo nativo do navegador')
  await page.getByText(/^Você apagou esta mensagem para todos · \d{2}:\d{2}$/).waitFor()
  assert.equal(await page.getByText('Sai sim! Mando o Pix?', { exact: true }).count(), 0, 'o texto apagado some da bolha')
  await umaVoltaDoLaco()
  assert.equal(await page.getByText(/^Você apagou esta mensagem para todos/).count(), 1, 'a volta do laço não desfaz o apagar')
  await foto(page, '04-apagada-1280')

  // 4. Meta.
  await abrirConversa('Joana')
  await abrirMenuDa('Tem, ao sugo.')
  const naMeta = page.getByRole('menu', { name: 'Mais opções da mensagem' })
  assert.equal(await naMeta.getByRole('menuitem', { name: /^Apagar para todos/ }).count(), 0, 'na Meta o item não aparece')
  await naMeta.getByText(/conexão oficial \(Meta\)/).waitFor()
  await foto(page, '05-menu-na-meta-1280')
  await page.keyboard.press('Escape')

  // Celular: o seletor cabe na coluna.
  await page.setViewportSize({ width: 390, height: 844 })
  await abrirConversa('Joana')
  await page.getByRole('button', { name: 'Emoji', exact: true }).click()
  const caixa = await page.getByRole('dialog', { name: 'Emoji' }).boundingBox()
  assert.ok(caixa.x >= 0 && caixa.x + caixa.width <= 390, 'o seletor cabe na coluna do celular')
  await foto(page, '06-seletor-de-emoji-390')

  assert.equal(chamadas.some((c) => c.metodo === 'POST' && /\/mensagens$/.test(c.path)), false, 'nenhuma mensagem foi enviada')
  assert.deepEqual(erros, [])
  console.log('ok #2833: emoji no cursor e :nome; reação pelo menu; apagar pergunta, chama DELETE e marca a bolha; Meta explica; 390 cabe')
} finally {
  await foto(page, '99-estado-final')
  writeFileSync(join(pasta, 'diagnostico.json'), JSON.stringify({ chamadas, erros }, null, 2))
  await browser.close()
}
