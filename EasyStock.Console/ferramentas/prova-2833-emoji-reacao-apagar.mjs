/* eslint-disable no-console */
// Prova da #2833: emoji no Composer, reação e apagar para todos no atendimento. As regras são puras
// (dominio/acoesNaMensagem); a tela só desenha. A prova cobre:
//   1. a busca do emoji por nome, com e sem os dois-pontos e sem acento;
//   2. o emoji entra no cursor (ou troca a seleção) e o cursor fica logo depois dele;
//   3. "Apagar para todos" só na mensagem da loja que já está no WhatsApp, até 48 h, pela conexão por QR;
//      na Meta o item some e uma linha explica por quê;
//   4. a resposta da API troca a mensagem na conversa, inclusive quando a reação sai;
//   5. o Composer tem o botão de emoji e a bolha tem o menu com as reações, copiar e apagar.
//
//   node ferramentas/prova-2833-emoji-reacao-apagar.mjs

import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const regras = await import('../src/dominio/acoesNaMensagem.js')
const acao = await import('../src/aplicacao/acoes.js')
const { casosApi } = await import('../src/aplicacao/casos/api.js')

// 1. Busca.
assert.equal(regras.EMOJIS_DA_LOJA.length, 24, 'os 24 mais usados na loja')
assert.equal(regras.REACOES_RAPIDAS.length, 6, 'seis reações rápidas')
assert.deepEqual(regras.buscarEmojis(':mas').map((e) => e.emoji), ['🍝'], ':nome busca como no WhatsApp Web')
assert.deepEqual(regras.buscarEmojis('coracao').map((e) => e.emoji), ['❤️'], 'busca sem acento')
assert.deepEqual(regras.buscarEmojis('CORAÇÃO').map((e) => e.emoji), ['❤️'], 'busca sem caixa')
assert.equal(regras.buscarEmojis('').length, 24, 'sem termo, a lista inteira')
assert.equal(regras.buscarEmojis(':').length, 24, 'só os dois-pontos, a lista inteira')
assert.deepEqual(regras.buscarEmojis('xyz'), [], 'nada casa, nada volta')

// 2. Cursor.
assert.deepEqual(regras.inserirNoCursor('Oi tudo bem', 2, 2, '😊'), { texto: 'Oi😊 tudo bem', cursor: 4 },
  'entra no cursor; o emoji conta 2 unidades e o cursor fica depois dele')
assert.deepEqual(regras.inserirNoCursor('Oi Maria', 3, 8, '👋'), { texto: 'Oi 👋', cursor: 5 }, 'troca a seleção')
assert.deepEqual(regras.inserirNoCursor('Oi', undefined, undefined, '🙏'), { texto: 'Oi🙏', cursor: 4 }, 'sem cursor, no fim')
assert.deepEqual(regras.inserirNoCursor('', 0, 0, '❤️'), { texto: '❤️', cursor: 2 }, 'campo vazio')
assert.deepEqual(regras.inserirNoCursor('ab', 9, 9, '✅'), { texto: 'ab✅', cursor: 3 }, 'cursor além do fim cai no fim')

// 3. Apagar para todos.
const agora = new Date('2026-11-18T15:00:00Z')
const daLoja = { id: 'm1', dir: 'out', status: 'enviada', noCanal: true, pelaPonte: true, em: '2026-11-18T14:58:00Z', texto: 'Mando o Pix?' }
const pelaMeta = { ...daLoja, pelaPonte: undefined }

assert.deepEqual(regras.apagarParaTodos(daLoja, agora), { mostra: true }, 'da loja, no WhatsApp, pela ponte')
const naMeta = regras.apagarParaTodos(pelaMeta, agora)
assert.equal(naMeta.mostra, false, 'na Meta o item não aparece')
assert.match(naMeta.motivo, /oficial \(Meta\)/, 'e a linha explica por quê')
assert.equal(regras.apagarParaTodos({ ...daLoja, dir: 'in' }, agora).mostra, false, 'mensagem do cliente não se apaga')
assert.equal(regras.apagarParaTodos({ ...pelaMeta, dir: 'in' }, agora).motivo, undefined, 'nem explica na do cliente')
assert.equal(regras.apagarParaTodos({ ...daLoja, noCanal: false }, agora).mostra, false, 'sem id no WhatsApp, nada a apagar')
assert.equal(regras.apagarParaTodos({ ...daLoja, apagadaEm: '2026-11-18T14:59:00Z' }, agora).mostra, false, 'já apagada')
assert.equal(regras.apagarParaTodos({ ...daLoja, em: '2026-11-16T14:59:00Z' }, agora).mostra, false, 'passou de 48 h')
assert.equal(regras.apagarParaTodos({ ...daLoja, em: '2026-11-16T15:01:00Z' }, agora).mostra, true, 'dentro de 48 h')

// O id externo diz por onde a mensagem saiu.
const { mensagemDaApi } = await import('../src/infra/api/traducaoConversas.js')
const daApi = (externoId) => mensagemDaApi({ id: 'x', direcao: 'Saida', autor: 'Dona', status: 'Enviada', texto: 'Oi', enviadaEm: '2026-11-18T14:58:00Z', externoId })
assert.equal(daApi('wpp:loja:true_5511999998888@c.us_3EB0ABC').pelaPonte, true, 'id da ponte')
assert.equal(daApi('wpp:loja:true_123@lid_3EB0ABC').pelaPonte, true, 'id da ponte com @lid')
assert.equal(daApi('wamid.HBgNNTUxMTk5OTk5ODg4OBUCABEYEjQ').pelaPonte, undefined, 'wamid é da Meta')
assert.equal(daApi('wpp:loja:menu:5511999998888').pelaPonte, undefined, 'menu numerado não é mensagem do WhatsApp')
assert.equal(daApi('wamid.X').noCanal, true)
assert.equal(daApi(null).noCanal, undefined)
const reagida = mensagemDaApi({ id: 'y', direcao: 'Entrada', autor: 'Cliente', status: 'Recebida', texto: 'Oi', enviadaEm: '2026-11-18T14:58:00Z', reacao: '👍', apagadaEm: '2026-11-18T15:00:00' })
assert.equal(reagida.reacao, '👍')
assert.equal(reagida.apagadaEm, '2026-11-18T15:00:00Z', 'a hora sem fuso vem em UTC')
assert.equal(regras.podeReagir(daLoja), true, 'reage na mensagem que está no WhatsApp')
assert.equal(regras.podeReagir({ ...daLoja, dir: 'in' }), true, 'inclusive na do cliente')
assert.equal(regras.podeReagir({ ...daLoja, noCanal: false }), false, 'sem id no WhatsApp, não reage')
assert.equal(regras.podeReagir({ ...daLoja, apagadaEm: '2026-11-18T14:59:00Z' }), false, 'apagada não recebe reação')
assert.equal(regras.podeReagir({ id: 's', dir: 'sistema', texto: 'nota' }), false, 'nota do sistema não reage')
assert.equal(regras.fraseDaApagada({ ...daLoja, apagadaEm: '2026-11-18T14:59:00Z' }, (iso) => iso.slice(11, 16)),
  'Você apagou esta mensagem para todos · 14:59')

// 4. Reducer.
const estado = {
  conversas: [{ id: 'c1', mensagens: [{ ...daLoja, reacao: '👍' }, { id: 'm2', dir: 'in', em: '2026-11-18T14:57:00Z', texto: 'Oi' }] }],
}
const comCoracao = casosApi[acao.ATUALIZAR_MENSAGEM_API](estado, { id: 'c1', mensagem: { ...daLoja, reacao: '❤️' } })
assert.equal(comCoracao.conversas[0].mensagens[0].reacao, '❤️', 'a reação nova troca a antiga')
assert.equal(comCoracao.conversas[0].mensagens[1], estado.conversas[0].mensagens[1], 'a outra mensagem fica como estava')
const semReacao = casosApi[acao.ATUALIZAR_MENSAGEM_API](estado, { id: 'c1', mensagem: { ...daLoja } })
assert.equal('reacao' in semReacao.conversas[0].mensagens[0], false, 'a reação retirada some da bolha')
const apagada = casosApi[acao.ATUALIZAR_MENSAGEM_API](estado, { id: 'c1', mensagem: { ...daLoja, apagadaEm: '2026-11-18T14:59:00Z' } })
assert.equal(apagada.conversas[0].mensagens[0].apagadaEm, '2026-11-18T14:59:00Z')

// 5. Tela.
const composer = readFileSync(new URL('../src/features/atendimento/Composer.jsx', import.meta.url), 'utf8')
const balao = readFileSync(new URL('../src/features/atendimento/Balao.jsx', import.meta.url), 'utf8')
const painel = readFileSync(new URL('../src/features/atendimento/PainelAtendimento.jsx', import.meta.url), 'utf8')
assert.match(composer, /SeletorDeEmoji/, 'o Composer abre o seletor de emoji')
assert.match(composer, /inserirNoCursor/, 'e insere no cursor')
assert.match(balao, /MenuDaMensagem/, 'a bolha tem o menu da mensagem')
assert.match(balao, /fraseDaApagada/, 'e mostra a mensagem apagada')
assert.match(painel, /aoReagir=/, 'o painel liga a reação')
assert.match(painel, /aoApagar=/, 'e o apagar')

// 6. Grupo não sai pela ponte para reagir ou apagar: o WppConnectClient só lê o id de @c.us e @lid.
assert.equal(daApi('wpp:loja:true_120363000000000000@g.us_3EB0ABC').pelaPonte, undefined, 'grupo não é da ponte')

// 7. Reagir e apagar não mexem em `ultimaMensagemEm`: a volta de 5 s devolve a lista guardada no laço, e a mescla
// dá a vez ao servidor. A resposta da API entra no lugar da mensagem guardada, sem mexer na `ultima`.
const { criarMemoriaDoLaco, guardarMensagemNaMemoria } = await import('../src/aplicacao/cicloDeSincronizacao.js')
const memoria = criarMemoriaDoLaco()
memoria.mensagens.set('c1', { ultima: '2026-11-18T14:58:00Z', mensagens: [{ id: 'a', texto: 'Oi' }, { id: 'b', texto: 'Pix?' }] })
guardarMensagemNaMemoria(memoria, 'c1', { id: 'b', texto: 'Pix?', reacao: '❤️' })
assert.equal(memoria.mensagens.get('c1').mensagens[1].reacao, '❤️', 'a reação fica na lista do laço')
assert.equal(memoria.mensagens.get('c1').mensagens[0].texto, 'Oi', 'as outras ficam como estavam')
assert.equal(memoria.mensagens.get('c1').ultima, '2026-11-18T14:58:00Z', 'a última, que decide a releitura, não muda')
guardarMensagemNaMemoria(memoria, 'outra', { id: 'b' })
assert.equal(memoria.mensagens.has('outra'), false, 'conversa que o laço não guardou fica de fora')
guardarMensagemNaMemoria(null, 'c1', { id: 'b' })
const provider = readFileSync(new URL('../src/aplicacao/AtendimentoProvider.jsx', import.meta.url), 'utf8')
assert.match(provider, /comApi\(acoes, \{[^}]*memoria: memoriaDoLaco/, 'as ações da API recebem a memória do laço')

console.log('prova-2833: emoji, reação e apagar para todos ok')
