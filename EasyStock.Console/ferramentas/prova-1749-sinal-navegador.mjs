// Console real em1280x720 e390px; HTTP controlado, sem chamadas à loja.
// PLAYWRIGHT_MODULE deve apontar para um runtime instalado de Playwright.
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import assert from 'node:assert/strict'
import { join, resolve } from 'node:path'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(process.env.EVIDENCIAS_UX ?? resolve(import.meta.dirname, '../../.build/1749/depois'))
const origem = process.env.CONSOLE_URL ?? 'http://127.0.0.1:5760'
mkdirSync(pasta, { recursive: true })
const indice = []
const foto = async (page, nome) => {
  await page.evaluate(() => document.fonts.ready)
  const arquivo = join(pasta, nome + '.png')
  await page.screenshot({ path: arquivo, fullPage: true })
  indice.push({ modulo: 'Atendimento', tela: nome, rota: '/#/m/atendimento/balcao', arquivo,
    viewport: page.viewportSize(), tipoProva: 'Navegador real, HTTP controlado' })
}
const empresa = '11111111-1111-4111-8111-111111111111', usuario = '22222222-2222-4222-8222-222222222222'
const conversas = ['Maria', 'Joana'].map((nome, i) => ({ id: 'conversa-' + i, contatoNome: nome, contatoIdExterno: '551198765000' + i,
  clienteId: 'cliente-' + i, canal: 'WhatsApp', situacao: 'Assumida', assumidaPorUsuarioId: usuario, assumidaPorNome: 'Dona de teste',
  ultimaMensagemEm: new Date(Date.now() - i * 60000).toISOString(), naoLidas: 0 }))
const itens = [{ id: '19736f37-3b4f-4213-90bc-45d886dd995f', nome: 'Lasanha Bolonhesa Clássica 600 g', pesoExibicao: '600 g', precoCentavos: 3800, categoria: 'Lasanhas', disponivel: true, estoqueAtual: null }]
const browser = await chromium.launch({ headless: true })
const context = await browser.newContext({ viewport: { width: 1280, height: 720 }, reducedMotion: 'reduce' })
await context.addInitScript(({ empresa, usuario }) => {
  sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'prova-controlada', expiraEm: Date.now() + 3600000,
    empresa: { id: empresa, nome: 'Empresa de teste' }, usuario: { id: usuario, nome: 'Dona de teste' } }))
}, { empresa, usuario })
const chamadas = [], erros = []
const hoje = new Date().toLocaleDateString('sv-SE', { timeZone: 'America/Sao_Paulo' })
const amanha = new Date(new Date(hoje + 'T12:00:00Z').getTime() + 86400000).toISOString().slice(0, 10)
// #2259: o sinal vale com 2 dias de antecedência; amanhã já não pede sinal.
const depois = new Date(new Date(hoje + 'T12:00:00Z').getTime() + 2 * 86400000).toISOString().slice(0, 10)
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
  if (path.startsWith('/api/atendimento/comanda/cardapio/')) {
    const item = itens.find(i => path.endsWith('/' + i.id))
    if (item) return ok({ nome: item.nome, descricao: item.descricao, porcao: item.pesoExibicao, preco: item.precoCentavos / 100 })
  }
  if (path.endsWith('/mensagens')) return ok([{ id: 'mensagem', direcao: 'Entrada', texto: 'Quero montar meu pedido.', enviadaEm: new Date().toISOString(), tipoConteudo: 'Texto' }])
  if (path.endsWith('/pedido')) return ok(pedidoCriado ? { pedidoId: 'pedido-prova-' + (path.includes('conversa-1') ? '1' : '0'), numeroComanda: 72, status: 'aguardando_pagamento', total: itens.slice(0, 5).reduce((s, i) => s + i.precoCentavos / 100, 0), totalPago: 0, frete: 0,
    itens: itens.slice(0, 5).map(i => ({ cardapioItemId: i.id, quantidade: 1, nome: i.nome, precoUnitario: i.precoCentavos / 100 })), cobranca: null } : null)
  if (path.endsWith('/estornos-manuais')) return ok({ pagamentos: [], estornos: [] })
  if (path.endsWith('/dossie')) return ok({ cliente: { id: path.includes('conversa-1') ? 'cliente-1' : 'cliente-0', nome: path.includes('conversa-1') ? 'Joana' : 'Maria', telefone: '+5511987650000' }, enderecos: [{ logradouro: 'Rua de teste', numero: '10', bairro: 'Centro', cep: '01001000', padrao: true }], totalPedidos: 0, notas: [], tags: [] })
  if (path === '/api/atendimento/configuracao') return ok({ slaRespostaMinutos: 5 })
  if (path === '/api/atendimento/comanda/janelas') {
    const data = new URL(route.request().url()).searchParams.get('dataInicio') || hoje
    return ok({ lojaDisponivel: true, janelas: [{ janelaId: 'janela-prova', data, label: 'Janela de prova', horaInicio: '09:00', horaFim: '11:00', vagasRestantes: 10, capacidade: 10 }] })
  }
  if (path === '/api/atendimento/agenda') return ok({ lojaDisponivel: true, dias: [] })
  return ok([])
})
const page = await context.newPage()
page.setDefaultTimeout(10000)
page.on('pageerror', erro => erros.push(erro.message))
const aviso = page.getByText('Maquininha na entrega: Encomenda com antecedência: sinal de 50% antes da produção. Restante na entrega.', { exact: true })
// #2437: dia, meios e aviso moram no painel "Revisar comanda" (#2232); no celular a aba "Pedido" virou "Cliente".
const abrirComanda = async () => {
  const dia = page.getByLabel('Dia da entrega (opcional)', { exact: true })
  if (await dia.isVisible()) return
  const aba = page.getByRole('tab', { name: 'Cliente', exact: true })
  if (await aba.isVisible()) { await aba.click(); await page.waitForTimeout(500) }
  if (!(await dia.isVisible())) await page.getByRole('button', { name: /^Revisar comanda/ }).click()
  await dia.waitFor()
}
// O painel da comanda flutua sobre a barra no celular; recolhe antes de usar o "Gerar cobrança".
const recolherComanda = async () => {
  const b = page.getByRole('button', { name: /^Recolher Comanda/ })
  if (await b.isVisible()) { await b.click(); await page.waitForTimeout(400) }
}
const escolherDia = async data => {
  await abrirComanda()
  await Promise.all([
    page.waitForResponse(r => r.url().includes('/comanda/janelas?') && new URL(r.url()).searchParams.get('dataInicio') === data),
    page.getByLabel('Dia da entrega (opcional)', { exact: true }).fill(data),
  ])
  const diaMes = data.slice(8, 10) + '/' + data.slice(5, 7)
  await page.getByRole('button', { name: new RegExp(diaMes + '.*Janela de prova') }).click()
}
try {
  // #2437: desde a #2232 o meio só se escolhe no seletor do "Gerar cobrança e enviar" (a escolha já gera),
  // então a prova confere o aviso nesse seletor, sem clicar em meio nenhum. Os outros meios ficam na prova-1749-aviso-sinal.
  await page.goto(origem + '/#/m/atendimento/balcao')
  // #2437: a Caixa de entrada abre em "Precisa de você"; as conversas da prova já estão assumidas, então ela abre em "Todas".
  await page.getByRole('button', { name: /^Todas/ }).click()
  await page.getByText('Maria', { exact: true }).first().waitFor()
  // #2135: a tela abre sem conversa escolhida; a atendente abre a Maria.
  await page.getByText('Maria', { exact: true }).first().click()
  await page.getByRole('button', { name: 'Adicionar itens', exact: true }).click()
  await page.getByRole('button', { name: /^Lasanha Bolonhesa Clássica 600 g/ }).click()
  await page.getByRole('button', { name: 'Fechar cardápio', exact: true }).click()
  await escolherDia(depois)
  assert.equal(await aviso.count(), 0, 'Sem abrir o seletor não há aviso')
  await page.getByRole('button', { name: 'Gerar cobrança e enviar', exact: true }).click()
  await aviso.waitFor()
  assert.equal(await aviso.count(), 1, 'Um aviso no seletor da barra antes de gerar')
  await aviso.scrollIntoViewIfNeeded()
  await foto(page, '01-picker-2-dias-1280')
  await escolherDia(amanha)
  await page.waitForTimeout(300)
  assert.equal(await aviso.count(), 0, 'Entrega amanhã não pede sinal (#2259)')
  await escolherDia(hoje)
  await page.waitForTimeout(300)
  assert.equal(await aviso.count(), 0, 'Entrega hoje não mostra aviso de sinal')
  await foto(page, '02-picker-hoje-1280')
  await escolherDia(depois)
  await page.setViewportSize({ width: 390, height: 844 })
  // A troca de layout remonta a barra: reabrimos seu seletor pela ação real.
  await page.getByRole('tab', { name: 'Cliente', exact: true }).click()
  await recolherComanda()
  if (!(await aviso.isVisible())) await page.getByRole('button', { name: 'Gerar cobrança e enviar', exact: true }).click()
  await aviso.waitFor()
  await aviso.scrollIntoViewIfNeeded()
  const box = await aviso.boundingBox()
  assert.ok(box.x >= 0 && box.x + box.width <= 390, 'O aviso cabe na coluna do celular')
  await foto(page, '03-picker-2-dias-390')
  pedidoCriado = true
  await page.reload()
  await page.getByRole('tab', { name: 'Cliente', exact: true }).click()
  await page.getByRole('button', { name: /^Revisar comanda/ }).waitFor()
  await page.waitForTimeout(500)
  assert.equal(await aviso.count(), 0, 'Pedido criado não mostra aviso de geração')
  await foto(page, '04-pedido-criado-sem-aviso-390')
  assert.equal(chamadas.filter(c => c.metodo !== 'GET').every(c => /\/(marcar-lida|sugestao|pedido\/simulacao)$/.test(c.path)), true, 'Nenhum pedido foi gerado pela prova')
  assert.deepEqual(erros, [])
  console.log('ok sinal no seletor da barra: 2 dias com aviso, amanhã e hoje sem; 1280/390; pedido criado sem aviso; zero envio de pedido')
} finally {
  await foto(page, '99-estado-final')
  writeFileSync(join(pasta, 'estado-final.txt'), await page.locator('body').innerText())
  writeFileSync(join(pasta, 'indice.json'), JSON.stringify(indice, null, 2))
  writeFileSync(join(pasta, 'diagnostico.json'), JSON.stringify({ chamadas, erros }, null, 2))
  await browser.close()
}
