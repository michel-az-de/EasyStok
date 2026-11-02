// #2185: o que cada estado da conversa oferece. Uma função só para a barra, o menu ⋮, a Ficha e o
// balão do Assistente: cada um fazia a sua conta e a mesma conversa mostrava ações diferentes,
// inclusive "Retomar Assistente" com o Assistente já respondendo. A matriz está provada em
// ferramentas/prova-2185-acoes-da-conversa.mjs.
//
// Puro. `pausado` é o valor CRU de `automaticoPausado`: a marca separa "assumi" de "é de outra pessoa".

import { MODOS, PAUSA_POR_OUTRO, modoDoAtendimento } from './automatico'
import { conversaEncerrada } from './conversa'
import { automaticoDesligadoNaConversa } from './motivoDeEscalada'

// `Assumida` sem responsável na API é a fila humana: ninguém pegou, mesmo sem pendência a mostrar.
export const naFilaSemResponsavel = (conversa) =>
  conversa?.situacaoApi === 'Assumida' && !conversa.assumidaPorUsuarioId

const NENHUMA = Object.freeze({
  assumir: false, confirmarAoAssumir: false, assumirAntesDeEnviar: false, devolver: false, concluir: false,
  enviarCardapio: false, passar: false,
})

export function acoesDaConversa({ conversa, pausado = false, bloqueada = false, canal, permitePassar = false }) {
  if (!conversa) return NENHUMA
  const { chave } = modoDoAtendimento(conversa, pausado, bloqueada)
  // Bloqueio vence no modo; conversa concluída de cliente bloqueado continua concluída.
  const aberta = !conversaEncerrada(conversa)
  const livre = aberta && chave !== MODOS.BLOQUEADO
  const pausada = chave === MODOS.PAUSADO
  const deOutro = pausada && pausado === PAUSA_POR_OUTRO
  const semResponsavel = pausada && naFilaSemResponsavel(conversa)
  const parouNoAssistente = (pausada && !deOutro) || chave === MODOS.COM_VOCE
  return {
    assumir: chave === MODOS.LIGADO || chave === MODOS.COM_VOCE || deOutro || semResponsavel,
    // O botão Assumir na conversa de um colega pede confirmação: é de outra pessoa. Enviar por cima assume
    // sem perguntar (`assumirAntesDeEnviar`, abaixo).
    confirmarAoAssumir: deOutro,
    // #2185 (F12, E09): enviar por cima do Assistente (ou de um colega) é assumir antes, para texto, foto,
    // cardápio, Assistente e cobrança. Com o automático desligado na loja ninguém responde: nada a assumir.
    assumirAntesDeEnviar: (chave === MODOS.LIGADO && !automaticoDesligadoNaConversa(conversa)) || deOutro,
    // Só o WhatsApp tem Assistente (o servidor recusa nos outros canais) e, de outra pessoa, a
    // conversa só volta ao Assistente depois de assumida: não se devolve o que é de um colega.
    devolver: parouNoAssistente && Boolean(canal?.temAssistente) && !automaticoDesligadoNaConversa(conversa),
    // Cadastro bloqueado também se conclui: o que trava é o envio, não o fim do atendimento.
    concluir: aberta,
    enviarCardapio: livre,
    passar: livre && permitePassar,
  }
}
