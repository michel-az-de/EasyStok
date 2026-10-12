// UI real do Console com API controlada. Não usa nem altera dados da loja.
// PLAYWRIGHT_MODULE aponta uma instalação existente; CONSOLE_URL aponta o Vite em modo API.
import assert from 'node:assert/strict'
import { createRequire, registerHooks } from 'node:module'
import { mkdirSync } from 'node:fs'
import { join } from 'node:path'
const require = createRequire(import.meta.url)
registerHooks({ resolve(spec, contexto, proximo) {
  if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
    try { return proximo(spec + '.js', contexto) } catch { /* resolução normal */ }
  }
  return proximo(spec, contexto)
} })
const { MODULOS, hashDoModulo, telasDoMenu } = await import('../src/dominio/modulos.js')
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const origem = process.env.CONSOLE_URL ?? 'http://127.0.0.1:5186'
const evidencias = process.env.EVIDENCIAS_UX ?? 'builds/prova-financeiro'
mkdirSync(evidencias, { recursive: true })
const empresaId = '11111111-1111-4111-8111-111111111111'
const categoriaId = '33333333-3333-4333-8333-333333333333'
const contaId = '44444444-4444-4444-8444-444444444444'
const parcelaId = '55555555-5555-4555-8555-555555555555'
const pagamentoId = '66666666-6666-4666-8666-666666666666'
const hoje = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo' }).format(new Date())
const instante = `${hoje}T12:00:00-03:00`
const conta = { id: contaId, empresaId, descricao: 'Compra de embalagens', categoriaFinanceiraId: categoriaId, centroCustoId: null, valorTotal: 100, totalPago: 0, totalRecebido: 0, pendente: 100, status: 'Aberta', dataEmissao: instante, observacoes: 'Conferir comprovante', parcelas: [{ id: parcelaId, numero: 1, valor: 100, valorPago: 0, saldo: 100, status: 'Pendente', dataVencimento: instante, pagamentos: [] }] }
const contaReceber = structuredClone(conta)
contaReceber.id = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
contaReceber.descricao = 'Venda de encomenda'
contaReceber.parcelas[0].id = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'
const acoes = Object.fromEntries(['visualizarContasPagar', 'visualizarContasReceber', 'gerenciarContasPagar', 'gerenciarContasReceber', 'gerenciarCategoriasFinanceiras', 'gerenciarCentrosCusto', 'gerenciarCaixa', 'editarProducao', 'editarCardapio', 'editarAtendimento'].map((nome) => [nome, true]))
const escritas = []
const erros = []
let falhaDeGravacao = false
let shaDoDocumento, versaoPublicada, respostaInvalida = false
const browser = await chromium.launch({ headless: true })
let paginaProva
try {
  const context = await browser.newContext({ viewport: { width: 1440, height: 960 }, reducedMotion: 'reduce' })
  await context.addInitScript(({ empresaId }) => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'teste-ui-controlado', expiraEm: Date.now() + 3600000, empresa: { id: empresaId, nome: 'Empresa de teste' }, usuario: { id: '22222222-2222-4222-8222-222222222222', nome: 'Dona de teste' } })), { empresaId })
  // Emula somente a troca de versão, mantendo o JS/CSS reais do pacote servido.
  await context.route(origem + '/**', async route => {
    const request = route.request(), caminho = new URL(request.url()).pathname
    if (caminho === '/versao.json') return route.fulfill(respostaInvalida ? { contentType: 'application/json', body: 'indisponível' } : { json: { sha: versaoPublicada } })
    if (request.resourceType() !== 'document') return route.continue()
    const resposta = await route.fetch()
    let html = await resposta.text()
    shaDoDocumento ??= html.match(/easystok-console-sha" content="([a-f0-9]{40})"/)?.[1] ?? 'a'.repeat(40)
    versaoPublicada ??= shaDoDocumento
    html = html.includes('<meta name="easystok-console-sha"')
      ? html.replace(/(easystok-console-sha" content=")[a-f0-9]{40}/, '$1' + shaDoDocumento)
      : html.replace('<head>', `<head><meta name="easystok-console-sha" content="${shaDoDocumento}">`)
    return route.fulfill({ response: resposta, body: html })
  })
  await context.route('**/api/**', async (route) => {
    const request = route.request(), url = new URL(request.url()), caminho = url.pathname, metodo = request.method()
    if (!caminho.startsWith('/api/')) return route.continue()
    const ok = (data, meta = {}) => route.fulfill({ json: { data, meta } })
    const aReceber = caminho.startsWith('/api/contas-a-receber')
    const contaAtual = aReceber ? contaReceber : conta
    if (metodo !== 'GET') {
      const corpo = request.postDataJSON()
      escritas.push({ caminho, metodo, corpo })
      if (falhaDeGravacao && caminho === '/api/contas-a-pagar') return route.fulfill({ status: 400, json: { error: { message: 'Categoria de teste indisponível' } } })
      if (caminho.startsWith('/api/contas-a-') || caminho.startsWith('/api/financeiro/')) {
        if (corpo) assert.equal(corpo.empresaId, empresaId)
        if (caminho.endsWith('/pagamentos')) {
          assert.equal(corpo.valor, 100); assert.ok(corpo.gatewayTransactionId)
          contaAtual.parcelas[0].saldo = 0; contaAtual.parcelas[0].valorPago = 100
          contaAtual.parcelas[0].pagamentos = [{ id: aReceber ? 'cccccccc-cccc-4ccc-8ccc-cccccccccccc' : pagamentoId, valor: 100, metodo: corpo.metodo, status: 'Confirmado', dataPagamento: corpo.dataPagamento, movimentoCaixaId: aReceber ? 'dddddddd-dddd-4ddd-8ddd-dddddddddddd' : '77777777-7777-4777-8777-777777777777' }]
          contaAtual.totalPago = 100; contaAtual.totalRecebido = 100; contaAtual.pendente = 0; contaAtual.status = 'Paga'
        }
        if (caminho.endsWith('/estornar')) { contaAtual.parcelas[0].pagamentos[0].status = 'Estornado'; contaAtual.parcelas[0].saldo = 100; contaAtual.pendente = 100; contaAtual.totalPago = 0; contaAtual.totalRecebido = 0; contaAtual.status = 'Aberta' }
        if (caminho.endsWith('/cancelar')) contaAtual.status = 'Cancelada'
        if (caminho.endsWith('/emitir')) contaAtual.status = 'Aberta'
        if (metodo === 'PATCH') contaAtual.descricao = corpo.descricao ?? contaAtual.descricao
      }
      return ok(contaAtual)
    }
    if (caminho === '/api/auth/me/modulos') return ok({ modulos: ['cardapio', 'producao', 'atendimento', 'cozinha', 'financeiro', 'campanhas', 'configuracoes', 'entregas'].map((id) => ({ id, liberado: true })), acoes })
    if (caminho === '/api/financeiro/categorias') return ok([{ id: categoriaId, nome: 'Operação', tipo: 'Ambas', ativa: true }])
    if (caminho === '/api/financeiro/centros-custo') return ok([])
    if (caminho === '/api/financeiro/dashboard') return ok({ totalAVencer30dPagar: 100, totalAVencer30dReceber: 100, totalVencidoPagar: 0, totalVencidoReceber: 0, totalPagoMes: 100, totalRecebidoMes: 200, qtdContasPagarAVencer30d: 1, qtdContasReceberAVencer30d: 1 })
    if (caminho === '/api/financeiro/fluxo-caixa') return ok([{ inicioBucket: instante, fimBucket: instante, rotulo: 'Outubro', previstoPagar: 100, previstoReceber: 200, realizadoPagar: 50, realizadoReceber: 200 }])
    if (/^\/api\/contas-a-(pagar|receber)$/.test(caminho)) return ok([contaAtual], { total: 1, page: 1, limit: 20 })
    if (/^\/api\/contas-a-(pagar|receber)\//.test(caminho)) return ok(contaAtual)
    if (caminho === '/api/caixa/movimentos') return ok([{ id: '88888888-8888-4888-8888-888888888888', tipo: 'saida', descricao: 'Despesa avulsa', valor: 30, dataMovimento: instante, metodo: 'dinheiro' }], { total: 1, page: 1, limit: 100 })
    if (caminho === '/api/caixa/dia') return ok({ data: url.searchParams.get('data') ?? hoje, aberto: true, fechado: false, saldoInicial: 100, saldoEsperado: 100, movimentos: [], linhasExtras: [] })
    if (caminho === '/api/atendimento/configuracao') return ok({})
    if (caminho === '/api/atendimento/agenda') return ok({ lojaDisponivel: false, dias: [] })
    if (caminho === '/api/atendimento/producao/insumos') return ok([{ produtoId: 'insumo', nome: 'Molho sugo', unidade: 'G', saldo: 1000, minimo: 500, custo: 0.02, receitas: 1 }])
    if (caminho === '/api/atendimento/producao/perdas') return ok({ de: hoje, ate: hoje, valor: 0, porMotivo: [], porProduto: [], lancamentos: [] })
    if (caminho === '/api/atendimento/comanda/cardapio/secoes') return ok([{ secaoId: 'secao', nome: 'Massas', ordem: 1, visivel: true, itens: 0 }])
    if (/nao-lidas|não-lidas|count/.test(caminho)) return ok({ total: 0 })
    return ok([])
  })
  const page = await context.newPage()
  paginaProva = page
  page.on('pageerror', (erro) => erros.push(erro.message))
  page.on('console', (mensagem) => {
    if (mensagem.type() === 'error' && !/Failed to load resource: the server responded with a status of 400/.test(mensagem.text())) erros.push(mensagem.text())
  })
  let telasVerificadas = 0
  for (const modulo of MODULOS) {
    const telas = telasDoMenu(modulo, { fonteApi: true })
    for (const telaId of [undefined, ...telas.map(tela => tela.id)]) {
      await page.goto(origem + '/' + hashDoModulo(modulo.id, telaId))
      // O Balcão é a referência aprovada e conserva seu próprio painel lateral.
      if (modulo.id === 'atendimento' && (telaId === undefined || telaId === 'balcao')) {
        await page.getByRole('navigation', { name: 'Navegação', exact: true }).waitFor()
        telasVerificadas++
        continue
      }
      const navegacao = page.getByRole('navigation', { name: `Telas de ${modulo.nome}`, exact: true })
      await navegacao.waitFor()
      await navegacao.locator(`a[href="${hashDoModulo(modulo.id, telaId ?? telas[0].id)}"][aria-current="page"]`).waitFor()
      assert.equal(await navegacao.getByRole('link').count(), telas.length, `${modulo.id}/${telaId ?? '(entrada)'}`)
      const ativa = navegacao.locator('[aria-current="page"]')
      assert.equal(await ativa.getAttribute('href'), hashDoModulo(modulo.id, telaId ?? telas[0].id))
      assert.equal(await page.getByRole('navigation', { name: 'Módulos da loja', exact: true }).getByRole('link').count(), MODULOS.length + 1)
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true)
      telasVerificadas++
    }
    await page.goto(origem + '/' + hashDoModulo(modulo.id))
    if (modulo.id === 'atendimento') await page.getByRole('navigation', { name: 'Navegação', exact: true }).waitFor()
    else await page.getByRole('navigation', { name: `Telas de ${modulo.nome}`, exact: true }).locator(`a[href="${hashDoModulo(modulo.id, telas[0].id)}"][aria-current="page"]`).waitFor()
    if (modulo.id === 'cardapio') await page.getByRole('button', { name: 'Novo prato', exact: true }).first().waitFor()
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))))
    await page.screenshot({ path: join(evidencias, `navegacao-${modulo.id}.png`) })
    if (modulo.id === 'cardapio') {
      await page.getByRole('button', { name: 'Novo prato', exact: true }).first().waitFor()
      const botoes = await page.getByRole('button', { name: 'Novo prato', exact: true }).evaluateAll(nos => nos.map(no => {
        const r = no.getBoundingClientRect()
        const faixa = document.createRange()
        faixa.selectNodeContents(no)
        const conteudo = faixa.getBoundingClientRect()
        return { esquerda: r.left, direita: r.right, conteudoDireita: conteudo.right, viewport: innerWidth }
      }))
      assert.ok(botoes.length > 0 && botoes.every(b => b.esquerda >= 0 && b.direita <= b.viewport && b.conteudoDireita <= b.direita), JSON.stringify(botoes))
    }
  }
  await page.goto(origem + '/#/m/financeiro/pagar')
  await page.getByRole('button', { name: 'Nova conta a pagar', exact: true }).waitFor()
  assert.equal(await page.getByRole('navigation', { name: 'Telas de Financeiro', exact: true }).getByRole('link').count(), 8)
  await page.getByRole('button', { name: 'Nova conta a pagar', exact: true }).click()
  let modal = page.getByRole('dialog', { name: 'Nova conta a pagar', exact: true })
  await modal.getByLabel('Descrição', { exact: true }).fill('Conta criada pela prova')
  await modal.getByLabel('Categoria financeira', { exact: true }).selectOption(categoriaId)
  await modal.getByLabel('Valor total', { exact: true }).fill('10000')
  await page.screenshot({ path: join(evidencias, 'financeiro-modal-nova-conta.png') })
  await modal.getByRole('button', { name: 'Salvar', exact: true }).click()
  await modal.waitFor({ state: 'hidden' })
  assert.equal(escritas.find((item) => item.caminho === '/api/contas-a-pagar').corpo.parcelas[0].valor, 100)
  await page.getByRole('button', { name: 'Abrir conta Compra de embalagens' }).click()
  modal = page.getByRole('dialog', { name: 'Compra de embalagens', exact: true })
  await modal.getByRole('button', { name: 'Registrar pagamento', exact: true }).click()
  const pagamento = page.getByRole('dialog', { name: 'Registrar pagamento', exact: true })
  await pagamento.getByRole('button', { name: 'Confirmar registro', exact: true }).click()
  await pagamento.waitFor({ state: 'hidden' })
  assert.equal(await modal.getByRole('button', { name: 'Registrar pagamento', exact: true }).count(), 0)
  await modal.getByRole('button', { name: 'Estornar', exact: true }).click()
  const estorno = page.getByRole('dialog', { name: 'Estornar registro', exact: true })
  await estorno.getByLabel('Motivo', { exact: true }).fill('Registro de teste incorreto')
  await estorno.getByRole('button', { name: 'Confirmar', exact: true }).click()
  await estorno.waitFor({ state: 'hidden' })
  await modal.getByRole('button', { name: 'Registrar pagamento', exact: true }).waitFor()
  await page.keyboard.press('Escape')
  await modal.waitFor({ state: 'hidden' })
  assert.equal(await page.getByRole('button', { name: 'Abrir conta Compra de embalagens' }).evaluate((no) => no === document.activeElement), true)
  await page.screenshot({ path: join(evidencias, 'financeiro-contas-desktop.png') })
  await page.goto(origem + '/#/m/financeiro/receber')
  await page.getByRole('button', { name: 'Abrir conta Venda de encomenda' }).click()
  modal = page.getByRole('dialog', { name: 'Venda de encomenda', exact: true })
  await modal.getByRole('button', { name: 'Registrar recebimento', exact: true }).click()
  const recebimento = page.getByRole('dialog', { name: 'Registrar recebimento', exact: true })
  await recebimento.getByRole('button', { name: 'Confirmar registro', exact: true }).click()
  await recebimento.waitFor({ state: 'hidden' })
  assert.ok(escritas.some((item) => item.caminho === `/api/contas-a-receber/${contaReceber.id}/parcelas/${contaReceber.parcelas[0].id}/pagamentos`))
  assert.equal(await modal.getByRole('button', { name: 'Registrar recebimento', exact: true }).count(), 0)
  await modal.getByRole('button', { name: 'Estornar', exact: true }).click()
  const estornoRecebimento = page.getByRole('dialog', { name: 'Estornar registro', exact: true })
  await estornoRecebimento.getByLabel('Motivo', { exact: true }).fill('Recebimento de teste incorreto')
  await estornoRecebimento.getByRole('button', { name: 'Confirmar', exact: true }).click()
  await estornoRecebimento.waitFor({ state: 'hidden' })
  await modal.getByRole('button', { name: 'Registrar recebimento', exact: true }).waitFor()
  await page.keyboard.press('Escape')
  await modal.waitFor({ state: 'hidden' })
  await page.getByRole('navigation', { name: 'Telas de Financeiro', exact: true }).getByRole('link', { name: 'Extrato', exact: true }).click()
  await page.getByRole('button', { name: 'Detalhes de Despesa avulsa' }).waitFor()
  await page.getByRole('button', { name: 'Detalhes de Despesa avulsa' }).click()
  await page.getByRole('dialog', { name: 'Movimento financeiro', exact: true }).waitFor()
  await page.keyboard.press('Escape')
  await page.getByRole('navigation', { name: 'Telas de Financeiro', exact: true }).getByRole('link', { name: 'Relatórios', exact: true }).click()
  await page.getByRole('button', { name: 'Ampliar relatório', exact: true }).click()
  await page.getByRole('dialog', { name: 'Relatório de fluxo de caixa', exact: true }).waitFor()
  await page.keyboard.press('Escape')
  const downloadPromise = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Exportar CSV', exact: true }).click()
  assert.match((await downloadPromise).suggestedFilename(), /^financeiro-.*\.csv$/)
  await page.screenshot({ path: join(evidencias, 'financeiro-relatorios-desktop.png') })
  await page.setViewportSize({ width: 390, height: 844 })
  await page.getByRole('button', { name: 'Menu de Financeiro', exact: true }).click()
  const menu = page.getByRole('dialog', { name: 'Menu de Financeiro', exact: true })
  await menu.getByRole('link', { name: 'Contas a receber', exact: true }).click()
  await menu.waitFor({ state: 'hidden' })
  await page.getByRole('heading', { name: 'Contas a receber', exact: true }).waitFor()
  await page.getByRole('button', { name: 'Nova conta a receber', exact: true }).click()
  await page.getByRole('dialog', { name: 'Nova conta a receber', exact: true }).waitFor()
  await page.screenshot({ path: join(evidencias, 'financeiro-modal-celular.png') })
  await page.keyboard.press('Escape')
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true)
  // Duas modais abertas: Escape fecha a filha e o foco volta ao botão da conta.
  await page.setViewportSize({ width: 1440, height: 960 })
  await page.goto(origem + '/#/m/financeiro/pagar')
  await page.getByRole('button', { name: 'Abrir conta Compra de embalagens' }).click()
  modal = page.getByRole('dialog', { name: 'Compra de embalagens', exact: true })
  await modal.getByRole('button', { name: 'Editar conta', exact: true }).click()
  await page.getByRole('dialog', { name: 'Editar conta', exact: true }).waitFor()
  await page.keyboard.press('Escape')
  assert.equal(await modal.isVisible(), true)
  assert.equal(await modal.getByRole('button', { name: 'Editar conta', exact: true }).evaluate((no) => no === document.activeElement), true)
  await page.keyboard.press('Escape')
  falhaDeGravacao = true
  await page.getByRole('button', { name: 'Nova conta a pagar', exact: true }).click()
  modal = page.getByRole('dialog', { name: 'Nova conta a pagar', exact: true })
  await modal.getByLabel('Descrição', { exact: true }).fill('Conta recusada')
  await modal.getByLabel('Categoria financeira', { exact: true }).selectOption(categoriaId)
  await modal.getByLabel('Valor total', { exact: true }).fill('10000')
  await modal.getByRole('button', { name: 'Salvar', exact: true }).click()
  await modal.getByRole('alert').filter({ hasText: 'Categoria de teste indisponível' }).waitFor()
  assert.equal(await modal.getByLabel('Descrição', { exact: true }).inputValue(), 'Conta recusada')
  await page.keyboard.press('Escape')
  falhaDeGravacao = false
  for (const [rota, botao, titulo] of [
    ['producao/insumos', 'Cadastrar insumo', 'Cadastrar insumo'],
    ['producao/insumos', 'Ajustar Molho sugo', 'Ajustar Molho sugo'],
    ['producao/perdas', 'Lançar perda', 'Lançar perda'],
    ['cardapio/categorias', 'Nova categoria', 'Nova categoria do cardápio'],
    ['cardapio/categorias', 'Renomear Massas', 'Renomear Massas'],
    ['financeiro/categorias', 'Nova categoria', 'Novo cadastro de categoria financeira'],
    ['financeiro/centros-custo', 'Novo centro de custo', 'Novo cadastro de centro de custo'],
  ]) {
    await page.goto(origem + '/#/m/' + rota)
    const abrir = page.getByRole('button', { name: botao, exact: true })
    await abrir.click()
    const janela = page.getByRole('dialog', { name: titulo, exact: true })
    await janela.waitFor()
    await page.keyboard.press('Escape')
    await janela.waitFor({ state: 'hidden' })
    assert.equal(await abrir.evaluate((no) => no === document.activeElement), true, `foco de ${titulo}`)
  }
  await page.goto(origem + '/#/m/financeiro/visao-geral')
  await page.getByText('Pago no mês', { exact: true }).waitFor()
  await page.getByRole('radio', { name: 'Escuro', exact: true }).click()
  await page.screenshot({ path: join(evidencias, 'financeiro-visao-geral-escuro.png') })
  await page.goto(origem + '/#/m/atendimento/balcao')
  await page.getByRole('button', { name: 'Gestão', exact: true }).click()
  const gestao = page.getByRole('dialog', { name: 'Gestão da loja', exact: true })
  await gestao.getByRole('tab', { name: 'Financeiro', exact: true }).click()
  await gestao.getByRole('tab', { name: 'Contas a pagar', exact: true }).click()
  await gestao.getByRole('button', { name: 'Nova conta a pagar', exact: true }).click()
  await page.getByRole('dialog', { name: 'Nova conta a pagar', exact: true }).waitFor()
  await page.keyboard.press('Escape')
  assert.equal(await gestao.isVisible(), true)
  await page.screenshot({ path: join(evidencias, 'financeiro-na-gestao-atendimento.png') })
  await page.keyboard.press('Escape')
  acoes.gerenciarContasPagar = false
  await page.goto(origem + '/#/m/financeiro/pagar')
  await page.reload()
  await page.getByRole('button', { name: 'Abrir conta Compra de embalagens' }).waitFor()
  assert.equal(await page.getByRole('button', { name: 'Nova conta a pagar', exact: true }).count(), 0)
  acoes.visualizarContasPagar = false
  await page.reload()
  await page.getByText('Seu perfil não pode consultar estas contas', { exact: true }).waitFor()
  acoes.visualizarContasPagar = true
  acoes.gerenciarContasPagar = true
  await page.reload()
  await page.getByRole('button', { name: 'Nova conta a pagar', exact: true }).waitFor()
  assert.equal(await page.getByRole('button', { name: 'Atualizar sistema', exact: true }).count(), 0)
  await page.getByRole('button', { name: 'Nova conta a pagar', exact: true }).click()
  modal = page.getByRole('dialog', { name: 'Nova conta a pagar', exact: true })
  await modal.getByLabel('Descrição', { exact: true }).fill('Rascunho preservado durante publicação')
  const consultarVersao = async () => {
    await page.waitForTimeout(5100)
    const consulta = page.waitForResponse(resposta => new URL(resposta.url()).pathname === '/versao.json')
    await page.evaluate(() => window.dispatchEvent(new Event('focus')))
    await consulta
  }
  respostaInvalida = true
  await consultarVersao()
  assert.equal(await page.getByRole('button', { name: 'Atualizar sistema', exact: true }).count(), 0)
  respostaInvalida = false
  versaoPublicada = shaDoDocumento === 'b'.repeat(40) ? 'c'.repeat(40) : 'b'.repeat(40)
  await consultarVersao()
  await page.getByRole('button', { name: 'Atualizar sistema', exact: true }).waitFor({ state: 'attached' })
  assert.equal(await modal.getByLabel('Descrição', { exact: true }).inputValue(), 'Rascunho preservado durante publicação')
  assert.equal(new URL(page.url()).searchParams.has('versao'), false)
  await page.keyboard.press('Escape')
  await page.setViewportSize({ width: 390, height: 844 })
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true)
  await page.screenshot({ path: join(evidencias, 'aviso-atualizacao-celular.png') })
  shaDoDocumento = versaoPublicada
  await page.getByRole('button', { name: 'Atualizar sistema', exact: true }).click()
  await page.waitForURL(url => url.searchParams.get('versao') === versaoPublicada)
  await page.getByRole('button', { name: 'Nova conta a pagar', exact: true }).waitFor()
  assert.equal(new URL(page.url()).hash, '#/m/financeiro/pagar')
  assert.equal(await page.getByRole('button', { name: 'Atualizar sistema', exact: true }).count(), 0)
  assert.equal(await page.evaluate(() => JSON.parse(sessionStorage.getItem('easystok.sessao')).token), 'teste-ui-controlado')
  assert.deepEqual(erros, [])
  console.log(`OK: ${telasVerificadas} rotas dos oito módulos, navegação lateral, operações financeiras, modais, permissões, atualização por clique sem perder rascunho, manifesto inválido, sessão/rota preservadas e celular; ${escritas.length} escritas controladas; sem erros de página ou console.`)
} catch (erro) {
  console.error('Erros de página:', erros.filter(erro => !erro.startsWith('Failed to send error')).map(erro => erro.split('\n')[0]))
  if (paginaProva) {
    console.error((await paginaProva.locator('body').innerText()).slice(0, 1500))
    await paginaProva.screenshot({ path: join(evidencias, 'falha.png') })
  }
  throw erro
} finally { await browser.close() }
