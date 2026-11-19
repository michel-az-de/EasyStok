// #2871: "Novo pedido" do cliente que pediu por outro canal, pelas quatro entradas (lista de Clientes, Ficha, Pedidos e
// busca do Balcão). Cada uma abre a conversa do WhatsApp da loja pela API, leva ao Balcão com ela selecionada e abre o
// cardápio da comanda. Sem acesso ao Atendimento o botão não aparece. App real no Chromium, HTTP controlado.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2871/novo-pedido')
mkdirSync(pasta, { recursive: true })
const URL_CONSOLE = process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'

const pessoas = ['Ana Paula', 'Bruno Lima', 'Carla Souza'].map((nome, i) => ({ id: `cli-${i}`, revisao: 1, nome, telefone: `+551199999000${i}`, email: '', ativo: true, orderCount: i }))
const conversaDe = (p) => ({
  id: `conv-${p.id}`, contatoNome: p.nome, contatoIdExterno: p.telefone.slice(1), clienteId: p.id, vinculoRevisao: 1, canal: 'WhatsApp',
  situacao: 'Automatica', ultimaMensagemEm: new Date().toISOString(), naoLidas: 0, ultimaMensagemTexto: null,
})

const browser = await chromium.launch({ headless: true })
const erros = []
async function preparar({ hash = '#/', modulos = ['atendimento', 'cadastros'], largura = 1440, altura = 900 } = {}) {
  const abertas = []
  const contexto = await browser.newContext({ viewport: { width: largura, height: altura }, locale: 'pt-BR', timezoneId: 'America/Sao_Paulo', reducedMotion: 'reduce' })
  await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa', nome: 'Loja do novo pedido' }, usuario: { id: 'tatiana', nome: 'Atendente de teste' } })))
  await contexto.route('**/api/**', async (route) => {
    const req = route.request(), url = new URL(req.url()), path = url.pathname
    if (!path.startsWith('/api/')) return route.continue()
    const ok = (data, extra = {}) => route.fulfill({ json: { data, ...extra } })
    if (req.method() === 'POST' && path === '/api/atendimento/conversas/do-cliente') {
      const { clienteId } = JSON.parse(req.postData() ?? '{}')
      const pessoa = pessoas.find((p) => p.id === clienteId)
      assert.ok(pessoa, `cliente desconhecido: ${clienteId}`)
      abertas.push(clienteId)
      return ok(conversaDe(pessoa))
    }
    assert.ok(req.method() === 'GET' || path.endsWith('/marcar-lida'), `Escrita inesperada: ${req.method()} ${path}`)
    if (path === '/api/auth/me/modulos') return ok({ modulos: modulos.map((id) => ({ id, liberado: true })), acoes: { verPedidos: true } })
    if (path === '/api/clientes') return ok(pessoas, { meta: { total: pessoas.length, totalCount: pessoas.length } })
    if (path === '/api/clientes/buscar') {
      const termo = (url.searchParams.get('termo') ?? '').toLowerCase()
      return ok(pessoas.filter((p) => p.nome.toLowerCase().includes(termo)))
    }
    if (path === '/api/pedidos') return ok([], { meta: { total: 0, totalCount: 0 } })
    const ficha = path.match(/^\/api\/clientes\/(cli-\d)$/)
    if (ficha) return ok({ cliente: pessoas.find((p) => p.id === ficha[1]), alteracoes: [] })
    if (path === '/api/atendimento/conversas') return ok(abertas.map((id) => conversaDe(pessoas.find((p) => p.id === id))))
    if (path.endsWith('/mensagens')) return ok([])
    if (path.endsWith('/pedido')) return ok(null)
    const dossie = path.match(/conversas\/conv-(cli-\d)\/dossie$/)
    if (dossie) return ok({ cliente: pessoas.find((p) => p.id === dossie[1]), enderecos: [], totalPedidos: 0, notas: [], tags: [] })
    if (path === '/api/atendimento/configuracao') return ok({ slaRespostaMinutos: 5 })
    if (path === '/api/atendimento/agenda') return ok({ lojaDisponivel: true, dias: [] })
    if (path === '/api/atendimento/comanda/cardapio') return ok({ itens: [], porcoes: {} })
    if (path === '/api/notificacoes/badge') return ok({ count: 0 })
    return ok([])
  })
  const page = await contexto.newPage()
  page.setDefaultTimeout(12000)
  page.on('pageerror', (e) => erros.push(e.message))
  page.on('console', (m) => { if (m.type() === 'error' && !m.text().startsWith('Failed to load resource')) erros.push('console: ' + m.text()) })
  await page.goto(`${URL_CONSOLE}/${hash}`)
  return { contexto, page, abertas }
}
async function cardapioAbertoPara(page, nome) {
  await page.waitForFunction(() => window.location.hash === '#/m/atendimento/balcao')
  await page.getByRole('complementary', { name: 'Cardápio de hoje' }).waitFor()
  // O cabeçalho do cardápio diz de quem é a comanda pelo primeiro nome: a conversa certa ficou selecionada.
  const comanda = page.getByRole('complementary', { name: 'Cardápio de hoje' }).locator('p', { hasText: 'Comanda de' })
  assert.equal((await comanda.innerText()).trim(), `Comanda de ${nome.split(' ')[0]}`)
}

try {
  // 1. Lista de Clientes: "Novo pedido" na linha.
  {
    const { contexto, page, abertas } = await preparar({ hash: '#/m/cadastros/clientes' })
    await page.getByRole('row').filter({ hasText: 'Ana Paula' }).waitFor()
    await page.screenshot({ path: `${pasta}/clientes-1440.png` })
    await page.getByRole('button', { name: 'Novo pedido de Ana Paula', exact: true }).click()
    await cardapioAbertoPara(page, 'Ana Paula')
    assert.deepEqual(abertas, ['cli-0'])
    await page.screenshot({ path: `${pasta}/balcao-cardapio-1440.png` })
    await contexto.close()
  }

  // 2. Ficha do cliente: "Novo pedido" no rodapé.
  {
    const { contexto, page, abertas } = await preparar({ hash: '#/m/cadastros/clientes' })
    await page.getByRole('button', { name: 'Abrir ficha de Carla Souza', exact: true }).click()
    const ficha = page.getByRole('dialog', { name: 'Ficha do cliente' })
    await ficha.getByRole('button', { name: 'Novo pedido', exact: true }).waitFor()
    await page.screenshot({ path: `${pasta}/ficha-1440.png` })
    await ficha.getByRole('button', { name: 'Novo pedido', exact: true }).click()
    await cardapioAbertoPara(page, 'Carla Souza')
    assert.equal(await page.getByRole('dialog', { name: 'Ficha do cliente' }).count(), 0, 'a ficha fecha')
    assert.deepEqual(abertas, ['cli-2'])
    await contexto.close()
  }

  // 3. Pedidos: "Novo pedido" no cabeçalho escolhe o cliente pela busca.
  {
    const { contexto, page, abertas } = await preparar({ hash: '#/m/cadastros/pedidos' })
    await page.getByRole('button', { name: 'Novo pedido', exact: true }).click()
    const escolha = page.getByRole('dialog', { name: 'Novo pedido' })
    await escolha.getByText('Digite ao menos 3 letras').waitFor()
    await escolha.getByRole('searchbox', { name: 'Cliente' }).fill('zzz')
    await escolha.getByText('Nenhum cliente ativo com este nome ou telefone.').waitFor()
    await escolha.getByRole('searchbox', { name: 'Cliente' }).fill('bru')
    await escolha.getByRole('button', { name: 'Novo pedido de Bruno Lima', exact: true }).waitFor()
    await page.screenshot({ path: `${pasta}/pedidos-escolher-1440.png` })
    await escolha.getByRole('button', { name: 'Novo pedido de Bruno Lima', exact: true }).click()
    await cardapioAbertoPara(page, 'Bruno Lima')
    assert.equal(await page.getByRole('dialog', { name: 'Novo pedido' }).count(), 0, 'a escolha fecha')
    assert.deepEqual(abertas, ['cli-1'])
    await contexto.close()
  }

  // 4. Busca do Balcão: cliente sem conversa aberta ganha "Novo pedido" ao lado de "Chamar no WhatsApp".
  {
    const { contexto, page, abertas } = await preparar({ hash: '#/m/atendimento/balcao' })
    await page.getByPlaceholder('Buscar conversa').fill('ana')
    await page.getByRole('button', { name: 'Novo pedido de Ana Paula', exact: true }).waitFor()
    await page.getByRole('button', { name: 'Chamar no WhatsApp', exact: true }).waitFor()
    await page.screenshot({ path: `${pasta}/balcao-busca-1440.png` })
    await page.getByRole('button', { name: 'Novo pedido de Ana Paula', exact: true }).click()
    await cardapioAbertoPara(page, 'Ana Paula')
    assert.deepEqual(abertas, ['cli-0'])
    await contexto.close()
  }

  // 5. Celular: a escolha de Pedidos cabe na tela.
  {
    const { contexto, page } = await preparar({ hash: '#/m/cadastros/pedidos', largura: 390, altura: 844 })
    await page.getByRole('button', { name: 'Novo pedido', exact: true }).click()
    const escolha = page.getByRole('dialog', { name: 'Novo pedido' })
    await escolha.getByRole('searchbox', { name: 'Cliente' }).fill('ana')
    await escolha.getByRole('button', { name: 'Novo pedido de Ana Paula', exact: true }).waitFor()
    const larguraDaPagina = await page.evaluate(() => document.documentElement.scrollWidth)
    assert.ok(larguraDaPagina <= 390, `sem rolagem lateral: ${larguraDaPagina}`)
    await page.screenshot({ path: `${pasta}/pedidos-escolher-390.png` })
    await contexto.close()
  }

  // 6. Sem o Atendimento o botão não aparece (a conversa não abriria).
  {
    const { contexto, page } = await preparar({ hash: '#/m/cadastros/clientes', modulos: ['cadastros'] })
    await page.getByRole('row').filter({ hasText: 'Ana Paula' }).waitFor()
    assert.equal(await page.getByRole('button', { name: /^Novo pedido/ }).count(), 0)
    await page.evaluate(() => { window.location.hash = '#/m/cadastros/pedidos' })
    await page.getByRole('heading', { name: 'Pedidos', exact: true, level: 2 }).waitFor()
    assert.equal(await page.getByRole('button', { name: /^Novo pedido/ }).count(), 0)
    await contexto.close()
  }

  assert.deepEqual(erros, [])
  console.log('ok prova-2871: Novo pedido pela lista, Ficha, Pedidos e Balcão abre a comanda na conversa da loja')
} finally {
  await browser.close()
}
