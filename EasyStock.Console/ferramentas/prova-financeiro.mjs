import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(nome, contexto, proximo) {
    if (nome.startsWith('.') && !/\.[a-z]+$/i.test(nome)) { try { return proximo(nome + '.js', contexto) } catch { /* resolução original */ } }
    return proximo(nome, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const API_BASE = ''; export const FONTE_API = true" }
    return proximo(url, contexto)
  },
})
const { parcelasFinanceiras, diaFinanceiro, extratoFinanceiro } = await import('../src/dominio/financeiro.js')
const parcelas = parcelasFinanceiras(10001, 3, '2026-01-31')
assert.equal(parcelas.reduce((total, item) => total + Math.round(item.valor * 100), 0), 10001)
assert.deepEqual(parcelas.map((item) => diaFinanceiro(item.dataVencimento)), ['2026-01-31', '2026-02-28', '2026-03-31'])
assert.equal(diaFinanceiro(parcelasFinanceiras(10000, 2, '2028-01-31')[1].dataVencimento), '2028-02-29')
assert.equal(diaFinanceiro('2026-10-11T01:00:00Z'), '2026-10-10')
assert.deepEqual(parcelasFinanceiras(1, 2, '2026-01-01'), [])
const pagamento = { id: 'pagamento', valor: 50, metodo: 'pix', status: 'Confirmado', dataPagamento: '2026-10-11T12:00:00Z', movimentoCaixaId: 'movimento' }
const contas = [{ lado: 'receber', conta: { id: 'conta', descricao: 'Pedido', parcelas: [{ pagamentos: [pagamento] }] } }]
const movimentos = [{ id: 'movimento', tipo: 'entrada', valor: 50, dataMovimento: pagamento.dataPagamento }, { id: 'saida', tipo: 'saida', valor: 10, dataMovimento: pagamento.dataPagamento }]
let linhas = extratoFinanceiro(contas, movimentos, '2026-10-11', '2026-10-11')
assert.equal(linhas.length, 2, 'o pagamento ligado ao Caixa não duplica')
assert.equal(linhas.find((item) => item.id === 'saida').lado, 'pagar')
pagamento.status = 'Estornado'
assert.equal(extratoFinanceiro(contas, movimentos, '2026-10-11', '2026-10-11')[0].estornado, true)
assert.equal(extratoFinanceiro(contas, movimentos, '2026-10-12', '2026-10-12').length, 0)

const { gravarSessao } = await import('../src/infra/api/sessao.js')
const armazenamento = new Map()
globalThis.sessionStorage = { getItem: (chave) => armazenamento.get(chave) ?? null, setItem: (chave, valor) => armazenamento.set(chave, valor), removeItem: (chave) => armazenamento.delete(chave) }
globalThis.localStorage = { getItem: () => null, removeItem: () => {} }
globalThis.window = { dispatchEvent: () => {} }
const empresaId = '11111111-1111-4111-8111-111111111111'
gravarSessao({ token: 'token-de-prova', expiraEm: Date.now() + 3600000, empresa: { id: empresaId } })
const api = await import('../src/infra/api/financeiroApi.js')
const chamadas = []
let falharPagina = false
globalThis.fetch = async (caminho, opcoes) => {
  assert.equal(opcoes.headers.Authorization, 'Bearer token-de-prova')
  const url = new URL(caminho, 'http://localhost'), corpo = opcoes.body && JSON.parse(opcoes.body)
  chamadas.push({ caminho: url.pathname, query: url.searchParams, metodo: opcoes.method, corpo })
  if (opcoes.method !== 'GET') { assert.equal(corpo.empresaId, empresaId); return new Response(JSON.stringify({ data: {} }), { status: 200 }) }
  if (url.pathname === '/api/caixa/movimentos' || url.pathname === '/api/contas-a-receber') return new Response(JSON.stringify({ data: [], meta: { total: 0 } }), { status: 200 })
  const numero = Number(url.searchParams.get('page'))
  if (numero === 2 && falharPagina) return new Response(JSON.stringify({ error: { message: 'Falha na página 2' } }), { status: 503 })
  const data = Array.from({ length: numero === 1 ? 100 : 1 }, (_, indice) => ({ id: `${numero}-${indice}`, descricao: 'Conta', parcelas: [] }))
  return new Response(JSON.stringify({ data, meta: { total: 101 } }), { status: 200 })
}
await api.obterExtratoFinanceiro({ inicio: '2026-10-01', fim: '2026-10-31' }, { visualizarContasPagar: true, visualizarContasReceber: false })
assert.deepEqual(chamadas.filter((item) => item.caminho === '/api/contas-a-pagar').map((item) => item.query.get('page')), ['1', '2'])
assert.equal(chamadas.some((item) => item.caminho === '/api/contas-a-receber'), false, 'não consulta o lado sem permissão')
falharPagina = true
await assert.rejects(() => api.obterExtratoFinanceiro({ inicio: '2026-10-01', fim: '2026-10-31' }, { visualizarContasPagar: true }), /Falha na página 2/)
await api.adicionarParcelaFinanceira('pagar', 'conta', { numero: 2, valor: 10, dataVencimento: parcelas[0].dataVencimento })
assert.equal(chamadas.at(-1).corpo.contaPagarId, 'conta')
await api.adicionarParcelaFinanceira('receber', 'conta', { numero: 2, valor: 10, dataVencimento: parcelas[0].dataVencimento })
assert.equal(chamadas.at(-1).corpo.contaReceberId, 'conta')
await api.registrarParcelaFinanceira('pagar', 'conta', 'parcela', { valor: 10, gatewayTransactionId: 'tentativa-unica' })
assert.equal(chamadas.at(-1).caminho, '/api/contas-a-pagar/conta/parcelas/parcela/pagamentos')
assert.equal(chamadas.at(-1).corpo.gatewayTransactionId, 'tentativa-unica')
console.log('OK: centavos, parcelas, fim de mês, fuso, extrato sem duplicação, estornos, paginação completa, falha parcial, permissões e empresa da sessão.')
