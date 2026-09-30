/* eslint-disable no-console */
// Prova da clareza das telas (rodada 12, issue #16, áudio da Thatiane de
// 26/09/2026). Importa as MESMAS funções que a tela usa e confere o texto que
// responde cada dúvida dela:
//
//   - origem do "Passou para você" (quem passou, por quê, a que horas),
//     igual no cartão do Balcão, na barra da conversa e no lembrete;
//   - próximo passo depois que ela responde (encerrar x aguardar o cliente);
//   - resumo do dia dos entregues, que NÃO lança no caixa.
//
//   node ferramentas/prova-r12-clareza.mjs
//
// Mesmo gancho de resolução de `prova-contadores-canal.mjs`.

process.env.TZ = 'America/Sao_Paulo'

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    try {
      return proximo(especificador, contexto)
    } catch (erro) {
      if (especificador.startsWith('.') && !/\.m?jsx?$/.test(especificador)) {
        return proximo(especificador + '.js', contexto)
      }
      throw erro
    }
  },
})

const { origemDaPassagem } = await import('../src/dominio/passagem.js')
const {
  MODOS, PAUSA_POR_ASSUMIR, modoDoAtendimento, proximoPassoDepoisDeResponder,
} = await import('../src/dominio/automatico.js')
const { lembretesAutomaticos } = await import('../src/dominio/lembrete.js')
const { resumoDoDia, textoDoResumoDoDia } = await import('../src/dominio/resumoDoDia.js')
const { CARDAPIO, INSTANTE_INICIAL } = await import('../src/infra/catalogo.js')

const AGORA = new Date(INSTANTE_INICIAL).getTime()
const iso = (minutosAntes) => new Date(AGORA - minutosAntes * 60000).toISOString()

let passos = 0
const ok = (texto) => { passos += 1; console.log(`ok ${passos} - ${texto}`) }

// --- 1. Origem da passagem ---------------------------------------------------
const passagem = { motivo: 'Restrição alimentar, o automático não responde isso', em: iso(0), assumida: false }
const origem = origemDaPassagem(passagem)
assert.equal(origem.hora, '11:05')
assert.match(origem.longo, /^O atendimento automático não soube responder e passou para você às 11:05\./)
assert.match(origem.longo, /Motivo: restrição alimentar, o automático não responde isso\.$/)
assert.equal(origem.curto, 'Automático não soube responder (11:05): restrição alimentar, o automático não responde isso')
ok('origem da passagem diz quem passou, a hora e o motivo')

assert.equal(origemDaPassagem(null), null)
const semHora = origemDaPassagem({ motivo: null, assumida: false })
assert.equal(semHora.hora, null)
assert.match(semHora.curto, /^Automático não soube responder: o agente preferiu não responder sozinho$/)
ok('passagem antiga, sem hora nem motivo, cai no texto padrão sem inventar hora')

// --- 2. A barra da conversa conta a mesma origem ------------------------------
const beatriz = {
  id: 'beatriz', nome: 'Beatriz Nogueira', estado: 'Aberto', pedido: null, passagem,
  mensagens: [{ id: 'm1', dir: 'in', texto: 'Tem glúten no raviólí?', em: iso(0) }],
}
const modo = modoDoAtendimento(beatriz, false, false)
assert.equal(modo.chave, MODOS.COM_VOCE)
assert.equal(modo.detalhe, origem.longo)
ok('barra "Passou para você" mostra a mesma origem do cartão')

// --- 3. O lembrete também conta a origem --------------------------------------
const lembrete = lembretesAutomaticos([beatriz], AGORA).find((l) => l.origem === 'passagem')
assert.equal(lembrete.detalhe, origem.curto)
ok('lembrete "Responder Beatriz" diz que veio do automático, com hora e motivo')

// --- 4. Automático ligado se explica -----------------------------------------
const ligado = modoDoAtendimento({ ...beatriz, passagem: null }, false, false)
assert.equal(ligado.chave, MODOS.LIGADO)
assert.match(ligado.detalhe, /atendimento automático responde/)
ok('automático ligado explica quem responde e como assumir')

// --- 5. Próximo passo depois de ela responder ---------------------------------
const respondeu = (extra) => ({
  ...beatriz,
  passagem: { ...passagem, assumida: true },
  mensagens: [
    ...beatriz.mensagens,
    { id: 'm2', dir: 'out', texto: 'Oi Beatriz! Temos massa sem glúten sim.', em: iso(-1) },
    { id: 'm3', dir: 'sistema', texto: 'Nota interna.', em: iso(-2) },
  ],
  ...extra,
})
assert.equal(proximoPassoDepoisDeResponder(beatriz, PAUSA_POR_ASSUMIR), null, 'cliente falou por último: nada a sugerir')
assert.equal(proximoPassoDepoisDeResponder(respondeu(), false), null, 'automático ligado: não é ela quem conduz')
const semPedido = proximoPassoDepoisDeResponder(respondeu(), PAUSA_POR_ASSUMIR)
assert.equal(semPedido.chave, 'encerrar-ou-aguardar')
assert.match(semPedido.texto, /Você respondeu\./)
assert.match(semPedido.texto, /encerre/)
assert.match(semPedido.texto, /devolva ao automático/)
ok('sem pedido em andamento: sugere encerrar se acabou, ou devolver e aguardar o cliente')

const comPedido = proximoPassoDepoisDeResponder(respondeu({ pedido: { estado: 'preparo', itens: [] } }), true)
assert.equal(comPedido.chave, 'aguardar')
assert.match(comPedido.texto, /aguardar o cliente/)
assert.match(comPedido.texto, /não encerre/)
assert.doesNotMatch(comPedido.texto, /acabou, encerre/)
ok('pedido em andamento: sugere aguardar o cliente, nunca encerrar')

const entregue = proximoPassoDepoisDeResponder(respondeu({ pedido: { estado: 'entregue', itens: [] } }), true)
assert.equal(entregue.chave, 'encerrar-ou-aguardar')
ok('pedido entregue volta a sugerir encerrar')

// --- 6. Resumo do dia dos entregues -------------------------------------------
const pago = (valor, meio, pagaEm = iso(30)) => ({ id: `cob-${valor}-${meio}`, valor, valorPago: valor, meio, pagaEm })
const conversas = [
  { id: 'a', nome: 'Ana Souza', pedido: { numero: '2026-0181', estado: 'entregue', itens: [{ sku: 'LAS-CLA', qtd: 1 }], cobranca: pago(85, 'pix') } },
  { id: 'b', nome: 'Bruno Lima', pedido: { numero: '2026-0182', estado: 'entregue', itens: [{ sku: 'LAS-CLA', qtd: 2 }], cobranca: pago(170, 'maquininha') } },
  // Entregue sem pagamento registrado (acerto na porta): entra no total e no "falta receber".
  { id: 'c', nome: 'Carla Dias', pedido: { numero: '2026-0183', estado: 'entregue', itens: [{ sku: 'LAS-CLA', qtd: 1 }], cobranca: null } },
  { id: 'd', nome: 'Davi Reis', pedido: { numero: '2026-0184', estado: 'preparo', itens: [{ sku: 'LAS-CLA', qtd: 1 }], cobranca: pago(85, 'pix') } },
  { id: 'e', nome: 'Eva Melo', pedido: { numero: '2026-0185', estado: 'cancelado', itens: [{ sku: 'LAS-CLA', qtd: 1 }], cobranca: null } },
  { id: 'f', nome: 'Lead sem pedido', pedido: null },
]
const precoLasanha = CARDAPIO.find((i) => i.sku === 'LAS-CLA').preco
const resumo = resumoDoDia(conversas, CARDAPIO)
assert.equal(resumo.quantidade, 3)
assert.equal(resumo.totalPedidos, precoLasanha * 4)
assert.equal(resumo.recebido, 255)
assert.equal(resumo.aReceber, Math.max(precoLasanha * 4 - 255, 0))
assert.deepEqual(resumo.porMeio.map((m) => [m.nome, m.valor]), [['Pix', 85], ['maquininha', 170]])
ok('resumo soma só os entregues, separa por meio e mostra o que falta receber')

const texto = textoDoResumoDoDia(resumo, AGORA)
assert.match(texto, /^Casa da Baba · resumo do dia 22\/09\/2026/)
assert.match(texto, /Entregues: 3 pedidos/)
assert.match(texto, /0181 Ana Souza/)
assert.doesNotMatch(texto, /Davi|Eva|Lead/)
assert.match(texto, /não lança nada no caixa/)
ok('texto copiado lista os entregues e avisa que não lança no caixa')

const vazio = resumoDoDia([], CARDAPIO)
assert.equal(vazio.quantidade, 0)
assert.equal(vazio.totalPedidos, 0)
ok('dia sem entregue dá zero, sem quebrar')

console.log(`\n${passos} passos verdes`)
