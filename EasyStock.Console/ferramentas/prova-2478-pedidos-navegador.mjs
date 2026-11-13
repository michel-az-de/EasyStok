// #2478: tela Pedidos de Cadastros no app real com HTTP controlado. Lista, filtros no contrato do `GET api/pedidos`,
// paginação, detalhe, atalho para a conversa, vazio, falha, perfil sem acesso, celular e tema escuro.
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { mkdirSync } from 'node:fs'
import { resolve } from 'node:path'
const { chromium } = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const pasta = resolve(import.meta.dirname, '../.build/2478/pedidos')
mkdirSync(pasta, { recursive: true })
const URL_CONSOLE = process.env.CONSOLE_TEST_URL ?? 'http://localhost:5735'
const HASH = '#/m/cadastros/pedidos'

const hoje = new Date()
const NOMES = ['Ana Paula', 'Bruno Lima', 'Carla Souza', 'Diego Alves', 'Elisa Prado']
const SITUACOES = ['aguardando_pagamento', 'aguardando', 'aprovado_baba', 'preparando', 'entregue']
const pedidos = Array.from({ length: 25 }, (_, i) => ({
  id: `pedido-${i + 1}`, numeroDoDia: i + 1, clienteId: `cli-${i % 5}`, clienteNome: NOMES[i % 5], clienteTelefone: '11999990000',
  status: SITUACOES[i % 5], total: 40 + i, totalPago: i % 5 === 4 ? 40 + i : 0, origem: i % 2 ? 'whatsapp' : 'web',
  criadoEm: new Date(hoje.getTime() - i * 60000).toISOString(),
  // Onda 3: o entregue traz a hora da entrega e o dia de produção do número (encomenda para outro dia).
  ...(i % 5 === 4 ? { entreguEm: new Date(hoje.getTime() - i * 30000).toISOString(), dataNumero: '2030-01-20' } : {}),
}))
const detalheDe = (p) => ({
  pedido: { ...p, observacoes: 'Sem cebola, por favor.' },
  itens: [{ id: 'i1', nome: 'Lasanha de berinjela', quantidade: 2, unidade: 'un', precoUnitario: 20, subtotal: 40, observacao: 'Bem quente' }],
  pagamentos: p.totalPago ? [{ metodo: 'pix', valor: p.totalPago, pagoEm: p.criadoEm, registradoPorNome: 'Thati' }] : [],
  eventos: [{ tipo: 'criado', criadoEm: p.criadoEm, origem: p.origem }],
})

const browser = await chromium.launch({ headless: true })
const erros = []
async function preparar({ largura = 1280, altura = 800, verPedidos = true } = {}) {
  const consultas = []
  const contexto = await browser.newContext({ viewport: { width: largura, height: altura }, isMobile: largura < 700, hasTouch: largura < 700, locale: 'pt-BR', timezoneId: 'America/Sao_Paulo', reducedMotion: 'reduce' })
  await contexto.addInitScript(() => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'fixture-controlada', expiraEm: Date.now() + 3600000, empresa: { id: 'empresa', nome: 'Loja dos pedidos' }, usuario: { id: 'dona', nome: 'Dona de teste' } })))
  await contexto.route('**/api/**', async (route) => {
    const req = route.request(), url = new URL(req.url()), path = url.pathname
    if (!path.startsWith('/api/')) return route.continue() // módulos do Vite em src/infra/api/
    assert.ok(req.method() === 'GET' || path.endsWith('/marcar-lida'), `Escrita inesperada: ${req.method()} ${path}`)
    const ok = (data, extra = {}) => route.fulfill({ json: { data, ...extra } })
    if (path === '/api/auth/me/modulos') return ok({ modulos: ['atendimento', 'cadastros'].map((id) => ({ id, liberado: true })), acoes: verPedidos ? { verPedidos: true } : {} })
    if (path === '/api/pedidos') {
      const q = Object.fromEntries(url.searchParams)
      consultas.push(q)
      if (q.search === 'falha') return route.fulfill({ status: 503, json: { error: { message: 'Pedidos indisponíveis no teste.' } } })
      const situacoes = q.status ? q.status.split(',') : null
      const filtrados = pedidos.filter((p) => (!q.search || p.clienteNome.toLowerCase().includes(q.search.toLowerCase()))
        && (!situacoes || situacoes.includes(p.status)))
      const tamanho = Number(q.pageSize), pagina = Number(q.page)
      return ok(filtrados.slice((pagina - 1) * tamanho, pagina * tamanho), { meta: { totalCount: filtrados.length } })
    }
    const unico = path.match(/^\/api\/pedidos\/([^/]+)$/)
    if (unico) return ok(detalheDe(pedidos.find((p) => p.id === unico[1])))
    if (path === '/api/atendimento/conversas') return ok([{ id: 'conversa-ana', contatoNome: 'Ana Paula', contatoIdExterno: '5511999990000', clienteId: 'cli-0', canal: 'WhatsApp', situacao: 'Assumida', ultimaMensagemEm: hoje.toISOString(), naoLidas: 0 }])
    if (path.endsWith('/mensagens')) return ok([])
    if (path.endsWith('/pedido')) return ok(null)
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
  await page.goto(`${URL_CONSOLE}/${HASH}`)
  return { contexto, page, consultas }
}
const linhas = (page) => page.getByRole('table').locator('tbody tr')
const ultima = (consultas) => consultas.at(-1)

// 1. Mesa: lista, contrato da consulta e paginação.
{
  const { contexto, page, consultas } = await preparar()
  const painel = page.getByRole('region', { name: 'Pedidos', exact: true })
  await painel.getByRole('heading', { name: 'Pedidos', exact: true }).waitFor()
  await page.getByRole('navigation', { name: 'Telas de Cadastros', exact: true }).getByRole('link', { name: 'Pedidos' }).waitFor()
  await linhas(page).nth(19).waitFor()
  assert.equal(await linhas(page).count(), 20)
  assert.deepEqual(ultima(consultas), { empresaId: 'empresa', page: '1', pageSize: '20', sort: 'criadoem', order: 'desc' })
  await painel.getByRole('status').filter({ hasText: '25 pedidos' }).waitFor()
  await painel.getByText('Página 1 de 2', { exact: true }).waitFor()
  assert.match(await linhas(page).first().innerText(), /Nº 001/)
  await page.screenshot({ path: `${pasta}/lista-1280-claro.png`, fullPage: true })

  await painel.getByRole('button', { name: 'Próxima', exact: true }).click()
  await painel.getByText('Página 2 de 2', { exact: true }).waitFor()
  await page.waitForFunction(() => document.querySelectorAll('table tbody tr').length === 5)
  assert.equal(ultima(consultas).page, '2')

  // 2. Filtros: a busca espera a digitação, "Na fila" vira lista de situações e a página volta para 1.
  const respostaAna = page.waitForResponse((r) => new URL(r.url()).searchParams.get('search') === 'ana')
  await painel.getByRole('searchbox', { name: 'Buscar pedido' }).fill('ana')
  await respostaAna
  await painel.getByRole('status').filter({ hasText: '5 pedidos' }).waitFor()
  assert.equal(ultima(consultas).search, 'ana'); assert.equal(ultima(consultas).page, '1')
  assert.equal(consultas.filter((q) => q.search && q.search !== 'ana').length, 0, 'busca sem consulta por tecla')
  await painel.getByRole('searchbox', { name: 'Buscar pedido' }).fill('')
  await painel.getByRole('combobox', { name: 'Situação' }).click()
  await page.getByRole('option', { name: 'Na fila', exact: true }).click()
  await painel.getByRole('status').filter({ hasText: '10 pedidos' }).waitFor()
  assert.equal(ultima(consultas).status, 'aguardando,aprovado_baba')
  const porValor = page.waitForResponse((r) => new URL(r.url()).searchParams.get('sort') === 'total')
  await painel.getByRole('combobox', { name: 'Ordem' }).click()
  await page.getByRole('option', { name: 'Maior valor', exact: true }).click()
  await porValor
  await painel.getByRole('status').filter({ hasText: '10 pedidos' }).waitFor()
  assert.equal(ultima(consultas).sort, 'total'); assert.equal(ultima(consultas).order, 'desc')

  // 3. Vazio com filtro oferece limpar; limpar devolve a lista inteira.
  await painel.getByRole('searchbox', { name: 'Buscar pedido' }).fill('ninguém')
  await painel.getByText('Nenhum pedido com estes filtros').waitFor()
  await painel.getByRole('button', { name: 'Limpar filtros', exact: true }).click()
  await painel.getByRole('status').filter({ hasText: '25 pedidos' }).waitFor()
  assert.equal(await painel.getByRole('searchbox', { name: 'Buscar pedido' }).inputValue(), '')

  // 4. Falha: alerta legível e nova tentativa.
  await painel.getByRole('searchbox', { name: 'Buscar pedido' }).fill('falha')
  await painel.getByRole('alert').filter({ hasText: 'Pedidos indisponíveis no teste.' }).waitFor()
  const antes = consultas.length
  await painel.getByRole('button', { name: 'Tentar de novo', exact: true }).click()
  await page.waitForResponse((r) => new URL(r.url()).searchParams.get('search') === 'falha')
  await painel.getByRole('alert').filter({ hasText: 'Pedidos indisponíveis' }).waitFor()
  assert.ok(consultas.length > antes, 'Tentar de novo consulta de novo')
  await painel.getByRole('searchbox', { name: 'Buscar pedido' }).fill('')
  await linhas(page).nth(19).waitFor()

  // 5. Detalhe: itens, totais, pagamento, observação, histórico e atalho para a conversa da cliente.
  await painel.getByRole('button', { name: /^Abrir Pedido Nº 001 de \d\d\/\d\d, Ana Paula$/ }).click()
  const detalhe = page.getByRole('dialog', { name: /^Pedido Nº 001 de \d\d\/\d\d$/ })
  await detalhe.waitFor()
  const texto = await detalhe.innerText()
  for (const esperado of ['Lasanha de berinjela', 'Bem quente', 'Falta receber', 'Nenhum pagamento registrado.', 'Sem cebola, por favor.', 'Aguardando pagamento', 'Painel']) {
    assert.ok(texto.includes(esperado), `detalhe mostra "${esperado}"`)
  }
  await detalhe.getByText(/^Histórico \(1 registro\)$/).click()
  await page.screenshot({ path: `${pasta}/detalhe-1280-claro.png` })
  await detalhe.getByRole('button', { name: 'Abrir conversa', exact: true }).click()
  await detalhe.waitFor({ state: 'hidden' })
  await page.waitForFunction(() => location.hash.startsWith('#/m/atendimento/balcao'))

  // 6. Voltar à tela guarda filtros e página; o pedido pago mostra "Pago".
  await page.goto(`${URL_CONSOLE}/${HASH}`)
  await linhas(page).nth(4).waitFor()
  await painel.getByRole('button', { name: /^Abrir Pedido Nº 005 de/ }).click()
  const pago = page.getByRole('dialog', { name: 'Pedido Nº 005 de 20/01', exact: true })
  await pago.waitFor()
  assert.match(await pago.innerText(), /Pago/); assert.match(await pago.innerText(), /Pix/)
  assert.match(await pago.innerText(), /Entregue em/, 'detalhe mostra a hora da entrega')
  assert.equal(await pago.getByRole('button', { name: 'Abrir conversa' }).count(), 0, 'sem conversa carregada, sem atalho')
  await page.keyboard.press('Escape')
  await pago.waitFor({ state: 'hidden' })

  // 7. Tema escuro.
  await page.getByRole('button', { name: /^Conta de / }).locator('visible=true').first().click()
  await page.getByRole('group', { name: 'Conta', exact: true }).getByRole('radio', { name: 'Escuro', exact: true }).click()
  await page.keyboard.press('Escape')
  await page.screenshot({ path: `${pasta}/lista-1280-escuro.png`, fullPage: true })
  await contexto.close()
}

// 8. Celular: a coluna Cliente sai, o nome desce para baixo do número e a página não rola de lado.
{
  const { contexto, page } = await preparar({ largura: 390, altura: 844 })
  await linhas(page).nth(19).waitFor()
  const primeira = linhas(page).first()
  assert.equal(await primeira.locator('td').nth(1).isVisible(), false, 'coluna Cliente sai no celular')
  assert.equal(await primeira.locator('td').nth(2).isVisible(), false, 'coluna Situação sai no celular')
  assert.ok(await primeira.locator('td').first().getByText('Ana Paula').isVisible())
  assert.ok(await primeira.locator('td').first().getByText('Aguardando pagamento').isVisible())
  const botao = primeira.getByRole('button', { name: /^Abrir Pedido/ })
  const caixa = await botao.boundingBox()
  assert.ok(caixa.height < 56, `"Abrir" numa linha só (${caixa.height}px)`)
  const cortados = await page.evaluate(() => [...document.querySelectorAll('table tbody tr:first-child td')].filter((td) => td.offsetParent && td.scrollWidth > td.clientWidth + 1).length)
  assert.equal(cortados, 0, 'nenhuma célula cortada')
  const rolagem = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
  assert.ok(rolagem <= 1, `sem rolagem lateral (${rolagem}px)`)
  await page.screenshot({ path: `${pasta}/lista-390-claro.png`, fullPage: true })
  await primeira.getByRole('button', { name: /^Abrir Pedido Nº 001/ }).tap()
  const detalhe = page.getByRole('dialog', { name: /^Pedido Nº 001/ })
  await detalhe.waitFor()
  const largura = await detalhe.evaluate((el) => el.scrollWidth - el.clientWidth)
  const fundos = await detalhe.locator('dl').last().evaluate((dl) => [...dl.children].map((d) => getComputedStyle(d).backgroundColor))
  assert.ok(fundos.every((c) => c === 'rgba(0, 0, 0, 0)'), `totais sem fundo próprio (${fundos})`)
  assert.ok(largura <= 1, `detalhe sem rolagem lateral (${largura}px)`)
  await page.screenshot({ path: `${pasta}/detalhe-390-claro.png` })
  await contexto.close()
}

// 9. Perfil sem `verPedidos` (Visualizador): sem item no menu e sem consulta que a API negaria.
{
  const { contexto, page, consultas } = await preparar({ verPedidos: false })
  const menu = page.getByRole('navigation', { name: 'Telas de Cadastros', exact: true })
  await menu.waitFor()
  assert.equal(await menu.getByRole('link', { name: 'Pedidos' }).count(), 0)
  assert.equal(consultas.length, 0)
  await contexto.close()
}

await browser.close()
assert.deepEqual(erros, [], 'sem erro de página')
console.log(`prova-2478-pedidos-navegador: ok (capturas em ${pasta})`)
