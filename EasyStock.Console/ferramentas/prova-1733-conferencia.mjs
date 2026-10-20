import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      try { return proximo(spec + '.js', contexto) } catch { /* resolução normal */ }
    }
    return proximo(spec, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format:'module', shortCircuit:true, source:"export const FONTE_API = true; export const API_BASE = ''" }
    return proximo(url, contexto)
  },
})
globalThis.sessionStorage = { getItem: () => JSON.stringify({token:'teste',expiraEm:Date.now()+60000,empresa:{id:'empresa'}}) }
const { criarAcoesComandaApi } = await import('../src/aplicacao/api/comanda.js')
const pedidoId='11111111-1111-1111-1111-111111111111'
const pedido = {pedidoId,estado:'aguardando',statusApi:'aguardando_pagamento',itens:[],totalApi:100,meio:'pix',cobranca:{id:'cb',link:'https://example.test/pagar',valor:100}}
const dto = {pedidoId,status:'aguardando',total:100,totalPago:100,itens:[],pagamentos:[],cobranca:{cobrancaId:'cb',status:'Paga',valor:100,linkPagamento:'https://example.test/pagar',tentativa:1}}
const ok = data => new Response(JSON.stringify({data}))
function montar(responder, atual=pedido) {
  const chamadas=[], avisos=[], atualizacoes=[]
  globalThis.fetch=async(url, opcoes={})=> {
    chamadas.push({url:String(url),metodo:opcoes.method??'GET',corpo:opcoes.body??null})
    return responder(String(url),opcoes)
  }
  const api=criarAcoesComandaApi({}, {estadoRef:{current:{conversas:[{id:'conversa',pedido:atual}]}},despachar:acao=>{
    if(acao.mensagem) avisos.push(acao.mensagem)
    if(acao.pedido) atualizacoes.push(acao.pedido)
  }})
  return {api,chamadas,avisos,atualizacoes}
}
let testes=0
for (const situacao of ['em_processamento','nao_encontrado','aguardando_credencial','sem_cobranca_online','nao_conciliado']) {
  const t=montar(()=>ok({situacao,confirmado:false}))
  assert.deepEqual(await t.api.conferirPagamento('conversa'),{situacao,confirmado:false})
  assert.deepEqual(t.chamadas,[{url:`/api/pedidos/${pedidoId}/cobranca/conferir`,metodo:'POST',corpo:null}])
  assert.equal(t.atualizacoes.length,0); testes++
}
for (const situacao of ['confirmado','ja_conferido']) {
  const t=montar(url=>url.endsWith('/conferir')?ok({situacao,confirmado:true}):ok(dto))
  assert.equal((await t.api.conferirPagamento('conversa')).confirmado,true)
  assert.equal(t.chamadas.length,2); assert.equal(t.atualizacoes.length,1)
  assert.equal(t.atualizacoes[0].totalPagoApi,100); testes++
}
{
  let liberar
  let avisarPost
  const iniciou=new Promise(resolve=>{avisarPost=resolve})
  const t=montar(()=>new Promise(resolve=>{liberar=()=>resolve(ok({situacao:'em_processamento',confirmado:false}));avisarPost()}))
  const a=t.api.conferirPagamento('conversa'), b=t.api.conferirPagamento('conversa')
  assert.equal(a,b); await iniciou; liberar(); await a; assert.equal(t.chamadas.length,1); testes++
}
{
  const t=montar(()=>{throw new TypeError('Failed to fetch')})
  assert.match((await t.api.conferirPagamento('conversa')).erro,/Não foi possível conferir/)
  assert.equal(t.atualizacoes.length,0); testes++
}
{
  const t=montar(url=>url.endsWith('/conferir')?ok({situacao:'confirmado',confirmado:true}):new Response('{}',{status:503}))
  assert.equal((await t.api.conferirPagamento('conversa')).confirmado,true)
  assert.match(t.avisos.at(-1),/Não foi possível atualizar a tela/); testes++
}
{
  const t=montar(()=>{throw new Error('não deve chamar')},{itens:[]})
  assert.ok((await t.api.conferirPagamento('conversa')).erro); assert.equal(t.chamadas.length,0); testes++
}
{
  const t=montar(url=>url.endsWith('/forma')?new Response(JSON.stringify({error:{code:'pedido_ja_pago',message:'Pagamento online conferido. Atualize o pedido antes de receber outro valor.'}}),{status:409}):ok(dto))
  assert.match((await t.api.confirmarPagamento('conversa',100,'pix')).erro,/Pagamento online conferido/)
  assert.equal(t.chamadas.some(x=>x.url.endsWith('/pagamentos')),false); testes++
}
{
  const t=montar(()=>{throw new TypeError('Failed to fetch')},{...pedido,cobranca:{id:'cb',valor:100,link:null},meio:'maquininha',statusApi:'aguardando'})
  const resultado=await t.api.confirmarPagamento('conversa',10,'dinheiro')
  assert.match(resultado.erro,/Não foi possível confirmar o registro/)
  assert.doesNotMatch(resultado.erro,/Pagamento não registrado/); testes++
}
{
  const t=montar((_url,opcoes)=>{if(opcoes.method==='POST')throw new TypeError('Failed to fetch');return ok(dto)},
    {estado:'montagem',itens:[{sku:'massa',qtd:1,obs:''}],janela:'j1|2026-10-13',meio:'pix'})
  const resultado=await t.api.gerarPedido('conversa')
  assert.equal(resultado.pedidoId,pedidoId)
  assert.equal(t.chamadas.filter(x=>x.metodo==='POST').length,1)
  assert.doesNotMatch(t.avisos.join(' '),/Pedido não criado/); testes++
}
console.log(`OK: ${testes} cenários de conferência, reentrada, recarga e resposta perdida; HTTP controlado.`)
