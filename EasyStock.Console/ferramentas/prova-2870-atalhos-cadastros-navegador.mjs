// #2870: Clientes e Pedidos a um clique (cartões do Início, trilho dos módulos e trilho do Atendimento) e a busca de
// Clientes que começa limpa ao voltar para Cadastros. App real no Chromium, com HTTP controlado.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2870/atalhos')
mkdirSync(pasta, { recursive: true })
const URL_CONSOLE = process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'

const hoje = new Date()
const pessoas = ['Ana Paula', 'Bruno Lima', 'Carla Souza'].map((nome, i) => ({ id: `cli-${i}`, revisao: 1, nome, telefone: '', email: '', ativo: true, orderCount: i }))
const pedidos = pessoas.map((p, i) => ({
  id: `pedido-${i + 1}`, numeroDoDia: i + 1, clienteId: p.id, clienteNome: p.nome, clienteTelefone: '11999990000',
  status: 'aguardando', total: 40 + i, totalPago: 0, origem: 'whatsapp', criadoEm: new Date(hoje.getTime() - i * 60000).toISOString(),
}))

const browser = await chromium.launch({ headless: true })
const erros = []
async function preparar({ largura = 1440, altura = 900, verPedidos = true, hash = '#/' } = {}) {
  const consultas = []
  const contexto = await browser.newContext({ viewport: { width: largura, height: altura }, locale: 'pt-BR', timezoneId: 'America/Sao_Paulo', reducedMotion: 'reduce' })
  await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa', nome: 'Loja dos atalhos' }, usuario: { id: 'tatiana', nome: 'Atendente de teste' } })))
  await contexto.route('**/api/**', async (route) => {
    const req = route.request(), url = new URL(req.url()), path = url.pathname
    if (!path.startsWith('/api/')) return route.continue()
    assert.ok(req.method() === 'GET' || path.endsWith('/marcar-lida'), `Escrita inesperada: ${req.method()} ${path}`)
    const ok = (data, extra = {}) => route.fulfill({ json: { data, ...extra } })
    if (path === '/api/auth/me/modulos') return ok({ modulos: ['atendimento', 'cadastros'].map((id) => ({ id, liberado: true })), acoes: verPedidos ? { verPedidos: true } : {} })
    if (path === '/api/clientes' || path === '/api/pedidos') {
      const q = Object.fromEntries(url.searchParams)
      consultas.push({ path, ...q })
      const itens = path === '/api/clientes' ? pessoas : pedidos
      const filtrados = itens.filter((i) => !q.search || (i.nome ?? i.clienteNome).toLowerCase().includes(q.search.toLowerCase()))
      return ok(filtrados, { meta: { total: filtrados.length, totalCount: filtrados.length } })
    }
    const ficha = path.match(/^\/api\/clientes\/(cli-\d)$/)
    if (ficha) return ok({ ...pessoas.find((p) => p.id === ficha[1]), enderecos: [], consentimentos: [] })
    if (path === '/api/atendimento/conversas') return ok([])
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
  return { contexto, page, consultas }
}
const hrefDe = (local) => local.getAttribute('href')
const buscaDeClientes = (page) => page.getByRole('combobox', { name: 'Buscar', exact: true })

try {
  // 1. Início: Clientes e Pedidos são cartões da grade e levam direto à tela, sem passar pelo menu de Cadastros.
  {
    const { contexto, page } = await preparar()
    const grade = page.getByRole('list', { name: 'Módulos', exact: true })
    await grade.getByRole('link', { name: 'Clientes', exact: true }).waitFor()
    assert.equal(await hrefDe(grade.getByRole('link', { name: 'Clientes', exact: true })), '#/m/cadastros/clientes')
    assert.equal(await hrefDe(grade.getByRole('link', { name: 'Pedidos', exact: true })), '#/m/cadastros/pedidos')
    await page.screenshot({ path: `${pasta}/inicio-1440.png`, fullPage: true })

    // 2. O cartão abre a lista de Clientes; no trilho dos módulos o atalho fica marcado e Pedidos está a um clique.
    await grade.getByRole('link', { name: 'Clientes', exact: true }).click()
    await page.getByRole('row').filter({ hasText: 'Ana Paula' }).waitFor()
    const trilho = page.getByRole('navigation', { name: 'Módulos da loja', exact: true })
    assert.equal(await trilho.getByRole('link', { name: 'Clientes', exact: true }).getAttribute('aria-current'), 'true')
    assert.equal(await hrefDe(trilho.getByRole('link', { name: 'Pedidos', exact: true })), '#/m/cadastros/pedidos')

    // 3. Dentro de Cadastros a busca fica: vai a Pedidos e volta a Clientes com o mesmo texto.
    const comAna = page.waitForRequest((r) => new URL(r.url()).pathname === '/api/clientes' && new URL(r.url()).searchParams.get('search') === 'ana')
    await buscaDeClientes(page).fill('ana')
    await comAna
    await page.screenshot({ path: `${pasta}/clientes-com-busca-1440.png` })
    await trilho.getByRole('link', { name: 'Pedidos', exact: true }).click()
    await page.getByRole('searchbox', { name: 'Buscar pedido' }).waitFor()
    await page.getByRole('searchbox', { name: 'Buscar pedido' }).fill('bruno')
    await trilho.getByRole('link', { name: 'Clientes', exact: true }).click()
    assert.equal(await buscaDeClientes(page).inputValue(), 'ana', 'dentro de Cadastros a busca continua')
    // Abrir a ficha (ainda Cadastros) e voltar à lista também mantém a busca.
    await page.evaluate(() => { window.location.hash = '#/m/cadastros/clientes/cli-0' })
    await page.waitForFunction(() => window.location.hash === '#/m/cadastros/clientes/cli-0')
    await page.waitForTimeout(300)
    await page.goBack()
    await page.getByRole('row').filter({ hasText: 'Ana Paula' }).waitFor()
    assert.equal(await buscaDeClientes(page).inputValue(), 'ana', 'fechar a ficha mantém a busca')

    // 4. Saiu de Cadastros (Início) e voltou: a lista de Clientes começa limpa.
    await trilho.getByRole('link', { name: 'Ir para o Início' }).click()
    await page.getByRole('list', { name: 'Módulos', exact: true }).getByRole('link', { name: 'Clientes', exact: true }).click()
    await page.getByRole('row').filter({ hasText: 'Carla Souza' }).waitFor()
    assert.equal(await buscaDeClientes(page).inputValue(), '', 'ao voltar para Cadastros a busca de Clientes começa limpa')
    await page.screenshot({ path: `${pasta}/clientes-limpo-1440.png` })

    // 5. Pedidos guarda os filtros ao ir à conversa e voltar (#2478): essa memória fica.
    await page.getByRole('navigation', { name: 'Módulos da loja', exact: true }).getByRole('link', { name: 'Pedidos', exact: true }).click()
    assert.equal(await page.getByRole('searchbox', { name: 'Buscar pedido' }).inputValue(), 'bruno', 'Pedidos mantém a busca (#2478)')
    await contexto.close()
  }

  // 6. Trilho do Atendimento: Clientes e Pedidos ao lado das conversas.
  {
    const { contexto, page } = await preparar({ hash: '#/m/atendimento/balcao' })
    const trilho = page.getByRole('navigation', { name: 'Navegação', exact: true })
    await trilho.getByRole('link', { name: 'Clientes', exact: true }).waitFor()
    assert.equal(await hrefDe(trilho.getByRole('link', { name: 'Clientes', exact: true })), '#/m/cadastros/clientes')
    assert.equal(await hrefDe(trilho.getByRole('link', { name: 'Pedidos', exact: true })), '#/m/cadastros/pedidos')
    await page.screenshot({ path: `${pasta}/atendimento-trilho-1440.png` })
    await contexto.close()
  }

  // 7. Perfil sem a tela Pedidos (sem `verPedidos`): o atalho Clientes fica, o de Pedidos some.
  {
    const { contexto, page } = await preparar({ verPedidos: false })
    const grade = page.getByRole('list', { name: 'Módulos', exact: true })
    await grade.getByRole('link', { name: 'Clientes', exact: true }).waitFor()
    assert.equal(await grade.getByRole('link', { name: 'Pedidos', exact: true }).count(), 0)
    await contexto.close()
  }

  assert.deepEqual(erros, [])
  console.log('ok prova-2870: atalhos Clientes e Pedidos e busca limpa ao sair de Cadastros')
} finally {
  await browser.close()
}
