// App real + HTTP controlado. Não chama API/LLM ou portas de envio reais.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2395/busca-na-casa')
mkdirSync(pasta, { recursive: true })
const browser = await chromium.launch({ headless: true })
const chamadas = [], erros = []
let achouConversa = false
let segurarPessoa = false, soltarPessoa, avisarPessoa
const resumo = { id: 'conversa-encontrada', contatoNome: 'João Açúcar', contatoIdExterno: '5511999991234', clienteId: 'pessoa', vinculoRevisao: 2, canal: 'WhatsApp', situacao: 'Assumida', assumidaPorUsuarioId: 'dona', ultimaMensagemEm: new Date().toISOString(), naoLidas: 0 }
const pessoa = { id: 'pessoa', nome: 'João Açúcar', telefone: '11999991234', tipoPessoa: 'fisica', revisao: 45, ativo: true }
const produto = { id: 'produto', nome: 'Pão de açúcar', tipo: 'Alimento', status: 'Ativo', categoriaId: 'categoria' }
const pedido = { id: 'pedido', numeroDoDia: 42, clienteNome: 'João Açúcar', status: 'Aguardando', total: 37, totalPago: 0 }
async function preparar(restrito = false) {
  const contexto = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true, locale: 'pt-BR', reducedMotion: 'reduce' })
  await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa', nome: 'Loja da busca' }, usuario: { id: 'dona', nome: 'Dona de teste' } })))
  await contexto.route('**/api/**', async route => {
    const req = route.request(), url = new URL(req.url()), path = url.pathname
    if (!path.startsWith('/api/')) return route.continue()
    chamadas.push({ path, query: url.search, metodo: req.method(), restrito })
    assert.ok(req.method() === 'GET' || path.endsWith('/marcar-lida'), `Escrita inesperada: ${path}`)
    const ok = data => route.fulfill({ json: { data } })
    if (path === '/api/auth/me/modulos') return ok({ modulos: (restrito ? ['cozinha'] : ['atendimento', 'cadastros', 'cardapio']).map(id => ({ id, liberado: true })), acoes: restrito ? {} : { verCadastrosProdutos: true, editarCadastrosProdutos: true, verPedidos: true } })
    if (path === '/api/clientes/buscar') {
      if (['lenta', 'Leads'].includes(url.searchParams.get('termo'))) await new Promise(r => setTimeout(r, 700))
      return ok(url.searchParams.get('termo') === 'nada' ? [] : [pessoa])
    }
    if (path === '/api/clientes/pessoa') {
      if (segurarPessoa) await new Promise(resolve => { soltarPessoa = resolve; avisarPessoa() })
      return ok({ cliente: pessoa, alteracoes: [] }).catch(() => {})
    }
    if (path === '/api/produtos/search') return ok(url.searchParams.get('termo') === 'nada' ? [] : [produto])
    if (path === '/api/produtos/produto') return ok(produto)
    if (path === '/api/categorias') return ok([{ id: 'categoria', nome: 'Pratos' }])
    if (path === '/api/pedidos') {
      if (url.searchParams.get('search') === 'falha') return route.fulfill({ status: 503, json: { error: { message: 'Pedidos indisponíveis no teste.' } } })
      return ok(url.searchParams.get('search') === 'nada' ? [] : [pedido])
    }
    if (path === '/api/pedidos/pedido') return ok({ pedido, itens: [{ id: 'item', nome: 'Lasanha', quantidade: 1, subtotal: 37 }], eventos: [], pagamentos: [] })
    if (path === '/api/atendimento/conversas') {
      if (url.searchParams.has('q')) { achouConversa = true; return ok(url.searchParams.get('q') === 'nada' ? [] : [resumo]) }
      return ok(achouConversa ? [resumo] : [])
    }
    if (path.endsWith('/mensagens')) return ok([{ id: 'mensagem', direcao: 'Entrada', texto: 'Mensagem da conversa certa.', enviadaEm: new Date().toISOString(), tipoConteudo: 'Texto' }])
    if (path.endsWith('/pedido')) return ok(null)
    if (path.endsWith('/dossie')) return ok({ cliente: pessoa, enderecos: [], totalPedidos: 0, notas: [], tags: [] })
    if (path === '/api/atendimento/configuracao') return ok({ slaRespostaMinutos: 5 })
    if (path === '/api/atendimento/agenda') return ok({ lojaDisponivel: true, dias: [] })
    if (path === '/api/atendimento/comanda/cardapio') return ok({ itens: [], porcoes: {} })
    return ok([])
  })
  const page = await contexto.newPage(); page.setDefaultTimeout(12000); page.on('pageerror', e => erros.push(e.message))
  await page.goto(`${process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'}/#/`)
  await page.getByRole('button', { name: 'Buscar na Casa', exact: true }).first().waitFor()
  return { contexto, page }
}
const { contexto, page } = await preparar()
try {
  await page.keyboard.press('Control+k')
  let dialogo = page.getByRole('dialog', { name: 'Buscar na Casa', exact: true })
  const buscar = async termo => { await dialogo.getByRole('combobox', { name: 'Buscar na Casa', exact: true }).fill(termo); await page.waitForResponse(r => new URL(r.url()).pathname === '/api/clientes/buscar' && new URL(r.url()).searchParams.get('termo') === termo) }
  await buscar('joao')
  await page.getByRole('option').filter({ hasText: 'Pessoas' }).waitFor()
  assert.equal(await page.getByRole('option').count(), 4)
  segurarPessoa = true
  const chegouPessoa = new Promise(resolve => { avisarPessoa = resolve })
  await page.getByRole('option').filter({ hasText: 'Pessoas' }).tap(); await chegouPessoa
  await buscar('nada'); segurarPessoa = false; soltarPessoa(); await page.waitForTimeout(150)
  const abriuFichaAntiga = await page.getByRole('dialog', { name: 'Editar cliente', exact: true }).count()
  if (abriuFichaAntiga) await page.getByRole('dialog', { name: 'Editar cliente', exact: true }).getByRole('button', { name: 'Cancelar', exact: true }).tap()
  const saiuLeads = page.waitForRequest(r => new URL(r.url()).pathname === '/api/clientes/buscar' && new URL(r.url()).searchParams.get('termo') === 'Leads')
  await dialogo.getByRole('combobox').fill('Leads'); await saiuLeads
  await dialogo.getByRole('combobox').press('Enter'); await page.waitForTimeout(150)
  const abriuTelaOculta = !(await dialogo.count())
  writeFileSync(resolve(pasta, 'contexto-intermediario.json'), JSON.stringify({ abriuFichaAntiga, abriuTelaOculta }, null, 2))
  assert.equal(abriuFichaAntiga, 0, 'GET de ficha A retido não pode abrir após trocar a busca')
  assert.equal(abriuTelaOculta, false, 'Enter não escolhe uma tela que está oculta pelo carregamento')
  await buscar('joao'); await page.getByRole('option').filter({ hasText: 'Pessoas' }).waitFor()
  await page.screenshot({ path: resolve(pasta, 'resultados-390.png'), fullPage: true })
  await dialogo.getByRole('combobox').press('Enter')
  let editor = page.getByRole('dialog', { name: 'Editar cliente', exact: true }); await editor.waitFor()
  assert.equal(await editor.getByRole('textbox', { name: 'Nome', exact: true }).inputValue(), pessoa.nome)
  await editor.getByRole('button', { name: 'Cancelar', exact: true }).tap()
  await dialogo.getByRole('combobox').focus()
  await page.getByRole('option').filter({ hasText: 'Produtos' }).tap()
  const produtoMovel = page.getByRole('dialog', { name: 'Editar produto', exact: true }); await produtoMovel.waitFor()
  assert.equal(await produtoMovel.getByRole('textbox', { name: 'Nome', exact: true }).inputValue(), produto.nome)
  await produtoMovel.getByRole('button', { name: 'Cancelar', exact: true }).tap()
  await dialogo.getByRole('combobox').focus()
  await page.getByRole('option').filter({ hasText: 'Pedido Nº 042' }).tap()
  // #2478: o detalhe é o mesmo da tela Pedidos de Cadastros (itens, totais e saldo), sem o atalho de conversa fora do Atendimento.
  const detalhe = page.getByRole('dialog', { name: 'Pedido Nº 042', exact: true }); await detalhe.waitFor()
  assert.match(await detalhe.innerText(), /Lasanha/); assert.match(await detalhe.innerText(), /Falta receber/)
  assert.equal(await detalhe.getByRole('button', { name: 'Abrir conversa' }).count(), 0)
  await detalhe.getByRole('button', { name: /Fechar/ }).first().tap()
  await buscar('falha')
  await dialogo.getByRole('alert').filter({ hasText: 'Pedidos' }).waitFor()
  assert.equal(await page.getByRole('option').count(), 3)
  const saiuLenta = page.waitForRequest(r => new URL(r.url()).pathname === '/api/clientes/buscar' && new URL(r.url()).searchParams.get('termo') === 'lenta')
  await dialogo.getByRole('combobox').fill('lenta'); await saiuLenta; await buscar('nada')
  await page.waitForTimeout(800)
  await dialogo.getByRole('status').filter({ hasText: 'Nenhum resultado' }).waitFor()
  assert.equal(await page.getByRole('option').count(), 0)
  await buscar('+55 (11) 99999-1234')
  await page.getByRole('option').filter({ hasText: 'Conversas' }).tap()
  await dialogo.waitFor({ state: 'detached' })
  await page.getByText('Mensagem da conversa certa.', { exact: true }).waitFor()
  assert.equal(await page.getByRole('tab', { name: 'Mensagens', exact: true }).getAttribute('aria-selected'), 'true')
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth), false)
  await page.screenshot({ path: resolve(pasta, 'conversa-certa-390.png'), fullPage: true })
  const composer = page.getByPlaceholder(/^Escreva para/)
  await composer.fill('Rascunho preservado ao consultar outra pessoa')
  await page.keyboard.press('Control+k'); dialogo = page.getByRole('dialog', { name: 'Buscar na Casa', exact: true }); await buscar('joao')
  await page.getByRole('option').filter({ hasText: 'Pessoas' }).tap()
  await page.getByRole('dialog', { name: 'Editar cliente', exact: true }).getByRole('button', { name: 'Cancelar', exact: true }).tap()
  await dialogo.getByRole('button', { name: /Fechar/ }).first().tap()
  assert.equal(await composer.inputValue(), 'Rascunho preservado ao consultar outra pessoa')
  await page.setViewportSize({ width: 1440, height: 900 }); await page.keyboard.press('Control+k')
  dialogo = page.getByRole('dialog', { name: 'Buscar na Casa', exact: true }); await buscar('joao')
  await page.getByRole('option').filter({ hasText: 'Produtos' }).tap()
  editor = page.getByRole('dialog', { name: 'Editar produto', exact: true }); await editor.waitFor()
  assert.equal(await editor.getByRole('textbox', { name: 'Nome', exact: true }).inputValue(), produto.nome)
  await page.screenshot({ path: resolve(pasta, 'produto-certo-1440.png'), fullPage: true })
  await contexto.close()
  const limitado = await preparar(true)
  await limitado.page.keyboard.press('Meta+k')
  await limitado.page.getByRole('combobox', { name: 'Buscar na Casa', exact: true }).fill('fila')
  await limitado.page.getByRole('option').filter({ hasText: 'Fila de preparo' }).waitFor()
  assert.ok(chamadas.filter(c => c.restrito).every(c => !/^\/api\/(clientes|produtos|pedidos|atendimento\/conversas)/.test(c.path)), 'Perfil sem acesso não consulta categorias proibidas')
  await limitado.contexto.close()
  assert.deepEqual(erros, [])
  writeFileSync(resolve(pasta, 'resultado.json'), JSON.stringify({ appReal: true, httpControlado: true, quatroCategorias: true, telasPermitidas: true, tecladoCtrlCmdK: true, identidadesCorretas: true, falhaParcial: true, touch390: true, desktop1440: true, erros }, null, 2))
  console.log('Busca na Casa: App real,4categorias,telas permitidas,Ctrl/CmdK,identidade,erro parcial e390/1440 passaram.')
} catch (e) { if (!page.isClosed()) await page.screenshot({ path: resolve(pasta, 'falha.png'), fullPage: true }); throw e }
finally { await browser.close() }
