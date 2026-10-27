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

// #2095: lista orientada a vencimento, rótulos honestos, formulário e período.
const fin = await import('../src/dominio/financeiro.js')
const hoje = '2026-10-27'
const parcela = (dia, saldo, status = 'Pendente', valor = saldo) => ({ id: dia + status, numero: 1, valor, saldo, status, dataVencimento: fin.instanteFinanceiro(dia), pagamentos: [] })
const conta = (id, status, parcelas, extra = {}) => ({ id, descricao: id, status, parcelas, pendente: parcelas.reduce((s, p) => s + p.saldo, 0), valorTotal: parcelas.reduce((s, p) => s + p.valor, 0), dataEmissao: fin.instanteFinanceiro('2026-10-01'), ...extra })
const contasDoDia = [
  conta('vencida', 'Vencida', [parcela('2026-10-20', 10)]),
  conta('hoje', 'Aberta', [parcela(hoje, 20)]),
  conta('amanha', 'Aberta', [parcela('2026-10-28', 30)]),
  conta('semana-que-vem', 'Aberta', [parcela('2026-11-03', 40)]),
  conta('longe', 'Aberta', [parcela('2026-11-20', 50)]),
  conta('rascunho-hoje', 'Rascunho', [parcela(hoje, 60)]),
  conta('cancelada-hoje', 'Cancelada', [parcela(hoje, 70, 'Cancelada')], { valorTotal: 0 }),
  conta('paga-hoje', 'Paga', [parcela(hoje, 0, 'Paga', 80)], { pendente: 0 }),
]
const doChip = (chip) => contasDoDia.filter(fin.planoDoChip(chip, hoje).local ?? (() => true)).map((c) => c.id)
const emAberto = ['vencida', 'hoje', 'amanha', 'semana-que-vem', 'longe']
assert.deepEqual(doChip('hoje'), ['hoje'], 'vence hoje: só conta em aberto, sem rascunho, cancelada ou paga')
assert.deepEqual(doChip('semana'), ['amanha', 'semana-que-vem'], 'próximos 7 dias: de amanhã a hoje + 7')
assert.deepEqual(doChip('abertas'), emAberto, 'em aberto: sem rascunho, cancelada nem paga')
assert.deepEqual(doChip('vencidas'), emAberto, 'vencidas: a API filtra o status e o cliente tira as fechadas')
assert.equal(fin.planoDoChip('vencidas', hoje).api.status, 'Vencida')
assert.equal(fin.planoDoChip('todas', hoje).local, null, 'Todas pagina no servidor')
assert.equal(fin.CHIP_PADRAO, 'abertas')
const planoHoje = fin.planoDoChip('hoje', hoje).api
assert.equal(planoHoje.vencimentoDe, '2026-10-27T03:00:00.000Z', 'filtro de vencimento vai como instante (F4)')
assert.equal(planoHoje.vencimentoAte, '2026-10-28T02:59:59.999Z')
assert.deepEqual(fin.filtrosDaApi({ busca: ' gás ', status: '', vencimentoDe: hoje, vencimentoAte: hoje }), { busca: 'gás', status: '', vencimentoDe: '2026-10-27T03:00:00.000Z', vencimentoAte: '2026-10-28T02:59:59.999Z' })
assert.deepEqual(fin.ordenarPorVencimento([contasDoDia[4], contasDoDia[7], contasDoDia[1], contasDoDia[0]]).map((c) => c.id), ['vencida', 'hoje', 'longe', 'paga-hoje'], 'ordem pela próxima parcela em aberto; quitada no fim')
assert.equal(fin.tomDoStatusConta('Vencida'), 'perigo', 'C2: vencida com o tom que existe na Pilula')
assert.equal(fin.tomDoStatusConta('Paga'), 'ok')
assert.equal(fin.statusDaConta(conta('atrasada', 'Aberta', [parcela('2026-10-26', 5)]), hoje), 'Vencida', 'aberta com parcela de ontem já aparece vencida')
assert.equal(fin.statusDaConta(contasDoDia[1], hoje), 'Aberta')
assert.equal(fin.statusDaConta(contasDoDia[6], hoje), 'Cancelada')
assert.deepEqual(fin.valorDaLinha(contasDoDia[1]), { rotulo: 'Falta', valor: 20 })
assert.deepEqual(fin.valorDaLinha(contasDoDia[6]), { rotulo: 'Cancelada', valor: 70 }, 'C3: cancelada mostra o valor lançado, não R$ 0,00')
assert.deepEqual(fin.valorDaLinha(contasDoDia[7], 'receber'), { rotulo: 'Recebido', valor: 80 })
assert.equal(fin.valorLancado({ parcelas: [{ valor: 0.1 }, { valor: 0.2 }] }), 0.3, 'soma em centavos')
assert.equal(fin.proximaParcela(conta('duas', 'Aberta', [parcela('2026-11-10', 5), parcela('2026-10-30', 5), parcela('2026-10-01', 0, 'Paga', 5)])).dataVencimento, fin.instanteFinanceiro('2026-10-30'))
assert.equal(fin.nomeStatusConta('ParcialmentePaga', 'receber'), 'Parcialmente recebida', 'C10')
assert.equal(fin.nomeStatusConta('Paga', 'receber'), 'Recebida')
assert.equal(fin.nomeStatusConta('ParcialmentePaga'), 'Parcialmente paga')
assert.equal(fin.nomeStatusPagamento('Confirmado'), 'Registrado')
assert.equal(fin.palavrasDoLado('receber').data, 'Data do recebimento')
assert.equal(fin.palavrasDoLado('pagar').forma, 'Forma de pagamento')
assert.equal(fin.paginaValida(3, 20), 1, 'C12: página vazia volta para a primeira')
assert.equal(fin.paginaValida(2, 21), 2)
assert.equal(fin.paginaValida(1, 0), 1)
assert.deepEqual(fin.paginaDaLista([1, 2, 3, 4, 5], 2, 2), [3, 4])
assert.equal(fin.podeEditarConta({ status: 'Aberta' }, true), false, 'C5: a API só edita rascunho')
assert.equal(fin.podeEditarConta({ status: 'Rascunho' }, true), true)
assert.equal(fin.podeEditarConta({ status: 'Rascunho' }, false), false)
assert.equal(fin.podeRegistrarNaConta(contasDoDia[1], true), true)
assert.equal(fin.podeRegistrarNaConta(contasDoDia[1], false), false, '#476: só quem gerencia registra pagamento')
assert.equal(fin.podeRegistrarNaConta(contasDoDia[5], true), false, 'rascunho não recebe pagamento (F1)')
assert.equal(fin.podeRegistrarNaConta(contasDoDia[6], true), false, 'cancelada não recebe pagamento (F2)')
assert.equal(fin.podeRegistrarNaConta(contasDoDia[7], true), false)
assert.equal(fin.chipDaHash(fin.hashDoChip('pagar', 'vencidas')), 'vencidas')
assert.equal(fin.chipDaHash('#/m/financeiro/pagar'), null)
assert.equal(fin.chipDaHash('#/m/financeiro/pagar/inventado'), null)
const formulario = { descricao: 'Gás', categoria: 'c', centavos: 1000, quantidade: 1, emissao: hoje, vencimento: hoje }
assert.equal(fin.motivoContaInvalida(formulario), null)
assert.equal(fin.motivoContaInvalida({ ...formulario, vencimento: '2026-10-26' }), 'O primeiro vencimento não pode ser antes da emissão.', 'C8')
assert.equal(fin.motivoContaInvalida({ ...formulario, descricao: ' ' }), 'Escreva a descrição da conta.')
assert.equal(fin.motivoContaInvalida({ ...formulario, categoria: '' }), 'Escolha a categoria financeira.')
assert.equal(fin.motivoContaInvalida({ ...formulario, centavos: 0 }), 'Informe o valor total.')
assert.equal(fin.motivoContaInvalida({ ...formulario, centavos: 2, quantidade: 3 }), 'Cada parcela precisa de pelo menos R$ 0,01.')
assert.deepEqual(fin.mesFinanceiro('2026-10-27'), { inicio: '2026-10-01', fim: '2026-10-31' }, 'C4: relatório no mês inteiro')
assert.deepEqual(fin.mesFinanceiro('2028-02-10'), { inicio: '2028-02-01', fim: '2028-02-29' })
assert.equal(fin.diaMais('2026-12-31', 1), '2027-01-01')
const tecnica = "Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone', only UTC is supported."
assert.doesNotMatch(fin.mensagemFinanceira(tecnica), /Cannot|Kind|PostgreSQL/, 'F4: texto do Npgsql não vai para a tela')
assert.doesNotMatch(fin.mensagemFinanceira('System.InvalidOperationException: x'), /Exception/)
assert.equal(fin.mensagemFinanceira('So e possivel atualizar conta em Rascunho'), 'Só dá para editar uma conta em rascunho.')
assert.equal(fin.mensagemFinanceira('Categoria de teste indisponível'), 'Categoria de teste indisponível', 'mensagem humana passa como veio')

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
