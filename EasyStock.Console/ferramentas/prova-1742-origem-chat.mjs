import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'
registerHooks({
  resolve(e, c, p) { if (e.startsWith('.') && !/\.[a-z]+$/i.test(e)) { try { return p(e + '.js', c) } catch { /* resolução padrão */ } } return p(e, c) },
  load(u, c, p) {
    if (u.endsWith('/infra/fonteDados.js')) return { format:'module', shortCircuit:true, source:"export const FONTE_API=true; export const API_BASE='';" }
    if (u.endsWith('.json')) return { format:'module', shortCircuit:true, source:'export default '+readFileSync(new URL(u),'utf8') }
    return p(u,c)
  },
})
const { situacaoConversaDaApi, mensagemDaApi, conversaDaApi } = await import('../src/infra/api/traducaoConversas.js')
const { origemDaPassagem } = await import('../src/dominio/passagem.js')
const { mensagensSemRespostaReal } = await import('../src/dominio/mensagem.js')
const { previaDaConversa } = await import('../src/dominio/conversa.js')
const { modoDoAtendimento } = await import('../src/dominio/automatico.js')
const motivo = 'Visitante do site aguardando atendimento da equipe.'
const base = { id:'c', canal:'ChatSite', situacao:'Assumida', assumidaPorUsuarioId:null, contatoNome:'Visitante', motivoEscalada:motivo }
for (const motivoEscalada of [motivo,null]) {
  const passagem = situacaoConversaDaApi({...base,motivoEscalada},{}).passagem
  assert.equal(passagem.origem,'chat-site'); assert.equal(origemDaPassagem(passagem).longo,motivo)
  assert.equal(modoDoAtendimento({passagem}).rotulo,'Aguardando equipe')
}
assert.equal(situacaoConversaDaApi({...base,assumidaPorUsuarioId:'outro'},{}).passagem,null)
assert.equal(situacaoConversaDaApi({...base,situacao:'Encerrada'},{}).passagem,null)
const reabertura = situacaoConversaDaApi({...base,motivoEscalada:'Conversa reaberta para atendimento.'},{}).passagem
assert.equal(reabertura.origem,'reabertura'); assert.equal(origemDaPassagem(reabertura).curto,reabertura.motivo)
const real = situacaoConversaDaApi({...base,canal:'WhatsApp',motivoEscalada:'Restrição alimentar'},{}).passagem
assert.match(origemDaPassagem(real).curto,/Automático não soube/)
const nota = {id:'ack',direcao:'Saida',autor:'Sistema',status:'Enviada',externoId:'chatsite:recepcao:c',texto:'Recebemos sua mensagem.',enviadaEm:'2026-10-13T12:01:00Z'}
assert.equal(mensagemDaApi(nota).dir,'sistema')
const conversa = conversaDaApi(base,[{id:'entrada',direcao:'Entrada',autor:'Cliente',texto:'Dúvida',enviadaEm:'2026-10-13T12:00:00Z'},nota],{})
assert.equal(mensagensSemRespostaReal(conversa).length,1)
assert.equal(previaDaConversa(conversa).corpo,'Dúvida')
assert.equal(mensagemDaApi({...nota,autor:'Dona'}).dir,'out')
assert.equal(mensagemDaApi({...nota,externoId:'chatsite:resposta-normal'}).dir,'out')
console.log('OK: 11 provas de origem humana nova/legada, reabertura, dona, escalada real e confirmação sem responder à dúvida.')
