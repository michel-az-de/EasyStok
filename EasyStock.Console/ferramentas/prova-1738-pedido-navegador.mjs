// Console real em1280x720 e390px; HTTP controlado, sem chamadas à loja.
// PLAYWRIGHT_MODULE deve apontar para um runtime instalado de Playwright.
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import assert from 'node:assert/strict'
import { join, resolve } from 'node:path'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(process.env.EVIDENCIAS_UX ?? resolve(import.meta.dirname, '../../.build/1738/depois'))
const origem = process.env.CONSOLE_URL ?? 'http://127.0.0.1:5755'
mkdirSync(pasta, { recursive: true })
const indice = []
const foto = async (page, nome) => {
  await page.evaluate(() => document.fonts.ready)
  const arquivo = join(pasta, nome + '.png')
  await page.screenshot({ path: arquivo, fullPage: true })
  indice.push({ modulo: 'Atendimento', tela: nome, rota: '/#/m/atendimento/balcao', arquivo,
    viewport: page.viewportSize(), tipoProva: 'Navegador real, HTTP controlado' })
}
const totalVisivel = page => page.getByText('Total dos itens', { exact: true }).evaluate(el => {
  const r = el.parentElement.getBoundingClientRect(), x = r.x + r.width / 2, y = r.y + r.height / 2
  return x >= 0 && x < innerWidth && y >= 0 && y < innerHeight && el.parentElement.contains(document.elementFromPoint(x, y))
})
const empresa = '11111111-1111-4111-8111-111111111111', usuario = '22222222-2222-4222-8222-222222222222'
const conversas = ['Maria', 'Joana'].map((nome, i) => ({ id: 'conversa-' + i, contatoNome: nome, contatoIdExterno: '551198765000' + i,
  clienteId: 'cliente-' + i, canal: 'WhatsApp', situacao: 'Assumida', assumidaPorUsuarioId: usuario, assumidaPorNome: 'Dona de teste',
  ultimaMensagemEm: new Date(Date.now() - i * 60000).toISOString(), naoLidas: 0 }))
const itens = Array.from({ length: 43 }, (_, i) => ({ id: 'prato-' + i, nome: 'Prato ' + (i + 1), linha: 'paraServir', pesoExibicao: '600 g', precoCentavos: 3500, disponivel: true }))
const browser = await chromium.launch({ headless: true })
const context = await browser.newContext({ viewport: { width: 1280, height: 720 }, reducedMotion: 'reduce' })
await context.addInitScript(({ empresa, usuario }) => {
  sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'prova-controlada', expiraEm: Date.now() + 3600000,
    empresa: { id: empresa, nome: 'Empresa de teste' }, usuario: { id: usuario, nome: 'Dona de teste' } }))
}, { empresa, usuario })
const chamadas = [], erros = []
let pedidoCriado = false
await context.route('**/api/**', async route => {
  const path = new URL(route.request().url()).pathname
  if (!path.startsWith('/api/')) return route.continue()
  chamadas.push({ path, metodo: route.request().method() })
  const ok = data => route.fulfill({ json: { data } })
  if (path === '/api/auth/me/modulos') return ok({ modulos: ['atendimento', 'cardapio'].map(id => ({ id, liberado: true })), acoes: { editarAtendimento: true, editarCardapio: true } })
  if (path === '/api/atendimento/conversas') return ok(conversas.map((c, i) => ({ ...c, ...(pedidoCriado ? { pedidoEmAndamentoId: 'pedido-prova-' + i, pedidoStatus: 'aguardando_pagamento' } : {}) })))
  if (path === '/api/atendimento/atendentes') return ok([{ id: usuario, nome: 'Dona de teste' }])
  if (path === '/api/atendimento/comanda/cardapio') return ok({ itens, porcoes: {} })
  if (path.endsWith('/mensagens')) return ok([{ id: 'mensagem', direcao: 'Entrada', texto: 'Quero montar meu pedido.', enviadaEm: new Date().toISOString(), tipoConteudo: 'Texto' }])
  if (path.endsWith('/pedido')) return ok(pedidoCriado ? { pedidoId: 'pedido-prova-' + (path.includes('conversa-1') ? '1' : '0'), numeroComanda: 72, status: 'aguardando_pagamento', total: 175, totalPago: 0, frete: 0,
    itens: itens.slice(0, 5).map(i => ({ cardapioItemId: i.id, quantidade: 1, nome: i.nome, precoUnitario: 35 })), cobranca: null } : null)
  if (path.endsWith('/estornos-manuais')) return ok({ pagamentos: [], estornos: [] })
  if (path.endsWith('/dossie')) return ok({ cliente: { id: path.includes('conversa-1') ? 'cliente-1' : 'cliente-0', nome: path.includes('conversa-1') ? 'Joana' : 'Maria', telefone: '+5511987650000' }, enderecos: [{ logradouro: 'Rua de teste', numero: '10', bairro: 'Centro', cep: '01001000', padrao: true }], totalPedidos: 0, notas: [], tags: [] })
  if (path === '/api/atendimento/configuracao') return ok({ slaRespostaMinutos: 5 })
  if (path === '/api/atendimento/agenda') return ok({ lojaDisponivel: true, dias: [] })
  return ok([])
})
const page = await context.newPage()
page.on('pageerror', erro => erros.push(erro.message))
try {
  await page.goto(origem + '/#/m/atendimento/balcao')
  await page.getByText('Maria', { exact: true }).first().waitFor({ timeout: 5000 })
  await foto(page, '01-inicial')
  await page.getByText('Joana', { exact: true }).first().click()
  await page.getByRole('button', { name: 'Adicionar itens', exact: true }).click()
  for (let i = 1; i <= 5; i++) await page.getByRole('button', { name: new RegExp('^Prato ' + i + '\\b') }).click()
  await page.getByRole('button', { name: 'Anotar na Prato 1', exact: true }).click()
  await page.getByRole('textbox', { name: 'Observação de Prato 1', exact: true }).fill('Molho à parte')
  await page.getByRole('textbox', { name: 'Observação de Prato 1', exact: true }).press('Tab')
  assert.equal(await totalVisivel(page), true, 'Total deve ficar visível com cinco itens em 1280x720')
  await foto(page, '02-montagem')
  writeFileSync(join(pasta, 'montagem.txt'), await page.locator('body').innerText())
  await page.getByRole('button', { name: 'Fechar cardápio', exact: true }).click()
  await page.setViewportSize({ width: 390, height: 844 })
  await page.getByRole('tab', { name: 'Pedido', exact: true }).click()
  assert.equal(await totalVisivel(page), true, 'Total deve ficar visível com cinco itens em390px')
  await foto(page, '02-montagem-390')
  writeFileSync(join(pasta, 'montagem-390.txt'), await page.locator('body').innerText())
  await page.setViewportSize({ width: 1280, height: 720 })
  await page.reload()
  await page.getByText('Joana · Sem janela', { exact: true }).waitFor()
  assert.equal(await page.getByRole('button', { name: /^Anotar na Prato / }).count(), 5, 'F5 conserva os cinco itens na conversa de Joana')
  await page.getByText('Molho à parte', { exact: true }).waitFor()
  assert.equal(chamadas.filter(c => c.metodo !== 'GET').every(c => c.path.endsWith('/marcar-lida')), true,
    'Montar e recarregar não envia pedido nem inicia conversa na API')
  assert.equal(await totalVisivel(page), true)
  await foto(page, '03-apos-f5')
  writeFileSync(join(pasta, 'apos-f5.txt'), await page.locator('body').innerText())
  pedidoCriado = true
  await page.reload()
  await page.getByRole('button', { name: 'Ver cardápio', exact: true }).waitFor()
  const visitas = await page.evaluate(() => Object.keys(sessionStorage).filter(k => k.startsWith('easystok.visita:')).map(k => JSON.parse(sessionStorage.getItem(k))))
  assert.equal(Object.keys(visitas[0].comandas).length, 0, 'Pedido da API substitui e remove a montagem local')
  await foto(page, '04-pedido-da-api')
  await page.getByRole('button', { name: 'Ver cardápio', exact: true }).click()
  await page.getByRole('heading', { name: 'Consulta do cardápio', exact: true }).waitFor()
  assert.equal(await page.getByRole('button', { name: 'Consultar pratos', exact: true }).getAttribute('aria-pressed'), 'true')
  assert.equal(await page.getByRole('button', { name: /^Prato \d+\b/ }).count(), 43)
  assert.equal(await page.getByRole('button', { name: /^Prato \d+\b/ }).evaluateAll(els => els.every(el => el.disabled)), true)
  await foto(page, '05-pedido-criado-consulta')
  await page.getByRole('button', { name: 'Fechar cardápio', exact: true }).click()
  await page.getByRole('button', { name: 'Sair', exact: true }).click()
  assert.equal(await page.evaluate(() => Object.keys(sessionStorage).filter(k => k.startsWith('easystok.visita:')).length), 0, 'Sair limpa a memória da visita')
  assert.deepEqual(erros, [])
  console.log('ok total1280/390, cinco itens e conversa apósF5, prioridadeAPI, consulta43 pratos e limpezaSair')
} finally {
  writeFileSync(join(pasta, 'snapshot.txt'), await page.locator('body').innerText())
  await foto(page, '06-estado-final')
  writeFileSync(join(pasta, 'diagnostico.json'), JSON.stringify({ chamadas, erros }, null, 2))
  writeFileSync(join(pasta, 'indice.json'), JSON.stringify(indice, null, 2))
  await browser.close()
}
