// Console real em1280x720 e390px; HTTP controlado, sem chamadas à loja.
// PLAYWRIGHT_MODULE deve apontar para um runtime instalado de Playwright.
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync, readFileSync } from 'node:fs'
import assert from 'node:assert/strict'
import { join, resolve } from 'node:path'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(process.env.EVIDENCIAS_UX ?? resolve(import.meta.dirname, '../../.build/1743/depois'))
const origem = process.env.CONSOLE_URL ?? 'http://127.0.0.1:5756'
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
const itens = JSON.parse(readFileSync(resolve(import.meta.dirname, '../../.build/1743/menu-publico-auditoria.json'), 'utf8')).itens
const browser = await chromium.launch({ headless: true })
const context = await browser.newContext({ viewport: { width: 1280, height: 720 }, reducedMotion: 'reduce' })
await context.addInitScript(({ empresa, usuario }) => {
  sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'prova-controlada', expiraEm: Date.now() + 3600000,
    empresa: { id: empresa, nome: 'Empresa de teste' }, usuario: { id: usuario, nome: 'Dona de teste' } }))
}, { empresa, usuario })
const chamadas = [], erros = []
let pedidoCriado = false, nativa = false
const pratoNativo = { ...itens[0], id: 'nativo-controlado', nome: 'Lasanha Bolonhesa Clássica', pesoExibicao: '', estoqueAtual: null }
await context.route('**/api/**', async route => {
  const path = new URL(route.request().url()).pathname
  if (!path.startsWith('/api/')) return route.continue()
  chamadas.push({ path, metodo: route.request().method() })
  const ok = data => route.fulfill({ json: { data } })
  if (path === '/api/auth/me/modulos') return ok({ modulos: ['atendimento', 'cardapio'].map(id => ({ id, liberado: true })), acoes: { editarAtendimento: true, editarCardapio: true } })
  if (path === '/api/atendimento/conversas') return ok(conversas.map((c, i) => ({ ...c, ...(pedidoCriado ? { pedidoEmAndamentoId: 'pedido-prova-' + i, pedidoStatus: 'aguardando_pagamento' } : {}) })))
  if (path === '/api/atendimento/atendentes') return ok([{ id: usuario, nome: 'Dona de teste' }])
  if (path === '/api/atendimento/comanda/cardapio') return ok({ itens: nativa ? [...itens, pratoNativo] : itens,
    porcoes: nativa ? { [pratoNativo.id]: [{ id: 'porcao-controlada', rotulo: '300 g', preco: 28, peso: '300 g', disponivel: true, padrao: true }] } : {} })
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
  if (path === '/api/atendimento/agenda') return ok({ lojaDisponivel: true, dias: [] })
  return ok([])
})
const page = await context.newPage()
page.setDefaultTimeout(10000)
page.on('pageerror', erro => erros.push(erro.message))
try {
  await page.goto(origem + '/#/m/atendimento/balcao')
  await page.getByText('Maria', { exact: true }).first().waitFor()
  await page.getByRole('button', { name: 'Adicionar itens', exact: true }).click()
  await page.getByRole('heading', { name: 'Cardápio de hoje', exact: true }).waitFor()
  const corpo = await page.evaluate(async itensDoMenu => {
    const { produtoDaApi, corpoDoPedido } = await import('/src/infra/api/comandaApi.js')
    const { agruparApresentacoes } = await import('/src/dominio/apresentacoesDoCardapio.js')
    const grupos = agruparApresentacoes(itensDoMenu.map(i => produtoDaApi(i)))
    return corpoDoPedido({ itens: grupos.flatMap(g => g.itens.map(i => ({ sku: i.sku, qtd: 1 }))) })
  }, itens)
  assert.deepEqual(corpo.itens.map(i => i.cardapioItemId).sort(), itens.map(i => i.id).sort())
  assert.equal(corpo.itens.some(i => i.variacaoId), false, 'Agrupar visualmente não converte itens em porções')
  writeFileSync(join(pasta, 'corpo-43-ids.json'), JSON.stringify(corpo, null, 2))
  await foto(page, '01-cardapio-1280')
  if (!process.env.ANTES) {
    await page.getByRole('button', { name: 'Somar Lasanha Bolonhesa Clássica 600 g na comanda', exact: true }).click()
    const tamanho = page.getByRole('button', { name: 'Somar Lasanha Bolonhesa Clássica 800 g na comanda', exact: true })
    const r = await tamanho.boundingBox(), alvo = await page.locator('#comanda-pedido').boundingBox()
    await page.mouse.move(r.x + r.width / 2, r.y + r.height / 2)
    await page.mouse.down()
    await page.mouse.move(r.x + r.width / 2 + 10, r.y + r.height / 2, { steps: 3 })
    await page.mouse.move(alvo.x + alvo.width / 2, Math.min(alvo.y + 100, 600), { steps: 18 })
    await page.getByText('Soltar aqui', { exact: true }).waitFor()
    await page.mouse.up()
    await page.getByRole('button', { name: 'Anotação para Lasanha Bolonhesa Clássica 800 g', exact: true }).waitFor()
    await page.getByRole('button', { name: 'Somar Lasanha Bolonhesa Clássica 1 kg na comanda', exact: true }).click()
    await foto(page, '01b-arrasto-tamanho-1280')
    await page.getByRole('button', { name: 'Tirar uma unidade de Lasanha Bolonhesa Clássica 800 g', exact: true }).click()
    assert.equal(await page.getByRole('button', { name: 'Anotação para Lasanha Bolonhesa Clássica 800 g', exact: true }).count(), 0)
    for (const peso of ['600 g', '1 kg']) {
      assert.equal(await page.getByRole('button', { name: 'Anotação para Lasanha Bolonhesa Clássica ' + peso, exact: true }).count(), 1,
        'Retirar 800 g preserva ' + peso)
    }
    await foto(page, '01c-retirada-800-preserva-600-1kg-1280')
    await page.getByRole('button', { name: 'Tirar uma unidade de Lasanha Bolonhesa Clássica 600 g', exact: true }).click()
    await page.getByRole('button', { name: 'Tirar uma unidade de Lasanha Bolonhesa Clássica 1 kg', exact: true }).click()
  }
  await page.setViewportSize({ width: 390, height: 844 })
  await foto(page, '02-cardapio-390')
  if (!process.env.ANTES) {
    assert.equal(await page.getByRole('list', { name: /^Apresentações de / }).count(), 17)
    const escapar = s => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
    for (const item of itens) {
      const escolha = page.getByRole('button', { name: new RegExp('^(Somar )?' + escapar(item.nome) + '( na comanda)?(?:$| )') })
      assert.equal(await escolha.count(), 1, 'Uma escolha direta para ' + item.id)
      assert.equal(await escolha.isEnabled(), item.disponivel !== false, 'Disponibilidade própria: ' + item.id)
      const box = await escolha.boundingBox()
      assert.ok(box.x >= 0 && box.x + box.width <= 390, 'Tamanho cabe em 390px: ' + item.id)
      if (item.disponivel !== false) await escolha.click()
    }
    await foto(page, '03-tamanhos-selecionados-390')
    const busca = page.getByRole('searchbox', { name: 'Buscar no cardápio', exact: true })
    await busca.fill('Lasanha Bolonhesa Clássica')
    const detalhes = page.getByRole('button', { name: 'Ingredientes e detalhes de Lasanha Bolonhesa Clássica 800 g', exact: true })
    const toque = await detalhes.boundingBox()
    assert.ok(toque.width >= 44 && toque.height >= 44, 'Detalhes têm alvo mínimo de 44×44')
    const fotoDo800 = page.getByRole('button', { name: 'Enviar a foto de Lasanha Bolonhesa Clássica 800 g ao cliente', exact: true })
    const fotoBox = await fotoDo800.boundingBox()
    assert.ok(fotoBox.width >= 44 && fotoBox.height >= 44, 'Foto tem alvo mínimo de 44×44')
    await detalhes.click()
    await page.getByText('Ingredientes', { exact: true }).waitFor()
    assert.ok(chamadas.some(c => c.path.endsWith('/' + itens.find(i => i.nome === 'Lasanha Bolonhesa Clássica 800 g').id)))
    const dados = await page.getByText('Ingredientes', { exact: true }).evaluate(el => {
      const r = el.closest('dl').getBoundingClientRect(); return { esquerda: r.x, direita: r.right }
    })
    assert.ok(dados.esquerda >= 0 && dados.direita <= 390, 'Detalhe aberto ocupa largura própria e cabe em 390px')
    await page.getByText('Ingredientes', { exact: true }).scrollIntoViewIfNeeded()
    await foto(page, '03b-detalhes-tamanho-390')
    await detalhes.click()
    await busca.fill('')
    await page.getByRole('button', { name: 'Fechar cardápio', exact: true }).click()
    await page.getByRole('tab', { name: 'Pedido', exact: true }).click()
    for (const item of itens.filter(i => i.disponivel !== false)) {
      assert.equal(await page.getByRole('button', { name: 'Anotação para ' + item.nome, exact: true }).count(), 1,
        'Cada tamanho fica no pedido com seu nome original: ' + item.id)
    }
    assert.equal(chamadas.filter(c => c.metodo !== 'GET').every(c => c.path.endsWith('/marcar-lida')), true)
    await foto(page, '04-pedido-390')
    nativa = true
    await page.reload()
    await page.getByRole('tab', { name: 'Pedido', exact: true }).click()
    await page.getByRole('button', { name: 'Adicionar itens', exact: true }).click()
    const porcoes = page.getByRole('list', { name: 'Porções de Lasanha Bolonhesa Clássica', exact: true })
    await porcoes.waitFor()
    assert.equal(await page.getByRole('list', { name: /^Apresentações de / }).count(), 17, 'A nativa continua separada')
    await porcoes.getByRole('button', { name: /^300 g/ }).click()
    await foto(page, '05-porcao-nativa-390')
    await page.getByRole('button', { name: 'Fechar cardápio', exact: true }).click()
    await page.getByRole('tab', { name: 'Pedido', exact: true }).click()
    await page.getByRole('button', { name: 'Anotação para Lasanha Bolonhesa Clássica · 300 g', exact: true }).waitFor()
    pedidoCriado = true
    await page.reload()
    await page.getByRole('tab', { name: 'Pedido', exact: true }).click()
    await page.getByRole('button', { name: 'Ver cardápio', exact: true }).click()
    for (const item of itens) {
      const escolha = page.getByRole('button', { name: new RegExp('^(Somar )?' + escapar(item.nome) + '( na comanda)?(?:$| )') })
      assert.equal(await escolha.isEnabled(), false, 'Pedido criado bloqueia ' + item.id)
    }
    assert.equal(await porcoes.getByRole('button', { name: /^300 g/ }).isEnabled(), false)
    await foto(page, '06-consulta-bloqueada-390')
    await page.setViewportSize({ width: 1280, height: 720 })
    const pedido = page.locator('#comanda-pedido')
    const pedidoAntes = await pedido.innerText()
    const escritasAntes = chamadas.filter(c => c.metodo !== 'GET').length
    const bloqueado = page.getByRole('button', { name: 'Somar Lasanha Bolonhesa Clássica 800 g na comanda', exact: true })
    await bloqueado.scrollIntoViewIfNeeded()
    const r = await bloqueado.boundingBox(), alvo = await pedido.boundingBox()
    await page.mouse.click(r.x + r.width / 2, r.y + r.height / 2)
    await page.mouse.move(r.x + r.width / 2, r.y + r.height / 2)
    await page.mouse.down()
    await page.mouse.move(r.x + r.width / 2 + 10, r.y + r.height / 2, { steps: 3 })
    await page.mouse.move(alvo.x + alvo.width / 2, Math.min(alvo.y + 100, 600), { steps: 18 })
    assert.equal(await page.getByText('Soltar aqui', { exact: true }).count(), 0, 'Comanda bloqueada não aceita arrasto')
    await page.mouse.up()
    assert.equal(await pedido.innerText(), pedidoAntes, 'Clique e arrasto bloqueados preservam os itens do pedido')
    assert.equal(chamadas.filter(c => c.metodo !== 'GET').length, escritasAntes, 'Tentativa bloqueada não escreve na API')
    await foto(page, '07-clique-arrasto-bloqueados-1280')
  }
  assert.deepEqual(erros, [])
  console.log('ok tamanhos: 43 IDs, disponibilidade própria, escolha direta e pedido sem envio fictício; 1280/390')
} finally {
  await foto(page, '99-estado-final')
  writeFileSync(join(pasta, 'estado-final.txt'), await page.locator('body').innerText())
  writeFileSync(join(pasta, 'indice.json'), JSON.stringify(indice, null, 2))
  writeFileSync(join(pasta, 'diagnostico.json'), JSON.stringify({ chamadas, erros }, null, 2))
  await browser.close()
}
