// API/Kestrel e PostgreSQL da fixture R4. Somente provedores externos são controlados.
import assert from 'node:assert/strict'
import { readFileSync, mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { randomUUID } from 'node:crypto'

const fixture = JSON.parse(readFileSync(process.env.R4_FIXTURE, 'utf8'))
assert.ok(['localhost', '127.0.0.1'].includes(new URL(fixture.api).hostname))
assert.match(fixture.provedores, /controlados/)
const pasta = resolve(import.meta.dirname, '../.build/2395/real')
mkdirSync(pasta, { recursive: true })
const resultados = []
let token
async function requisicao(caminho, corpo, metodo = corpo === undefined ? 'GET' : 'POST', chave, esperado) {
  const resposta = await fetch(`${fixture.api}/api/${caminho}`, {
    method: metodo,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(chave ? { 'Idempotency-Key': chave } : {}) },
    body: corpo === undefined ? undefined : JSON.stringify(corpo),
  })
  const texto = await resposta.text()
  const json = texto ? JSON.parse(texto) : {}
  assert.ok(esperado ? resposta.status === esperado : resposta.ok, `${metodo} ${caminho}: ${resposta.status} ${JSON.stringify(json)}`)
  return json.data
}
function provou(texto) { resultados.push(texto); console.log(`ok ${texto}`) }
async function saldo(produto) { return (await requisicao(`estoque/${produto.itemEstoqueId}?empresaId=${fixture.empresaId}`)).qty }
async function detalhe(id) { return requisicao(`pedidos/${id}?empresaId=${fixture.empresaId}`) }
async function status(id, valor) { return requisicao(`pedidos/${id}/status`, { empresaId: fixture.empresaId, id, status: valor }, 'PATCH') }
async function criar(produto, qtd) {
  const vinculo = await requisicao(`atendimento/conversas/${fixture.conversaId}/vinculo-cliente`)
  return requisicao(`atendimento/conversas/${fixture.conversaId}/pedido`, {
    clienteId: vinculo.clienteId, vinculoRevisao: vinculo.vinculoRevisao,
    itens: [{ cardapioItemId: produto.cardapioItemId, qtd }], janelaId: fixture.janelaId,
    dataEntrega: fixture.dataEntrega, forma: 'na_entrega', enviarAoCliente: false,
  })
}

try {
  const dona = fixture.identidades.find(i => i.perfil === 'Dona' && i.empresaId === fixture.empresaId)
  const login = await requisicao('auth/login', { email: dona.email, senha: dona.senha, empresaId: dona.empresaId })
  token = login.token
  assert.ok(token)
  const conta = await requisicao('cadastros/contas-bancarias', { requestId: randomUUID(), nome: 'Conta da jornada R4',
    tipo: 'pagamento', instituicao: 'Laboratório', agencia: null, conta: '2395', titular: 'Casa R4', ativo: true, revisao: 0 })
  const outro = fixture.identidades.find(i => i.empresaId === fixture.outraEmpresaId)
  token = (await requisicao('auth/login', { email: outro.email, senha: outro.senha, empresaId: outro.empresaId })).token
  await requisicao(`cadastros/contas-bancarias/${conta.id}`, undefined, 'GET', undefined, 404)
  await requisicao(`atendimento/conversas/${fixture.conversaId}/vinculo-cliente`, undefined, 'GET', undefined, 404)
  await requisicao('cadastros/contas-bancarias', { requestId: randomUUID(), empresaId: fixture.empresaId, nome: 'Negada',
    tipo: 'pagamento', instituicao: 'Laboratório', conta: '2395', titular: 'Casa R4', ativo: true, revisao: 0 }, 'POST', undefined, 400)
  token = login.token
  provou('JWT de outra empresa não lê conta/conversa nem grava com EmpresaId divergente')
  const [lasanha] = fixture.produtos
  const inicial = await saldo(lasanha)
  assert.ok(inicial >= 2)
  await requisicao(`atendimento/conversas/${fixture.conversaId}/reabrir`, {})
  const pedido = await criar(lasanha, 2)
  const cadastroComprador = await requisicao(`clientes/${fixture.clienteId}?empresaId=${fixture.empresaId}`)
  assert.ok(cadastroComprador.cliente.orderCount > 0)
  const clientes = await requisicao(`clientes?empresaId=${fixture.empresaId}&relacionamento=cliente&pageSize=100`)
  assert.ok(clientes.some(c => c.id === fixture.clienteId))
  const leads = await requisicao(`clientes?empresaId=${fixture.empresaId}&relacionamento=lead&pageSize=100`)
  assert.ok(leads.every(c => c.id !== fixture.clienteId))
  provou('Pedido real transforma lead em cliente no detalhe e nos filtros da listagem')
  assert.equal(await saldo(lasanha), inicial)
  await status(pedido.pedidoId, 'preparando')
  assert.equal(await saldo(lasanha), inicial)
  await status(pedido.pedidoId, 'pronto')
  assert.equal(await saldo(lasanha), inicial - 2)
  provou(`Duas lasanhas: estoque ${inicial} ao criar/preparar e ${inicial - 2} somente em Pronto`)

  const pagamento = { empresaId: fixture.empresaId, pedidoId: pedido.pedidoId, metodo: 'dinheiro', valor: pedido.total }
  const chave = randomUUID()
  await requisicao(`pedidos/${pedido.pedidoId}/pagamentos`, pagamento, 'POST', chave)
  await requisicao(`pedidos/${pedido.pedidoId}/pagamentos`, pagamento, 'POST', chave)
  const pago = await detalhe(pedido.pedidoId)
  assert.equal(pago.pagamentos.length, 1)
  assert.equal(pago.pedido.totalPago, pedido.total)
  await status(pedido.pedidoId, 'entregue')
  assert.equal(await saldo(lasanha), inicial - 2)
  provou('Recebimento repetido tem um pagamento; entrega não baixa estoque novamente')

  const segundo = await criar(lasanha, 1)
  await requisicao(`pedidos/${segundo.pedidoId}/pagamentos`, { ...pagamento, pedidoId: segundo.pedidoId, valor: segundo.total }, 'POST', randomUUID())
  await requisicao(`pedidos/${segundo.pedidoId}/cancelar`, { empresaId: fixture.empresaId, id: segundo.pedidoId, motivo: 'Cancelamento da prova R4' })
  const cancelado = await detalhe(segundo.pedidoId)
  const estorno = { operacaoId: randomUUID(), pagamentoId: cancelado.pagamentos[0].id, valor: segundo.total,
    metodo: 'dinheiro', motivo: 'Devolução controlada da prova R4', referencia: null, valorJaDevolvido: true, empresaId: fixture.empresaId }
  const devolvido = await requisicao(`pedidos/${segundo.pedidoId}/estornos-manuais`, estorno)
  const repetido = await requisicao(`pedidos/${segundo.pedidoId}/estornos-manuais`, estorno)
  assert.deepEqual({ ...repetido, registradoEm: null }, { ...devolvido, registradoEm: null })
  assert.equal(Date.parse(repetido.registradoEm), Date.parse(devolvido.registradoEm), 'Mesmo instante na precisão de leitura do navegador')
  assert.equal(await saldo(lasanha), inicial - 2)
  assert.equal((await detalhe(pedido.pedidoId)).pedido.totalPago, pedido.total)
  provou('Cancelamento e devolução repetida preservam estoque, pedido entregue e recebimento anterior')

  const vinculo = await requisicao(`atendimento/conversas/${fixture.conversaId}/vinculo-cliente`)
  assert.equal(vinculo.podeCorrigir, false)
  assert.ok(vinculo.pedidos.some(p => p.id === pedido.pedidoId))
  assert.ok(vinculo.pedidos.some(p => p.id === segundo.pedidoId))
  await requisicao(`atendimento/conversas/${fixture.conversaId}/mensagens`, { texto: 'Prova R4: entrega conferida e devolução registrada.', confirmar: true })
  await requisicao(`atendimento/conversas/${fixture.conversaId}/encerrar`, {})
  provou('Histórico entregue/cancelado bloqueia correção de pessoa; comunicação controlada e encerramento concluídos')
  writeFileSync(resolve(pasta, 'jornada-api.json'), JSON.stringify({ escopo: fixture.provedores, resultados, pedidoId: pedido.pedidoId, canceladoId: segundo.pedidoId }, null, 2))
} catch (erro) {
  writeFileSync(resolve(pasta, 'jornada-api-falha.json'), JSON.stringify({ resultados, erro: erro.message }, null, 2))
  throw erro
}
