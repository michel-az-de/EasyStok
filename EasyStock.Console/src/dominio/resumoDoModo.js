// Banner "quem responde esta conversa" em UMA linha (#1442). Homologação de
// 07/10: "Pausado porque você assumiu... Aviso de esteira continua saindo...
// Você respondeu. Próximo passo sugerido: ..." funcionava, mas era prolixo.
// A regra continua em `modoDoAtendimento` e `proximoPassoDepoisDeResponder`
// (provadas em prova-f07 e prova-r12); aqui só muda o tamanho da frase. A
// frase inteira continua em `detalhe`, que a tela põe no `title`.

import { MODOS, PAUSA_POR_OUTRO, modoDoAtendimento, proximoPassoDepoisDeResponder } from './automatico'
import { origemDaPassagem } from './passagem'

const ROTULO = {
  [MODOS.LIGADO]: 'Automático',
  [MODOS.PAUSADO]: 'Com você',
  [MODOS.COM_VOCE]: 'Passou para você',
  [MODOS.ENCERRADO]: 'Encerrada',
  [MODOS.BLOQUEADO]: 'Bloqueado',
}

const LINHA = {
  [MODOS.LIGADO]: 'Responde sozinho até você assumir.',
  [MODOS.PAUSADO]: 'Automático pausado; avisos do pedido seguem.',
  [MODOS.ENCERRADO]: 'Reabra para o automático voltar.',
  [MODOS.BLOQUEADO]: 'Nada sai em canal nenhum. Desbloqueie na ficha.',
}

const LINHA_DO_PROXIMO = {
  'encerrar-ou-aguardar': 'Respondido: encerre se acabou, ou devolva ao automático.',
  aguardar: 'Respondido. Pedido em andamento: aguarde o cliente.',
}

export function resumoDoModo(conversa, pausado = false, bloqueada = false) {
  const modo = modoDoAtendimento(conversa, pausado, bloqueada)
  const proximo = !bloqueada && modo.chave === MODOS.PAUSADO
    ? proximoPassoDepoisDeResponder(conversa, pausado)
    : null
  const detalhe = [modo.rotulo + '.', modo.detalhe, proximo?.texto].filter(Boolean).join(' ')
  const outro = modo.chave === MODOS.PAUSADO && pausado === PAUSA_POR_OUTRO
  const motivo = conversa?.passagem?.motivo ?? null

  let linha = LINHA[modo.chave]
  if (modo.chave === MODOS.COM_VOCE) linha = origemDaPassagem(conversa.passagem).motivo
  else if (outro) linha = 'Automático pausado.'
  else if (proximo) linha = LINHA_DO_PROXIMO[proximo.chave] ?? linha
  else if (modo.chave === MODOS.PAUSADO && motivo) linha = motivo

  return {
    chave: modo.chave,
    tom: modo.tom,
    rotulo: outro ? 'Outro atendente' : ROTULO[modo.chave],
    linha,
    detalhe,
    proximo,
    sugereEncerrar: proximo?.chave === 'encerrar-ou-aguardar',
  }
}
