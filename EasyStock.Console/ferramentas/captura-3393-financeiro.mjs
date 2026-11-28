// #3393: capturas do Financeiro com massa de prova maior (8 contas, 2 vencidas, 3 períodos) para a crítica impeccable.
// Não afirma comportamento (isso é da prova-financeiro-navegador); só desenha as telas com dados de loja de verdade.
// CONSOLE_URL aponta o Vite em modo API; EVIDENCIAS_UX a pasta das capturas.
//
// Roda com: CONSOLE_URL=http://127.0.0.1:5186 node ferramentas/captura-3393-financeiro.mjs
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
const { MODULOS } = await import('../src/dominio/modulos.js')
const { chromium } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright')
const origem = process.env.CONSOLE_URL ?? 'http://127.0.0.1:5186'
const evidencias = process.env.EVIDENCIAS_UX ?? 'builds/captura-3393'
mkdirSync(evidencias, { recursive: true })

const empresaId = '11111111-1111-4111-8111-111111111111'
const hoje = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo' }).format(new Date())
const dia = (delta) => new Date(Date.parse(`${hoje}T12:00:00-03:00`) + delta * 86400000).toISOString()
const categorias = [
  { id: 'c1000000-0000-4000-8000-000000000001', nome: 'Insumos', tipo: 'Ambas', ativa: true },
  { id: 'c1000000-0000-4000-8000-000000000002', nome: 'Aluguel', tipo: 'Ambas', ativa: true },
  { id: 'c1000000-0000-4000-8000-000000000003', nome: 'Encomendas', tipo: 'Ambas', ativa: true },
]
let seq = 0
const contaDe = (descricao, valor, vence, categoria, { pago = 0, status = 'Aberta' } = {}) => {
  seq++
  const id = `a${String(seq).padStart(7, '0')}-0000-4000-8000-000000000000`
  const saldo = Math.round((valor - pago) * 100) / 100
  return { id, empresaId, descricao, categoriaFinanceiraId: categorias[categoria].id, centroCustoId: null, valorTotal: valor, totalPago: pago, totalRecebido: pago, pendente: status === 'Cancelada' ? 0 : saldo, status, dataEmissao: dia(-20), observacoes: null,
    parcelas: [{ id: `b${String(seq).padStart(7, '0')}-0000-4000-8000-000000000000`, numero: 1, valor, valorPago: pago, saldo, status: saldo === 0 ? 'Paga' : pago ? 'ParcialmentePaga' : 'Pendente', dataVencimento: dia(vence), pagamentos: [] }] }
}
const pagar = [
  contaDe('Farinha e fermento (Moinho Sul)', 486.9, -3, 0),
  contaDe('Conta de luz', 312.45, -1, 1),
  contaDe('Aluguel da loja', 2800, 4, 1),
  contaDe('Embalagens kraft', 219.5, 9, 0, { pago: 100, status: 'ParcialmentePaga' }),
  contaDe('Manutenção do forno', 650, 17, 0),
  contaDe('Gás de cozinha', 138, 25, 0, { pago: 138, status: 'Paga' }),
]
const receber = [
  contaDe('Encomenda festa Ana Paula', 940, 2, 2),
  contaDe('Café da manhã empresa Lume', 1680, 12, 2),
]
const dashboard = { totalAVencer30dPagar: 3707.5 - 100, totalAVencer30dReceber: 2620, totalVencidoPagar: 799.35, totalVencidoReceber: 0, totalPagoMes: 234.4, totalRecebidoMes: 1020, qtdContasPagarAVencer30d: 4, qtdContasReceberAVencer30d: 2 }
// Semanas do mês filtrado; o realizado soma o mesmo que o Extrato (entradas 1.020,00, saídas 234,40).
const fluxo = [
  { rotulo: 'Semana 1', previstoReceber: 0, realizadoReceber: 0, previstoPagar: 138, realizadoPagar: 0 },
  { rotulo: 'Semana 2', previstoReceber: 0, realizadoReceber: 0, previstoPagar: 0, realizadoPagar: 0 },
  { rotulo: 'Semana 3', previstoReceber: 940, realizadoReceber: 0, previstoPagar: 798.35, realizadoPagar: 138 },
  { rotulo: 'Semana 4', previstoReceber: 1680, realizadoReceber: 1020, previstoPagar: 2800, realizadoPagar: 96.4 },
].map((item, indice) => ({ ...item, inicioBucket: dia(-27 + indice * 7), fimBucket: dia(-21 + indice * 7) }))
const movimentos = [
  { id: 'e0000001-0000-4000-8000-000000000000', tipo: 'entrada', descricao: 'Encomenda bolo de aniversário', valor: 180, dataMovimento: dia(0), metodo: 'pix' },
  { id: 'e0000002-0000-4000-8000-000000000000', tipo: 'saida', descricao: 'Ovos da granja', valor: 96.4, dataMovimento: dia(0), metodo: 'dinheiro' },
  { id: 'e0000003-0000-4000-8000-000000000000', tipo: 'entrada', descricao: 'Recebimento Lume (parcial)', valor: 840, dataMovimento: dia(-1), metodo: 'pix' },
  { id: 'e0000004-0000-4000-8000-000000000000', tipo: 'saida', descricao: 'Gás de cozinha', valor: 138, dataMovimento: dia(-2), metodo: 'pix' },
]
const acoes = Object.fromEntries(['visualizarContasPagar', 'visualizarContasReceber', 'gerenciarContasPagar', 'gerenciarContasReceber', 'gerenciarCategoriasFinanceiras', 'gerenciarCentrosCusto', 'gerenciarCaixa', 'verCadastrosFinanceiros', 'verFechamentosLegados'].map((nome) => [nome, true]))

const browser = await chromium.launch({ headless: true })
try {
  for (const [nome, viewport, tema] of [['desktop', { width: 1440, height: 960 }, 'light'], ['celular', { width: 390, height: 844 }, 'light'], ['escuro', { width: 1440, height: 960 }, 'dark']]) {
    const context = await browser.newContext({ viewport, reducedMotion: 'reduce', colorScheme: tema })
    await context.grantPermissions(['local-network-access'], { origin: origem }).catch(() => {})
    await context.addInitScript(({ empresaId }) => sessionStorage.setItem('easystok.sessao', JSON.stringify({ token: 'captura-3393', expiraEm: Date.now() + 3600000, empresa: { id: empresaId, nome: 'Padaria de teste' }, usuario: { id: '22222222-2222-4222-8222-222222222222', nome: 'Dona de teste' } })), { empresaId })
    await context.route('**/api/**', async (route) => {
      const url = new URL(route.request().url()), caminho = url.pathname
      if (!caminho.startsWith('/api/')) return route.continue()
      const ok = (data, meta = {}) => route.fulfill({ json: { data, meta } })
      if (caminho === '/api/auth/me/modulos') return ok({ modulos: MODULOS.map(({ id }) => ({ id, liberado: true })), acoes })
      if (caminho === '/api/financeiro/categorias') return ok(categorias)
      if (caminho === '/api/financeiro/centros-custo') return ok([])
      if (caminho === '/api/financeiro/dashboard') return ok(dashboard)
      if (caminho === '/api/financeiro/fluxo-caixa') return ok(fluxo)
      const lista = caminho.startsWith('/api/contas-a-receber') ? receber : pagar
      if (/^\/api\/contas-a-(pagar|receber)$/.test(caminho)) return ok(lista, { total: lista.length, page: 1, limit: 20 })
      if (/^\/api\/contas-a-(pagar|receber)\//.test(caminho)) return ok(lista.find((c) => caminho.includes(c.id)) ?? lista[0])
      if (caminho === '/api/caixa/movimentos') return ok(movimentos, { total: movimentos.length, page: 1, limit: 100 })
      return ok([])
    })
    const page = await context.newPage()
    for (const tela of ['visao-geral', 'pagar', 'receber', 'extrato', 'relatorios']) {
      await page.goto(`${origem}/#/m/financeiro/${tela}`)
      await page.getByRole('main').locator('h1, h2').first().waitFor({ timeout: 60000 })
      await page.waitForLoadState('networkidle')
      await page.evaluate(async () => { await document.fonts.ready; await new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r))) })
      await page.screenshot({ path: join(evidencias, `${tela}-${nome}.png`), fullPage: true })
    }
    await context.close()
  }
  console.log(`captura-3393: 15 telas em ${evidencias}`)
} finally {
  await browser.close()
}
