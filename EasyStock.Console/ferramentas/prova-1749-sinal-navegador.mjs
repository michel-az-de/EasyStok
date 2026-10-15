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
const aviso = page.getByText('Maquininha na entrega: Encomenda para outro dia: sinal de 50% antes da produção. Restante na entrega.', { exact: true })
const escolherDia = async data => {
  await Promise.all([
    page.waitForResponse(r => r.url().includes('/comanda/janelas?') && new URL(r.url()).searchParams.get('dataInicio') === data),
    page.getByLabel('Dia da entrega (opcional)', { exact: true }).fill(data),
  ])
  const diaMes = data.slice(8, 10) + '/' + data.slice(5, 7)
  await page.getByRole('button', { name: new RegExp(diaMes + '.*Janela de prova') }).click()
}
try {
  await page.goto(origem + '/#/m/atendimento/balcao')
  await page.getByText('Maria', { exact: true }).first().waitFor()
  await page.getByRole('button', { name: 'Adicionar itens', exact: true }).click()
  await page.getByRole('button', { name: /^Lasanha Bolonhesa Clássica 600 g/ }).click()
  await page.getByRole('button', { name: 'Fechar cardápio', exact: true }).click()
  await escolherDia(amanha)
  assert.equal(await aviso.count(), 0, 'A escolha normal ainda sem meio não duplica o aviso da barra')
  await page.getByRole('radio', { name: 'Maquininha na entrega', exact: true }).click()
  if (process.env.ANTES) {
    assert.equal(await aviso.count(), 0)
    await page.getByRole('radio', { name: 'Maquininha na entrega', exact: true }).scrollIntoViewIfNeeded()
  } else { await aviso.waitFor(); await aviso.scrollIntoViewIfNeeded() }
  await foto(page, '01-maquininha-futuro-1280')
  await page.setViewportSize({ width: 390, height: 844 })
  await page.getByRole('tab', { name: 'Pedido', exact: true }).click()
  if (process.env.ANTES) await page.getByRole('radio', { name: 'Maquininha na entrega', exact: true }).scrollIntoViewIfNeeded()
  else await aviso.scrollIntoViewIfNeeded()
  await foto(page, '02-maquininha-futuro-390')
  if (!process.env.ANTES) {
  const box = await aviso.boundingBox()
  assert.ok(box.x >= 0 && box.x + box.width <= 390, 'O aviso cabe na coluna do celular')
  for (const meio of ['Pix', 'Cartão por link', 'Vale-refeição · na maquininha']) {
    await page.getByRole('radio', { name: meio, exact: true }).click()
    assert.equal(await aviso.count(), 0, 'Outro meio não mostra aviso da maquininha: ' + meio)
  }
  await page.getByRole('radio', { name: 'Maquininha na entrega', exact: true }).click()
  await escolherDia(hoje)
  assert.equal(await aviso.count(), 0, 'Entrega hoje não mostra aviso de sinal')
  await foto(page, '03-maquininha-hoje-390')
  // Outra conversa começa sem meio: o seletor da barra precisa avisar antes de seu clique de envio.
  await page.setViewportSize({ width: 1280, height: 720 })
  await page.getByText('Joana', { exact: true }).first().click()
  await page.getByRole('button', { name: 'Adicionar itens', exact: true }).click()
  await page.getByRole('button', { name: /^Lasanha Bolonhesa Clássica 600 g/ }).click()
  await page.getByRole('button', { name: 'Fechar cardápio', exact: true }).click()
  await escolherDia(amanha)
  await page.getByRole('button', { name: 'Gerar cobrança e enviar', exact: true }).click()
  await aviso.waitFor()
  assert.equal(await aviso.count(), 1, 'Um aviso no picker da barra antes de gerar')
  await aviso.scrollIntoViewIfNeeded()
  await foto(page, '04-picker-antes-gerar-1280')
  await page.setViewportSize({ width: 390, height: 844 })
  await page.getByRole('tab', { name: 'Pedido', exact: true }).click()
  // A troca de layout remonta a barra: reabrimos seu seletor pela ação real.
  await page.getByRole('button', { name: 'Gerar cobrança e enviar', exact: true }).click()
  await aviso.scrollIntoViewIfNeeded()
  await foto(page, '05-picker-antes-gerar-390')
  pedidoCriado = true
  await page.reload()
  await page.getByRole('tab', { name: 'Pedido', exact: true }).click()
  await page.getByRole('button', { name: 'Ver cardápio', exact: true }).waitFor()
  assert.equal(await aviso.count(), 0, 'Pedido criado não mostra aviso de geração')
  await foto(page, '06-pedido-criado-sem-aviso-390')
  assert.equal(chamadas.filter(c => c.metodo !== 'GET').every(c => c.path.endsWith('/marcar-lida')), true, 'Nenhum pedido foi gerado pela prova')
  assert.deepEqual(erros, [])
  console.log('ok sinal antes de gerar: seleção normal e picker da barra; hoje/outros meios sem aviso; 1280/390; zero envio de pedido')
  }
} finally {
  await foto(page, '99-estado-final')
  writeFileSync(join(pasta, 'estado-final.txt'), await page.locator('body').innerText())
  writeFileSync(join(pasta, 'indice.json'), JSON.stringify(indice, null, 2))
  writeFileSync(join(pasta, 'diagnostico.json'), JSON.stringify({ chamadas, erros }, null, 2))
  await browser.close()
}
