import './prova-1742-origem-chat.mjs'
import assert from 'node:assert/strict'
const { mensagemDaApi, conversaDaApi, situacaoConversaDaApi } = await import('../src/infra/api/traducaoConversas.js')
const { origemDaPassagem } = await import('../src/dominio/passagem.js')
const { modoDoAtendimento } = await import('../src/dominio/automatico.js')
const { mensagensSemRespostaReal } = await import('../src/dominio/mensagem.js')
const { previaDaConversa } = await import('../src/dominio/conversa.js')
const entrada = {id:'entrada',direcao:'Entrada',autor:'Cliente',texto:'Preciso de ajuda',enviadaEm:'2026-10-13T12:00:00Z'}
for (const marco of ['pedido:pedido-real', 'frete:link-real:1']) {
  const aviso = {id:marco,direcao:'Saida',autor:'Sistema',status:'Enviada',externoId:'chatsite:comanda:'+marco,texto:'Aviso do pedido ou frete',enviadaEm:'2026-10-13T12:01:00Z'}
  assert.equal(mensagemDaApi(aviso).dir,'sistema')
  const conversa = conversaDaApi({id:'c',canal:'ChatSite',situacao:'Assumida',aguardaResposta:true},[entrada,aviso],{})
  assert.equal(mensagensSemRespostaReal(conversa).length,1)
  assert.equal(previaDaConversa(conversa).corpo,entrada.texto)
  assert.equal(mensagemDaApi({...aviso,autor:'Dona'}).dir,'out')
  assert.equal(mensagemDaApi({...aviso,autor:'Agente'}).dir,'out')
  assert.equal(mensagemDaApi({...aviso,externoId:'wamid.aviso'}).dir,'out')
}
for (const motivo of ['Entrega e frete a combinar. Confira o endereço e a seleção da comanda.', 'Motivo real ainda não catalogado']) {
  const base = {canal:'ChatSite',situacao:'Assumida',assumidaPorUsuarioId:null,motivoEscalada:motivo}
  const passagem = situacaoConversaDaApi(base,{}).passagem
  assert.equal(passagem.origem,'chat-site'); assert.equal(passagem.motivo,motivo)
  assert.equal(origemDaPassagem(passagem).longo,motivo)
  assert.equal(modoDoAtendimento({passagem}).rotulo,'Aguardando equipe')
  assert.equal(situacaoConversaDaApi({...base,assumidaPorUsuarioId:'humano'},{}).passagem,null)
  assert.equal(situacaoConversaDaApi({...base,motivoEscalada:'Conversa reaberta para atendimento.'},{}).passagem.origem,'reabertura')
  assert.notEqual(situacaoConversaDaApi({...base,canal:'WhatsApp'},{}).passagem.origem,'chat-site')
}
console.log('OK: pedido/frete Sistema preservam espera e prévia; resposta humana/agente e aviso WhatsApp mantêm tradução existente.')
