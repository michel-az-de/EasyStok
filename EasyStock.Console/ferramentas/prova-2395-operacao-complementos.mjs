// Opt-in: controles reais, JWT/PG locais e provedores controlados. Escritas positivas pela UI.
// A única tentativa direta de alteração é o cancelamento negativo por Atendimento: exige403 e pedido intacto.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { createHash } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { existsSync, mkdirSync, readFileSync, writeFileSync, readdirSync } from 'node:fs'
import { resolve } from 'node:path'

assert.equal(process.env.R4_EXECUTAR_COMPLEMENTOS, '1', 'Fixture final deve estar liberada pelo coordenador.')
const origem = process.env.CONSOLE_TEST_URL
const caminhoFixture = process.env.R4_FIXTURE
const fixture = JSON.parse(readFileSync(caminhoFixture, 'utf8'))
for (const url of [origem, fixture.api]) assert.ok(['localhost', '127.0.0.1', '[::1]'].includes(new URL(url).hostname), 'Somente loopback')
assert.match(JSON.stringify(fixture.provedores), /controlados/i)
assert.ok(fixture.conversaOperacionalId && fixture.conversaExcecoesId && fixture.produtoSemEstoque)
assert.ok(process.env.R4_BACKEND_REVISAO && /^[A-Fa-f0-9]{64}$/.test(process.env.R4_BACKEND_SHA256 ?? ''), 'Identificar o binário servido')
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const rodada = process.env.R4_RODADA_COMPLEMENTOS ?? ''
assert.match(rodada, /^[a-z0-9-]*$/)
const pasta = resolve(import.meta.dirname, `../.build/2395/real/operacao-complementos${rodada ? `-${rodada}` : ''}`)
mkdirSync(pasta, { recursive: true })
const arquivo = resolve(pasta, 'resultado.json')
const sha = v => createHash('sha256').update(v).digest('hex')
const prova = process.env.R4_CONTINUAR === '1' && existsSync(arquivo) ? JSON.parse(readFileSync(arquivo, 'utf8')) : {
  empresaId: fixture.empresaId, casos: [], pedidos: {}, versao: {
    head: execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim(),
    scriptSha256: sha(readFileSync(import.meta.filename)), fixtureSha256: sha(readFileSync(caminhoFixture)),
    api: process.env.R4_BACKEND_REVISAO, apiDllSha256: process.env.R4_BACKEND_SHA256,
  },
}
assert.equal(prova.empresaId, fixture.empresaId)
assert.equal(prova.versao.head, execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim(), 'Outra candidata exige outra rodada')
assert.equal(prova.versao.api, process.env.R4_BACKEND_REVISAO)
assert.equal(prova.versao.apiDllSha256, process.env.R4_BACKEND_SHA256)
prova.execucoes ??= []
prova.execucoes.push({ em: new Date().toISOString(), roteiroSha256: sha(readFileSync(import.meta.filename)), continua: process.env.R4_CONTINUAR === '1' })
const salvar = () => writeFileSync(arquivo, JSON.stringify(prova, null, 2))
const feito = id => prova.casos.some(c => c.id === id)
const registrar = (id, dados = {}) => { prova.casos.push({ id, ...dados }); salvar(); console.log(`ok ${id}`) }
const browser = await chromium.launch({ headless: true })
let context, page, conversaId, etapa = 'login'
const erros = [], produto = fixture.produtos[0], empresa = fixture.empresaId
const rx = texto => new RegExp(texto.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
async function ler(caminho) {
  assert.ok(caminho.startsWith('/api/'))
  return page.evaluate(async caminho => {
    const s = JSON.parse(sessionStorage.getItem('easystok.sessao') ?? localStorage.getItem('easystok.sessao'))
    const r = await fetch(caminho, { headers: { Authorization: `Bearer ${s.token}` } })
    const texto = await r.text()
    if (!r.ok) throw new Error(`GET ${caminho}: ${r.status} ${texto}`)
    return texto ? JSON.parse(texto).data : null
  }, caminho)
}
const detalhe = id => ler(`/api/pedidos/${id}?empresaId=${empresa}`)
const mensagens = id => ler(`/api/atendimento/conversas/${id}/mensagens?limite=200`)
const estoque = p => ler(`/api/estoque/${p.itemEstoqueId}?empresaId=${empresa}`)
async function foto(nome) { await page.screenshot({ path: resolve(pasta, `${nome}.png`), fullPage: true }) }
async function login(perfil, id) {
  await context?.close()
  context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, locale: 'pt-BR', reducedMotion: 'reduce' })
  page = await context.newPage(); page.setDefaultTimeout(25000); page.on('pageerror', e => erros.push(e.message))
  const pessoa = fixture.identidades.find(i => i.perfil === perfil && i.empresaId === empresa)
  assert.ok(pessoa, `Perfil real ${perfil}`)
  await page.goto(`${origem}/#/m/atendimento/balcao`)
  await page.getByLabel('E-mail', { exact: true }).fill(pessoa.email)
  await page.getByLabel('Senha', { exact: true }).fill(pessoa.senha)
  await page.getByRole('button', { name: 'Entrar', exact: true }).click()
  await selecionar(id)
}
async function selecionar(id) {
  conversaId = id
  await page.getByRole('button', { name: /^Todas\b/ }).click()
  const inbox = await ler('/api/atendimento/conversas?limite=100&pagina=1')
  const numero = inbox.find(c => c.id === id)?.numeroComanda
  assert.ok(numero)
  await page.getByPlaceholder('Buscar conversa', { exact: true }).fill(`comanda ${numero}`)
  await page.getByRole('button', { name: new RegExp(`Comanda ${numero},`) }).click()
}
async function navegar(href) {
  let link = page.locator(`a[href="${href}"]:visible`).first()
  if (!await link.count()) {
    const menu = page.getByRole('button', { name: /^Menu de / }); if (await menu.count()) await menu.click()
    link = page.locator(`a[href="${href}"]:visible`).first()
  }
  if (!await link.count()) { await page.locator('a[href="#/"]:visible').first().click(); link = page.locator(`a[href="${href}"]:visible`).first() }
  await link.click()
}
async function atendimento(id) { await navegar('#/m/atendimento'); await selecionar(id) }
async function fecharPaineis() {
  for (const nome of [/^Fechar cardápio$/, /^Recolher Cardápio/, /^Recolher Comanda/]) {
    const b = page.getByRole('button', { name: nome }); if (await b.isVisible()) await b.click()
  }
}
async function agir(caminho, metodo, gesto) {
  const esperando = page.waitForResponse(r => new URL(r.url()).pathname === caminho && r.request().method() === metodo)
  await gesto(); const r = await esperando, texto = await r.text()
  assert.ok(r.ok(), `${metodo} ${caminho}: ${r.status()} ${texto}`)
  return texto ? JSON.parse(texto).data : null
}
async function preparar(quantidade = 1, abrirConfirmacao = true) {
  await page.getByRole('button', { name: 'Adicionar itens', exact: true }).first().click()
  for (let i = 0; i < quantidade; i++) await page.getByRole('button', { name: new RegExp(`^(Somar )?${rx(produto.nome).source}`, 'i') }).first().click()
  await fecharPaineis(); await page.getByRole('button', { name: /^Revisar comanda/ }).first().click()
  await page.getByLabel('Dia da entrega (opcional)', { exact: true }).fill(fixture.dataEntrega)
  const janela = page.getByRole('button', { name: /Manhã.*vagas? livres?/ }).first()
  const janelaExibida = await janela.innerText(); await janela.click()
  await page.locator('[data-simulacao-total]').waitFor()
  const apresentacao = await page.locator('body').innerText()
  await foto(quantidade === 2 ? 'J02-proposta-duas-unidades' : 'E01-alternativa-preco-prazo')
  await fecharPaineis()
  if (abrirConfirmacao) await abrirCobranca()
  return { quantidade, dia: fixture.dataEntrega, janelaExibida, apresentacao }
}
async function abrirCobranca() {
  const aviso = page.getByRole('button', { name: 'Dispensar avisos do aparelho', exact: true }); if (await aviso.isVisible()) await aviso.click()
  await page.getByRole('button', { name: 'Gerar cobrança e enviar', exact: true }).click()
  await page.getByRole('radio', { name: 'Dinheiro na entrega', exact: true }).click()
}
async function nota(texto) {
  await page.getByRole('button', { name: 'Adicionar nota', exact: true }).click()
  const modal = page.getByRole('dialog', { name: /^Nota interna/ })
  await modal.getByRole('textbox', { name: /^Texto da nota/ }).fill(texto)
  const resposta = page.waitForResponse(r => r.request().method() === 'POST' && new URL(r.url()).pathname.endsWith('/notas'))
  await modal.getByRole('button', { name: 'Fixar nota', exact: true }).click(); const r = await resposta; assert.ok(r.ok()); return (await r.json()).data
}
async function posVenda() { await page.getByRole('button', { name: /^Pós-venda\b/ }).first().click(); return page.getByRole('dialog').last() }
async function secao(modal, nome) { const b = modal.getByRole('button', { name: new RegExp(`^${nome}`) }).first(); if (await b.getAttribute('aria-expanded') === 'false') await b.click() }
async function receber(id) {
  await page.getByRole('button', { name: /^Pagamento\b/ }).first().click()
  const modal = page.getByRole('dialog', { name: /^Pagamento\b/ })
  await modal.getByRole('button', { name: 'Marcar pago', exact: true }).click()
  await modal.getByLabel('Como recebeu', { exact: true }).selectOption('dinheiro')
  await agir(`/api/pedidos/${id}/pagamentos`, 'POST', () => modal.getByRole('button', { name: 'Marcar pago', exact: true }).click())
  return modal
}
async function abrirAssistente() {
  const painel = page.getByRole('region', { name: 'Assistente', exact: true })
  if (!await painel.isVisible()) await page.getByRole('button', { name: 'Abrir assistente', exact: true }).click()
  return painel
}
async function assumir() {
  const botao = page.getByRole('button', { name: 'Assumir conversa', exact: true })
  if (!await botao.isVisible()) return
  const confirmarNativo = async d => { assert.equal(d.type(), 'confirm'); assert.match(d.message(), /Confirme para assumir/); await d.accept() }
  page.on('dialog', confirmarNativo)
  try {
    await botao.click()
    const modal = page.getByRole('dialog', { name: 'Assumir esta conversa?', exact: true })
    if (await modal.isVisible()) await modal.getByRole('button', { name: 'Assumir conversa', exact: true }).click()
    await botao.waitFor({ state: 'hidden' })
  } finally { page.off('dialog', confirmarNativo) }
}
const modulos = ['src/aplicacao/api/comanda.js', 'src/infra/api/comandaApi.js', 'src/features/ficha-cliente/ExtratoDoPedido.jsx', 'src/features/gestao/caixa/AbaCaixaApi.jsx', 'src/features/cozinha/RegistrarAtraso.jsx', 'src/features/agente/PainelAgente.jsx']
const hashDoConteudo = sha
async function hashesServidos() {
  const resposta = await page.request.get(origem + '/'); assert.ok(resposta.ok())
  const html = await resposta.text()
  const meta = [...html.matchAll(/<meta\b[^>]*>/gi)].map(m => m[0]).find(m => /name=["']easystok-console-sha["']/i.test(m))
  const fontesLocais = Object.fromEntries(modulos.map(m => [m, hashDoConteudo(readFileSync(m))]))
  if (meta) {
    const shaDeclarado = meta.match(/content=["']([^"']+)["']/i)?.[1]
    assert.equal(shaDeclarado, execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim(), 'HTML deve declarar o HEAD da candidata')
    const nomes = readdirSync('dist/assets').filter(n => /\.(js|css)$/.test(n)).sort()
    assert.ok(nomes.length > 0, 'Build local precisa dos assets publicados')
    const assetsServidos = {}
    for (const nome of nomes) {
      const r = await page.request.get(`${origem}/assets/${nome}`); assert.ok(r.ok())
      const servido = hashDoConteudo(await r.body()), local = hashDoConteudo(readFileSync(`dist/assets/${nome}`))
      assert.equal(servido, local, `Asset servido deve ser idêntico ao build local: ${nome}`)
      assetsServidos[nome] = { servido, local }
    }
    return { modo: 'build', shaDeclarado, htmlSha256: hashDoConteudo(html), fontesLocais, assetsServidos }
  }
  assert.notEqual(process.env.R4_CONSOLE_BUILD, '1', 'Build sem meta easystok-console-sha não serve como prova de candidata')
  const modulosServidos = {}
  for (const modulo of modulos) {
    const r = await page.request.get(`${origem}/${modulo}`); assert.ok(r.ok())
    assert.match(r.headers()['content-type'] ?? '', /javascript/, 'Vite dev deve servir JavaScript; HTML de fallback não é um módulo')
    modulosServidos[modulo] = hashDoConteudo(await r.body())
  }
  return { modo: 'dev', fontesLocais, modulosServidos }
}
try {
  await login('Dona', fixture.conversaOperacionalId)
  const versaoInicial = await hashesServidos()
  if (prova.versao.inicial) assert.deepEqual(versaoInicial, prova.versao.inicial, 'Continuação deve usar a mesma fonte servida')
  else prova.versao.inicial = versaoInicial
  salvar()
  etapa = 'J02'
  if (!prova.pedidos.operacional) {
    assert.equal(await ler(`/api/atendimento/conversas/${conversaId}/pedido`), null)
    const saldoAntes = await estoque(produto), caminho = `/api/atendimento/conversas/${conversaId}/pedido`
    prova.propostaOperacional = await preparar(2)
    let chegou, liberar, posts = 0
    const persistido = new Promise(r => { chegou = r }), retido = new Promise(r => { liberar = r })
    await page.route(`**${caminho}`, async rota => {
      if (rota.request().method() !== 'POST') return rota.continue()
      posts++
      const r = await rota.fetch(), corpo = await r.json(); assert.ok(r.ok())
      prova.pedidos.operacional = corpo.data.pedidoId; prova.confirmacao = { pedido: corpo.data, corpo: rota.request().postDataJSON(), posts, saldoAntes: saldoAntes.qty }; salvar(); chegou()
      await retido; await rota.abort('failed').catch(() => {})
    })
    await page.getByRole('button', { name: 'Confirmar e enviar', exact: true }).dblclick()
    await persistido; assert.equal(posts, 1); await foto('J02-confirmacao-sem-resposta')
    const recarga = page.reload(); liberar(); await recarga; await page.unroute(`**${caminho}`); await selecionar(conversaId)
    const confirmado = await ler(`/api/atendimento/conversas/${conversaId}/pedido`)
    assert.equal(confirmado.pedidoId, prova.pedidos.operacional); assert.equal(posts, 1)
    assert.equal(confirmado.total, 69); assert.equal(confirmado.frete, 5)
    assert.deepEqual(prova.confirmacao.corpo.itens.map(i => ({ cardapioItemId: i.cardapioItemId, qtd: i.qtd })), [{ cardapioItemId: produto.cardapioItemId, qtd: 2 }])
    assert.equal(confirmado.total - confirmado.frete, 64, 'Duas unidades de 32: subtotal 64, frete 5 e total 69')
    assert.equal((await estoque(produto)).qty, saldoAntes.qty, 'Confirmar não baixa estoque')
    const cliente = await ler(`/api/clientes/${prova.confirmacao.corpo.clienteId}?empresaId=${empresa}`)
    assert.equal(cliente.cliente.orderCount, 1, 'Pessoa exclusiva tem um pedido após duplo gesto e reload')
    await foto('J02-reload-mesmo-pedido'); registrar('J02', { pedidoId: confirmado.pedidoId, posts, orderCount: cliente.cliente.orderCount, estoque: saldoAntes.qty })
  }
  if (!feito('J02')) {
    const confirmado = await ler(`/api/atendimento/conversas/${conversaId}/pedido`)
    const dossie = await ler(`/api/clientes/${prova.confirmacao.corpo.clienteId}?empresaId=${empresa}`)
    assert.equal(confirmado.pedidoId, prova.pedidos.operacional)
    assert.equal(prova.confirmacao.posts, 1); assert.equal(dossie.cliente.orderCount, 1)
    assert.equal(confirmado.total, 69); assert.equal(confirmado.frete, 5)
    assert.deepEqual(prova.confirmacao.corpo.itens.map(i => ({ cardapioItemId: i.cardapioItemId, qtd: i.qtd })), [{ cardapioItemId: produto.cardapioItemId, qtd: 2 }])
    assert.equal((await estoque(produto)).qty, prova.confirmacao.saldoAntes)
    await foto('J02-reload-mesmo-pedido')
    registrar('J02', { pedidoId: confirmado.pedidoId, posts: prova.confirmacao.posts, orderCount: dossie.cliente.orderCount,
      estoque: prova.confirmacao.saldoAntes, continuacaoSomenteLeitura: true })
  }
  etapa = 'J05'
  if (!feito('J05')) {
    const id = prova.pedidos.operacional
    const dAntes = await detalhe(id)
    let modal
    if (dAntes.pedido.totalPago === 0) modal = await receber(id)
    else { await page.getByRole('button', { name: /^Pagamento\b/ }).first().click(); modal = page.getByRole('dialog', { name: /^Pagamento\b/ }) }
    const d = await detalhe(id), pagamento = d.pagamentos[0]
    assert.equal(d.pedido.totalPago, d.pedido.total); assert.ok(pagamento.registradoPorNome)
    await modal.getByText(pagamento.registradoPorNome, { exact: false }).first().waitFor(); await foto('J05-extrato-autoria')
    await modal.getByRole('button', { name: 'Fechar janela', exact: true }).click()
    await navegar('#/m/financeiro/caixa')
    const caixa = await ler('/api/caixa/dia'), linha = caixa.linhasExtras.find(l => l.pedidoNumeroDoDia === d.pedido.numeroDoDia)
    assert.ok(linha); assert.equal(linha.valor, d.pedido.total); assert.equal(linha.registradoPorNome, pagamento.registradoPorNome)
    const pessoaDoPedido = await ler(`/api/clientes/${d.pedido.clienteId}?empresaId=${empresa}`)
    assert.equal(linha.clienteNome, pessoaDoPedido.cliente.nome, 'A linha do Caixa pertence à pessoa exclusiva deste pedido')
    await page.getByText(pagamento.registradoPorNome, { exact: false }).first().waitFor(); await foto('J05-caixa-autoria')
    registrar('J05', { pedido: d.pedido, pagamento, linha, totaisDisponiveis: { vendas: caixa.totalVendas, recebido: caixa.totalPagamentosPedidos, entradasExtras: caixa.totalEntradasExtras, saidasExtras: caixa.totalSaidasExtras, saldoEsperado: caixa.saldoEsperado }, limite: 'Campos reais exibidos; nenhum custo ou taxa ausente foi estimado.' })
    const fechar = page.getByRole('button', { name: 'Fechar janela', exact: true }); if (await fechar.isVisible()) await fechar.click()
  }
  etapa = 'E01'
  await atendimento(fixture.conversaExcecoesId)
  if (!feito('E01')) {
    assert.equal((await estoque(fixture.produtoSemEstoque)).qty, 0)
    assert.equal(await ler(`/api/atendimento/conversas/${conversaId}/pedido`), null)
    const msgsAntes = await mensagens(conversaId)
    await page.getByRole('button', { name: 'Adicionar itens', exact: true }).first().click()
    await page.getByRole('button', { name: new RegExp(`^(Somar )?${rx(fixture.produtoSemEstoque.nome).source}`, 'i') }).first().click()
    await page.getByText(/Sem saldo, mas ainda vende\. No lugar dele:/).waitFor(); await foto('E01-falta-antes-promessa')
    await fecharPaineis(); await page.getByRole('button', { name: /^Revisar comanda/ }).first().click()
    await page.getByRole('button', { name: 'Menos um', exact: true }).click(); await fecharPaineis()
    const alternativa = await preparar(1, false)
    assert.match(alternativa.apresentacao, /R\$\s*32,00/); assert.match(alternativa.apresentacao, /R\$\s*5,00/); assert.match(alternativa.apresentacao, /R\$\s*37,00/)
    assert.match(alternativa.janelaExibida, /Manhã/)
    const escolhaSimulada = `R4 E01, ensaio: escolha do cliente SIMULADA. Aceitar 1 ${produto.nome}: itens R$32, frete R$5, total R$37; entrega ${fixture.dataEntrega}, Manhã, conferida na UI. Responsável Dona retirou o produto sem saldo e pode prosseguir com esta alternativa.`
    prova.escolhaSimulada = await nota(escolhaSimulada)
    prova.alternativaPronta = alternativa; salvar()
    assert.deepEqual(await mensagens(conversaId), msgsAntes); assert.equal(await ler(`/api/atendimento/conversas/${conversaId}/pedido`), null)
    registrar('E01', { produtoId: fixture.produtoSemEstoque.produtoId, saldo: 0, decisao: 'Escolha do cliente explicitamente SIMULADA aceita alternativa de37 com preço e prazo conferidos; nenhum pedido até a decisão.', alternativa: prova.alternativaPronta, notaSimulacao: prova.escolhaSimulada, politica: 'Venda sem saldo permitida com aviso pelo produto existente, sem bloqueio inventado.' })
  }
  if (!prova.pedidos.excecoes && feito('E01')) {
    const existente = await ler(`/api/atendimento/conversas/${conversaId}/pedido`)
    if (existente) {
      assert.equal(existente.total, 37); assert.equal(existente.frete, 5)
      prova.pedidos.excecoes = existente.pedidoId; prova.recuperacaoPedidoExcecoes = existente; salvar()
    }
  }
  if (!prova.pedidos.excecoes) {
    assert.ok(prova.alternativaPronta, 'Alternativa deve ter sido conferida e aceita de forma SIMULADA')
    await abrirCobranca()
    const p = await agir(`/api/atendimento/conversas/${conversaId}/pedido`, 'POST', () => page.getByRole('button', { name: 'Confirmar e enviar', exact: true }).click())
    prova.pedidos.excecoes = p.pedidoId; salvar(); assert.equal(p.total, 37)
    const confirmado = await ler(`/api/atendimento/conversas/${conversaId}/pedido`)
    assert.equal(confirmado.pedidoId, p.pedidoId); assert.equal(confirmado.frete, 5)
  }
  etapa = 'E02'
  if (!feito('E02')) {
    const id = prova.pedidos.excecoes
    const numeroComanda = (await ler('/api/atendimento/conversas?limite=100&pagina=1')).find(c => c.id === conversaId).numeroComanda
    await navegar('#/m/cozinha'); await page.getByRole('radio', { name: 'Amanhã', exact: true }).click()
    const card = page.getByRole('listitem').filter({ hasText: new RegExp(`Comanda ${numeroComanda}(?=\\D|$)`) }).filter({ has: page.getByRole('button', { name: 'Ver comanda', exact: true }) })
    await card.getByRole('button', { name: 'Ver comanda', exact: true }).click()
    const modal = page.getByRole('dialog')
    await modal.getByRole('button', { name: 'Registrar atraso', exact: true }).click()
    const motivo = `R4 E02 origem Cozinha, comanda ${numeroComanda}, pedido ${id}: preparo atrasado; Atendimento deve conferir novo horário antes de prometer.`
    await modal.getByLabel('O que atrasou?', { exact: true }).fill(motivo)
    await foto('E02-comanda-na-cozinha')
    const ocorrencia = await agir('/api/ocorrencias', 'POST', () => modal.getByRole('button', { name: 'Salvar ocorrência', exact: true }).click())
    await modal.getByRole('status').filter({ hasText: 'Atraso registrado.' }).waitFor(); await foto('E02-atraso-registrado-na-origem')
    await modal.getByRole('button', { name: 'Fechar janela', exact: true }).click(); await atendimento(fixture.conversaExcecoesId); await page.reload(); await selecionar(conversaId)
    const inbox = await ler('/api/atendimento/conversas?limite=100&pagina=1'), c = inbox.find(c => c.id === conversaId)
    assert.equal(c.situacao, 'Assumida'); assert.equal(c.assumidaPorUsuarioId, null); assert.ok(c.motivoEscalada.includes(motivo))
    await page.getByRole('button', { name: /^Precisa de você/ }).click()
    await page.getByRole('button', { name: new RegExp(`Comanda ${numeroComanda},`) }).waitFor()
    await page.getByText(rx(motivo)).first().waitFor(); await foto('E02-origem-cozinha-fila')
    await page.getByRole('button', { name: /^Todas\b/ }).click()
    prova.ocorrenciaAtraso = ocorrencia; salvar()
    await assumir()
    const antes = await ler(`/api/pedidos/${id}/janelas?dataInicio=${fixture.dataEntrega}&dataFim=${fixture.dataEntrega}`)
    await page.getByRole('button', { name: /^Revisar comanda/ }).first().click()
    const janela = page.getByRole('button', { name: /^Janela de entrega/ }).first()
    if (await janela.count() && await janela.getAttribute('aria-expanded') === 'false') await janela.click()
    await page.getByLabel('Dia da nova entrega (opcional)', { exact: true }).fill(fixture.dataEntrega)
    const escolha = page.getByLabel('Nova janela de entrega', { exact: true })
    const valor = await escolha.locator('option').filter({ hasText: 'Tarde' }).getAttribute('value'); assert.ok(valor)
    await escolha.selectOption(valor)
    await context.setOffline(true)
    try {
      await page.getByRole('button', { name: 'Confirmar agendamento', exact: true }).click()
      await page.getByText(/Não foi possível confirmar a troca/).waitFor(); await foto('E02-reagendamento-falha-preservada')
    } finally { await context.setOffline(false) }
    assert.deepEqual((await ler(`/api/pedidos/${id}/janelas`)).atual, antes.atual)
    assert.equal(await escolha.inputValue(), valor, 'Falha preserva escolha para repetir')
    await agir(`/api/pedidos/${id}/janela`, 'PATCH', () => page.getByRole('button', { name: 'Confirmar agendamento', exact: true }).click())
    const depois = await ler(`/api/pedidos/${id}/janelas?dataInicio=${fixture.dataEntrega}&dataFim=${fixture.dataEntrega}`)
    assert.notDeepEqual(depois.atual, antes.atual)
    const vaga = (lista, atual) => lista.find(j => j.janelaId === atual.janelaId && j.data === atual.data)
    assert.equal(vaga(depois.janelas, antes.atual).vagasRestantes, vaga(antes.janelas, antes.atual).vagasRestantes + 1)
    assert.equal(vaga(depois.janelas, depois.atual).vagasRestantes, vaga(antes.janelas, depois.atual).vagasRestantes - 1)
    prova.agendaExcecoes = depois.atual; salvar(); await fecharPaineis()
    await navegar('#/m/cozinha'); await page.getByRole('radio', { name: 'Amanhã', exact: true }).click()
    await card.getByText(/Tarde/).waitFor(); await foto('E02-cozinha-reagendada')
    await navegar('#/m/entregas'); await page.getByRole('tab', { name: 'Painel do dia', exact: true }).click(); await page.getByRole('radio', { name: 'Amanhã', exact: true }).click()
    await page.getByRole('region', { name: /Janela.*Tarde/ }).getByText(new RegExp(`Comanda ${numeroComanda}(?=nº|\\s|$)`)).first().waitFor()
    await foto('E02-entregas-reagendada')
    await atendimento(fixture.conversaExcecoesId)
    await page.getByText(/13:00 às 16:00|13h–16h/).first().waitFor()
    registrar('E02', { ocorrencia, fila: c, antes: antes.atual, depois: depois.atual, capacidadeAntes: antes.janelas, capacidadeDepois: depois.janelas,
      recuperacao: 'Falha offline preservou janela antiga e escolha; repetição confirmou mudança única; Cozinha, Entregas e Atendimento concordam.', origemUI: 'Dona na Cozinha → Ver comanda → Registrar atraso → confirmação API → fila Atendimento da mesma conversa.', perfil: 'Dona, com capability gerenciarOcorrencias; perfil Cozinha não recebe privilégio gerencial.' })
  }
  etapa = 'A01'
  if (!feito('A01')) {
    await assumir()
    const id = prova.pedidos.excecoes, antes = await detalhe(id), msgs = await mensagens(conversaId)
    const campo = page.getByRole('textbox', { name: 'Mensagem para o cliente', exact: true }), rascunho = await campo.inputValue()
    const caminho = `/api/atendimento/conversas/${conversaId}/sugestao`, painel = await abrirAssistente()
    const sugestao = await agir(caminho, 'POST', () => painel.getByRole('button', { name: 'Sugerir resposta', exact: true }).click())
    await painel.getByRole('button', { name: 'Descartar', exact: true }).waitFor(); assert.match(sugestao.texto, /Confira os dados atuais da sua comanda:/)
    const pedidoConsultado = JSON.parse(sugestao.texto.slice(sugestao.texto.indexOf('{')))
    const reais = valor => Number(String(valor).replace(/[^\d,.-]/g, '').replace(/\./g, '').replace(',', '.'))
    assert.equal(pedidoConsultado.pedidoId, id)
    assert.equal(pedidoConsultado.status, antes.pedido.status)
    assert.equal(reais(pedidoConsultado.total), antes.pedido.total)
    assert.equal(reais(pedidoConsultado.pago), antes.pedido.totalPago)
    assert.equal(reais(pedidoConsultado.faltaPagar), antes.pedido.total - antes.pedido.totalPago)
    assert.ok(reais(pedidoConsultado.faltaPagar) > 0, 'Impedimento financeiro preparado e confirmado pela ferramenta real')
    // SugerirRespostaAgenteUseCase aplica TextoWhatsApp.SemTravessao no texto final.
    assert.deepEqual(pedidoConsultado.itens.map(i => ({ nome: i.nome, quantidade: i.quantidade })),
      antes.itens.map(i => ({ nome: i.nome.replace(/\s+[—–]\s+/g, ', '), quantidade: i.quantidade })),
      'Itens reais, incluindo entrega, após o acabamento TextoWhatsApp.SemTravessao')
    assert.deepEqual(await detalhe(id), antes); assert.deepEqual(await mensagens(conversaId), msgs); assert.equal(await campo.inputValue(), rascunho)
    await foto('A01-previa-sem-efeitos'); await painel.getByRole('button', { name: 'Descartar', exact: true }).click()
    await page.route(`**${caminho}`, rota => rota.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ error: { code: 'AGENTE_INDISPONIVEL', message: 'Assistente indisponível na prova controlada.', details: null, traceId: null } }) }))
    await painel.getByRole('button', { name: 'Sugerir resposta', exact: true }).click(); await painel.getByRole('alert').waitFor(); await foto('A01-503-fallback')
    await page.unroute(`**${caminho}`)
    let chegou, liberar, terminou
    const respostaPronta = new Promise(r => { chegou = r }), retida = new Promise(r => { liberar = r }), respostaEncerrada = new Promise(r => { terminou = r })
    await page.route(`**${caminho}`, async rota => { const resposta = await rota.fetch(); chegou(); await retida; await rota.fulfill({ response: resposta }).catch(() => {}); terminou() })
    await painel.getByRole('button', { name: 'Tentar novamente', exact: true }).click(); await respostaPronta
    await selecionar(fixture.conversaOperacionalId); liberar(); await respostaEncerrada; await page.unroute(`**${caminho}`)
    assert.equal(await page.getByRole('button', { name: 'Usar no rascunho', exact: true }).count(), 0, 'Resposta antiga não oferece aplicação na outra conversa')
    await selecionar(fixture.conversaExcecoesId)
    await campo.fill('Conferi manualmente o atraso. Vou confirmar o novo horário antes de prometer a entrega.')
    assert.deepEqual(await detalhe(id), antes); assert.deepEqual(await mensagens(conversaId), msgs); await foto('A01-manual-sem-envio')
    registrar('A01', { sugestao, pedidoConsultado, pedidoId: id, impedimento: 'Saldo financeiro pendente real; consultar_pedido não expõe a ocorrência de atraso e a prova não lhe atribui essa informação.', controles: ['prévia', 'descarte', '503 controlado no código real', 'resposta real retida até trocar conversa', 'rascunho manual'], efeitos: 'Pedido e mensagens iguais antes/depois; sem inferência externa, LLM controlado chama consultar_pedido real.' })
  }
  etapa = 'E03'
  if (!feito('E03')) {
    const id = prova.pedidos.excecoes
    if ((await detalhe(id)).pedido.totalPago === 0) { const m = await receber(id); await m.getByRole('button', { name: 'Fechar janela', exact: true }).click() }
    const antes = await detalhe(id)
    await login('Atendimento', fixture.conversaExcecoesId)
    await page.getByRole('button', { name: 'Ações da ficha', exact: true }).click(); assert.equal(await page.getByRole('menuitem', { name: 'Cancelar pedido', exact: true }).count(), 0); await page.keyboard.press('Escape')
    const negativa = await page.evaluate(async ({ id, empresa }) => {
      const sessao = JSON.parse(sessionStorage.getItem('easystok.sessao') ?? localStorage.getItem('easystok.sessao'))
      const r = await fetch(`/api/pedidos/${id}/cancelar`, { method: 'POST', headers: { Authorization: `Bearer ${sessao.token}`, 'Content-Type': 'application/json' }, body: JSON.stringify({ empresaId: empresa, id, motivo: 'Ensaio negativo R4: Atendente não pode cancelar pedido pago.' }) })
      return { status: r.status, corpo: await r.json() }
    }, { id, empresa })
    assert.equal(negativa.status, 403); assert.deepEqual(await detalhe(id), antes, 'Tentativa direta negada não altera pedido')
    const solicitacao = await nota(`R4 E03 pedido ${id}: solicito decisão da gerente sobre cancelar pedido pago por atraso. Aguardar avaliação antes de prometer devolução.`)
    await login('Dona', fixture.conversaExcecoesId)
    const recusa = `R4 E03 pedido ${id}: recuso cancelamento imediato; confirmar com cliente o novo horário. Próximo passo: Atendimento consultar disponibilidade e retornar antes de alterar pedido.`
    const notaRecusa = await nota(recusa); assert.deepEqual(await detalhe(id), antes)
    await login('Atendimento', fixture.conversaExcecoesId)
    await page.getByRole('button', { name: /^Notas\b/ }).first().click()
    await page.getByRole('article').filter({ hasText: recusa }).getByRole('button', { name: 'Destacar nota', exact: true }).click()
    await page.getByRole('complementary', { name: /^Nota ·/ }).filter({ hasText: recusa }).waitFor()
    await foto('E03-recusa-proximo-passo')
    await assumir()
    await page.getByRole('textbox', { name: 'Mensagem para o cliente', exact: true }).fill('Vou conferir o novo horário com você antes de alterar o pedido. A gerente pediu essa confirmação.')
    assert.deepEqual(await detalhe(id), antes)
    const recusaPersistida = await ler(`/api/clientes/${antes.pedido.clienteId}/notas/${notaRecusa.id}`)
    const solicitacaoPersistida = await ler(`/api/clientes/${antes.pedido.clienteId}/notas/${solicitacao.id}`)
    assert.equal(recusaPersistida.nota.texto, recusa)
    assert.equal(recusaPersistida.nota.autor, fixture.identidades.find(i => i.perfil === 'Dona' && i.empresaId === empresa).nome)
    assert.equal(solicitacaoPersistida.nota.autor, fixture.identidades.find(i => i.perfil === 'Atendimento' && i.empresaId === empresa).nome)
    registrar('E03', { pedidoId: id, recusa, negativa, recusaPersistida, solicitacaoPersistida, limite: 'Decisão e próximo passo em nota contextual; sem alegar workflow formal de aprovação.' })
  }
  etapa = 'E04'
  if (!feito('E04')) {
    await login('Dona', fixture.conversaExcecoesId)
    const id = prova.pedidos.excecoes, antes = await detalhe(id)
    const numeroComanda = (await ler('/api/atendimento/conversas?limite=100&pagina=1')).find(c => c.id === conversaId).numeroComanda
    prova.estoqueAntesPronto ??= (await estoque(produto)).qty; salvar()
    assert.ok(prova.estoqueAntesPronto >= 1)
    if (antes.pedido.status !== 'cancelado') {
    await navegar('#/m/cozinha'); await page.getByRole('radio', { name: 'Amanhã', exact: true }).click()
    const card = page.getByRole('listitem').filter({ hasText: new RegExp(`Comanda ${numeroComanda}(?=nº|\\s|$)`) }).filter({ has: page.getByRole('button', { name: 'Ver comanda', exact: true }) })
    if (['aguardando', 'pago'].includes(antes.pedido.status)) {
      await agir(`/api/kds/pedidos/${id}/status`, 'PATCH', () => card.getByRole('button', { name: 'Começar preparo', exact: true }).click())
      assert.equal((await estoque(produto)).qty, prova.estoqueAntesPronto, 'Preparando não baixa')
    }
    if (antes.pedido.status !== 'pronto') await agir(`/api/kds/pedidos/${id}/status`, 'PATCH', () => card.getByRole('button', { name: 'Marcar pronto', exact: true }).click())
    prova.estoqueAposPronto = (await estoque(produto)).qty
    assert.equal(prova.estoqueAposPronto, prova.estoqueAntesPronto - 1, 'Exceções tem só 1 unidade, distinta das 2 operacionais')
    await foto('E04-pronto-saida-real'); await atendimento(fixture.conversaExcecoesId)
    }
    const vagas = () => ler(`/api/atendimento/comanda/janelas?itens=${produto.cardapioItemId}&dataInicio=${fixture.dataEntrega}&dataFim=${fixture.dataEntrega}`)
    assert.ok(prova.capacidadeAntesCancelar || antes.pedido.status !== 'cancelado', 'Cancelamento prévio sem leitura de capacidade não prova liberação')
    prova.capacidadeAntesCancelar ??= await vagas(); salvar()
    const capacidadeAntes = prova.capacidadeAntesCancelar
    const anteriorCancelamento = await detalhe(id)
    if (anteriorCancelamento.pedido.status !== 'cancelado') {
      await page.getByRole('button', { name: 'Ações da ficha', exact: true }).click(); await page.getByRole('menuitem', { name: 'Cancelar pedido', exact: true }).click()
      await page.getByLabel('Motivo do cancelamento', { exact: true }).fill('R4 E04: após recusa inicial, cliente recusou novo horário; Dona autoriza cancelamento e devolução separados.')
      await agir(`/api/pedidos/${id}/cancelar`, 'POST', () => page.getByRole('button', { name: 'Cancelar pedido', exact: true }).click())
    }
    const capacidadeDepois = await vagas()
    const janela = prova.agendaExcecoes.janelaId
    const vagaAntes = capacidadeAntes.janelas.find(j => j.janelaId === janela), vagaDepois = capacidadeDepois.janelas.find(j => j.janelaId === janela)
    assert.equal(vagaDepois.vagasRestantes, vagaAntes.vagasRestantes + 1, 'Cancelamento libera exatamente a vaga do pedido')
    assert.equal((await estoque(produto)).qty, prova.estoqueAntesPronto, 'Cancelamento devolve apenas 1 saída real')
    const modal = await posVenda(); await secao(modal, 'Conferir acerto|Devoluções')
    await modal.getByRole('button', { name: 'Registrar devolução manual', exact: true }).click()
    await modal.getByLabel('Meio usado na devolução', { exact: true }).selectOption('dinheiro')
    await modal.getByLabel('Motivo da devolução', { exact: true }).fill('R4 E04 devolução fictícia controlada após cancelamento autorizado.')
    await modal.getByRole('checkbox', { name: 'Confirmo que já devolvi este valor ao cliente.', exact: true }).check()
    const caminho = `/api/pedidos/${id}/estornos-manuais`, posts = []
    await page.route(`**${caminho}`, async rota => {
      if (rota.request().method() !== 'POST') return rota.continue()
      posts.push(rota.request().postDataJSON()); const r = await rota.fetch(); assert.ok(r.ok())
      if (posts.length === 1) await rota.abort('failed'); else await rota.fulfill({ response: r })
    })
    await modal.getByRole('button', { name: 'Confirmar devolução já realizada', exact: true }).click()
    await modal.getByRole('button', { name: 'Repetir confirmação pendente', exact: true }).waitFor(); await foto('E04-resposta-perdida')
    await agir(caminho, 'POST', () => modal.getByRole('button', { name: 'Repetir confirmação pendente', exact: true }).click()); await page.unroute(`**${caminho}`)
    assert.equal(posts.length, 2); assert.deepEqual(posts[0], posts[1]); await page.reload(); await selecionar(conversaId)
    const estornos = await ler(caminho), depois = await detalhe(id)
    assert.equal(estornos.estornos.length, 1); assert.equal(estornos.estornos[0].valor, antes.pedido.totalPago); assert.deepEqual(depois.pagamentos, antes.pagamentos)
    await navegar('#/m/financeiro/caixa'); const caixa = await ler('/api/caixa/dia')
    const devolucoes = caixa.movimentos.filter(m => m.origem === 'devolucao_pedido' && m.descricao?.includes('R4 E04'))
    assert.equal(devolucoes.length, 1); assert.equal(devolucoes[0].valor, antes.pedido.totalPago)
    await page.getByText(/R4 E04 devolução fictícia controlada/).first().waitFor(); await foto('E04-caixa-devolucao-unica')
    assert.equal((await estoque(produto)).qty, prova.estoqueAntesPronto, 'Replay da devolução não repõe estoque outra vez')
    assert.deepEqual((await vagas()).janelas, capacidadeDepois.janelas, 'Replay da devolução não libera outra vaga')
    registrar('E04', { pedidoId: id, posts, estornos, pagamentoPreservado: depois.pagamentos, devolucoes,
      estoqueAntesPronto: prova.estoqueAntesPronto, estoqueAposPronto: prova.estoqueAposPronto, estoqueFinal: (await estoque(produto)).qty,
      capacidadeAntes: capacidadeAntes.janelas, capacidadeDepois: capacidadeDepois.janelas })
  }
  prova.versao.final = await hashesServidos()
  assert.deepEqual(prova.versao.final, prova.versao.inicial, 'Fonte servida deve permanecer congelada durante a rodada')
  assert.deepEqual(erros, [])
  prova.sucesso = true; prova.erros = erros; delete prova.falha; salvar()
} catch (erro) {
  prova.sucesso = false
  if (prova.versao.inicial) {
    prova.versao.final = await hashesServidos().catch(() => null)
    prova.versao.fonteEstavel = JSON.stringify(prova.versao.inicial) === JSON.stringify(prova.versao.final)
  }
  prova.falha = { etapa, mensagem: erro.stack }; prova.erros = erros; salvar()
  if (page) { await foto(`falha-${etapa}`); writeFileSync(resolve(pasta, `falha-${etapa}.txt`), await page.locator('body').innerText()) }
  throw erro
} finally { await browser.close() }
