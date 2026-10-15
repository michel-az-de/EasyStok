// HTTP sem mock contra a fixture Kestrel/PostgreSQL; executar após a prova de navegador do P04.
import assert from 'node:assert/strict'
import { readFileSync, mkdirSync, writeFileSync } from 'node:fs'
const fixture = JSON.parse(readFileSync(process.env.LOGISTICA_FIXTURE ?? '../.build/logistica-browser.json', 'utf8'))
const pasta = process.env.EVIDENCIAS_UX ?? '../.build/1693-api-real'
mkdirSync(pasta, { recursive: true })
const evidencias = []
const base = '/api/atendimento/logistica'
async function http(caminho, metodo = 'GET', corpo, esperado = 200) {
  const response = await fetch(fixture.api + caminho, { method: metodo, headers: corpo ? { 'Content-Type': 'application/json' } : undefined, body: corpo ? JSON.stringify(corpo) : undefined })
  const json = await response.json()
  assert.equal(response.status, esperado, `${metodo} ${caminho}: ${response.status} ${JSON.stringify(json)}`)
  return esperado < 400 ? json.data : json
}
const contexto = () => http(`${base}/contexto?dia=${fixture.dia}`)
const saidas = () => http('/api/caixa/movimentos?pageSize=200&tipo=saida')
const chave = (tipo, id) => `prova-dez-${tipo}-${id}`
async function evento(d, tipo, motivo = null) {
  return http(`${base}/despachos/${d.id}/evento`, 'POST', { tipo, motivo, chaveIdempotencia: chave(tipo, d.id) })
}
async function contratar(p, provedor = 'lalamove', extras = {}) {
  const cotacoes = await http(`${base}/cotacoes`, 'POST', { pedidoIds: [p.id], agrupar: false, ...extras })
  const q = cotacoes.find(x => x.provedor === provedor && !x.indisponibilidade)
  assert.ok(q, `Cotação elegível de ${provedor} para ${p.cliente}`)
  const d = await http(`${base}/despachos`, 'POST', { cotacaoId: q.id, chaveIdempotencia: chave('contrato', p.id) })
  assert.deepEqual(d.pedidoIds, [p.id])
  return d
}
async function pagarUmaVez(d) {
  const antes = await saidas()
  const dados = { meio: 'pix', referencia: `pagamento-dez-${d.id}`, chaveIdempotencia: chave('pagamento', d.id) }
  const pagos = await Promise.all([http(`${base}/despachos/${d.id}/pagamento`, 'POST', dados), http(`${base}/despachos/${d.id}/pagamento`, 'POST', dados)])
  assert.ok(pagos.every(p => p.pago))
  const depois = await saidas()
  const novos = depois.filter(p => !antes.some(a => a.id === p.id))
  assert.equal(novos.length, 1, 'Pagamento simultâneo repetido gera somente uma saída.')
  assert.equal(Math.round(novos[0].valor * 100), d.valorCentavos)
  assert.equal(novos[0].referencia, dados.referencia)
  return novos[0].id
}
async function validarEntrega(d) {
  await evento(d, 'a_caminho')
  const final = await evento(d, 'entregue')
  assert.equal(final.status, 'entregue')
  const atual = await contexto()
  assert.ok(d.pedidoIds.every(id => atual.pedidos.find(p => p.id === id)?.status === 'entregue'))
}
const inicial = await contexto()
assert.equal(inicial.demonstracao, true)
assert.equal(inicial.pedidos.length, 10)
const pedido = (nome) => {
  const p = inicial.pedidos.find(x => x.cliente.endsWith(`· ${nome}`))
  assert.ok(p, `Pedido de ${nome}`)
  return p
}
try {
  const grupo = inicial.despachos.find(d => ['Ana', 'Bruno', 'Carla'].every(n => d.pedidoIds.includes(pedido(n).id)))
  assert.ok(grupo?.pago, 'A fixture inicia o grupo de três contratado e pago.')
  await validarEntrega(grupo)
  evidencias.push({ pedidos: ['Ana', 'Bruno', 'Carla'], cenario: 'Agrupados, contrato prévio pago, saída e entrega propagadas aos três pedidos', despachoId: grupo.id })
  const diego = pedido('Diego')
  assert.equal((await contexto()).pedidos.find(p => p.id === diego.id).status, 'entregue', 'Execute antes a prova do navegador para Diego.')
  evidencias.push({ pedido: 'Diego', cenario: 'Contratação, caixa e entrega pela prova de navegador anterior', pedidoId: diego.id })
  const erroEndereco = await http(`${base}/cotacoes`, 'POST', { pedidoIds: [pedido('Elisa').id], agrupar: false }, 400)
  assert.match(JSON.stringify(erroEndereco), /endere|coordenad/i)
  evidencias.push({ pedido: 'Elisa', cenario: 'Endereço/coordenadas ausentes bloqueiam cotação', erro: erroEndereco })
  const fabio = pedido('Fábio')
  const dFabio = await contratar(fabio)
  const erroPreparo = await http(`${base}/despachos/${dFabio.id}/evento`, 'POST', { tipo: 'a_caminho', motivo: null, chaveIdempotencia: chave('cedo', dFabio.id) }, 400)
  assert.match(JSON.stringify(erroPreparo), /pronto/i)
  await http(`/api/kds/pedidos/${fabio.id}/status`, 'PATCH', { status: 'pronto' })
  const caixaFabio = await pagarUmaVez(dFabio)
  await validarEntrega(dFabio)
  evidencias.push({ pedido: 'Fábio', cenario: 'Saída bloqueada em preparo; KDS pronto libera saída; pagamento repetido sem duplicar', despachoId: dFabio.id, movimentoId: caixaFabio })
  const gabi = await contratar(pedido('Gabi'), 'manual', { incluirManual: true, valorManualCentavos: 1800 })
  assert.equal(gabi.simulado, false)
  assert.equal(gabi.valorCentavos, 1800)
  const caixaGabi = await pagarUmaVez(gabi)
  await validarEntrega(gabi)
  evidencias.push({ pedido: 'Gabi', cenario: 'Entregador manual com custo informado de R$18 e saída única', despachoId: gabi.id, movimentoId: caixaGabi })
  const antesMaos = await saidas()
  const hugo = await contratar(pedido('Hugo'), 'em_maos', { incluirManual: true })
  assert.equal(hugo.valorCentavos, 0)
  await http(`${base}/despachos/${hugo.id}/pagamento`, 'POST', { meio: 'pix', referencia: 'nao-deve-pagar', chaveIdempotencia: chave('zero', hugo.id) }, 400)
  await validarEntrega(hugo)
  assert.equal((await saidas()).length, antesMaos.length)
  evidencias.push({ pedido: 'Hugo', cenario: 'Entrega em mãos a zero, pagamento recusado e caixa preservado', despachoId: hugo.id })
  const iris = await contratar(pedido('Iris'))
  const caixaIris = await pagarUmaVez(iris)
  await evento(iris, 'a_caminho')
  await evento(iris, 'falha', 'Cliente ausente no destino de teste')
  await evento(iris, 'devolvendo', 'Cliente solicitou retorno à loja')
  await evento(iris, 'devolvido')
  const retorno = await contexto()
  assert.equal(retorno.despachos.find(d => d.id === iris.id).status, 'devolvido')
  assert.equal(retorno.pedidos.find(p => p.id === pedido('Iris').id).status, 'pronto')
  evidencias.push({ pedido: 'Iris', cenario: 'Falha, retorno, devolvido e pedido pronto; frete pago permanece separado do reembolso', despachoId: iris.id, movimentoId: caixaIris })
  await http(`${base}/politica`, 'PUT', { automatico: false, tetoCentavos: 100000, permitirAgrupar: false })
  const automatico = { pedidoIds: [pedido('João').id], agrupar: false, chaveIdempotencia: chave('automatico', pedido('João').id) }
  const bloqueioAutomatico = await http(`${base}/automatico`, 'POST', automatico, 400)
  assert.match(JSON.stringify(bloqueioAutomatico), /desativad|configur/i)
  await http(`${base}/politica`, 'PUT', { automatico: true, tetoCentavos: 100000, permitirAgrupar: false })
  const joao = await http(`${base}/automatico`, 'POST', automatico)
  assert.ok(joao.valorCentavos <= 100000)
  const caixaJoao = await pagarUmaVez(joao)
  await validarEntrega(joao)
  evidencias.push({ pedido: 'João', cenario: 'Automático bloqueado quando desativado e autorizado dentro do teto; pagamento separado', despachoId: joao.id, movimentoId: caixaJoao })
  await http(`${base}/politica`, 'PUT', { automatico: false, tetoCentavos: 3000, permitirAgrupar: false })
  writeFileSync(`${pasta}/dez-cenarios.json`, JSON.stringify({ tipo: 'API/PostgreSQL reais; prestadores externos simulados; autenticação de teste', dia: fixture.dia, evidencias }, null, 2))
  console.log('OK: dez pedidos exercitados; grupo de três, navegador P04, endereço inválido, preparo/KDS, manual, mãos zero, retorno e automático; saídas de caixa idempotentes.')
} catch (erro) {
  writeFileSync(`${pasta}/dez-cenarios-parcial.json`, JSON.stringify({ evidencias, erro: erro.message }, null, 2))
  throw erro
}
