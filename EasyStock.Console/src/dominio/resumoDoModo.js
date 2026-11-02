// Banner "quem responde esta conversa" em UMA linha (#1442). Homologação de
// 07/10: "Pausado porque você assumiu... Aviso de esteira continua saindo...
// Você respondeu. Próximo passo sugerido: ..." funcionava, mas era prolixo.
// A regra continua em `modoDoAtendimento` e `proximoPassoDepoisDeResponder`
// (provadas em prova-f07 e prova-r12); aqui só muda o tamanho da frase. A
// frase inteira continua em `detalhe`, que a tela põe no `title`.

import { MODOS, PAUSA_POR_OUTRO, modoDoAtendimento, proximoPassoDepoisDeResponder } from './automatico'
import { AGUARDANDO_EQUIPE, origemDaPassagem } from './passagem'
import { naFilaSemResponsavel } from './acoesDaConversa'
import { automaticoDesligadoNaConversa } from './motivoDeEscalada'

const ROTULO = {
  [MODOS.LIGADO]: 'Assistente respondendo',
  [MODOS.PAUSADO]: 'Você respondendo',
  [MODOS.COM_VOCE]: 'Passou para você',
  [MODOS.ENCERRADO]: 'Concluída',
  [MODOS.BLOQUEADO]: 'Bloqueado',
}

const LINHA = {
  [MODOS.LIGADO]: 'Responde sozinho até você assumir.',
  [MODOS.PAUSADO]: 'Assistente pausado; os avisos do pedido continuam.',
  [MODOS.ENCERRADO]: 'Reabra a conversa para retomar o atendimento.',
  [MODOS.BLOQUEADO]: 'Envios bloqueados. A gerência desbloqueia na ficha.',
}

const LINHA_DO_PROXIMO = {
  'encerrar-ou-aguardar': 'Conclua a conversa ou devolva ao Assistente.',
  aguardar: 'Respondido. Pedido em andamento: aguarde o cliente.',
  'aguardar-venda': 'Respondido. Venda em andamento: monte o pedido ou aguarde o cliente.',
}

// #2185: "Aguardando equipe" vale para a barra e o cartão (`modo.rotulo`), e quem é o outro atendente
// aparece pelo nome. Sem responsável, a conversa nunca diz "Você respondendo".
function rotuloDoModo(modo, conversa, outro) {
  if (outro) return conversa?.responsavel && conversa.responsavel !== 'Outro atendente' ? `Com ${conversa.responsavel}` : 'Outro atendente'
  if (modo.chave === MODOS.COM_VOCE) return modo.rotulo === AGUARDANDO_EQUIPE ? AGUARDANDO_EQUIPE : ROTULO[modo.chave]
  if (modo.chave === MODOS.PAUSADO && naFilaSemResponsavel(conversa)) return AGUARDANDO_EQUIPE
  return ROTULO[modo.chave]
}

// #2185 (E22): quem não pode reabrir não recebe a ordem "reabra"; o botão nem existe para ela.
const LINHA_ENCERRADA_SEM_PERMISSAO = 'Peça a quem pode reabrir a conversa.'

export function resumoDoModo(conversa, pausado = false, bloqueada = false, { podeReabrir = true } = {}) {
  const modo = modoDoAtendimento(conversa, pausado, bloqueada)
  const outro = modo.chave === MODOS.PAUSADO && pausado === PAUSA_POR_OUTRO
  // #2185 (E20): "Você respondeu" só vale quando a pausa é minha; a conversa de um colega não é.
  const proximo = !bloqueada && modo.chave === MODOS.PAUSADO && !outro
    ? proximoPassoDepoisDeResponder(conversa, pausado)
    : null
  const semReabrir = modo.chave === MODOS.ENCERRADO && !podeReabrir
  const detalhe = [modo.rotulo + '.', semReabrir ? LINHA_ENCERRADA_SEM_PERMISSAO : modo.detalhe, proximo?.texto].filter(Boolean).join(' ')
  const motivo = conversa?.passagem?.motivo ?? null

  let linha = semReabrir ? LINHA_ENCERRADA_SEM_PERMISSAO : LINHA[modo.chave]
  if (modo.chave === MODOS.COM_VOCE) linha = origemDaPassagem(conversa.passagem).motivo
  else if (outro) linha = 'Assistente pausado.'
  else if (proximo) linha = LINHA_DO_PROXIMO[proximo.chave] ?? linha
  else if (modo.chave === MODOS.PAUSADO && motivo) linha = motivo

  return {
    chave: modo.chave,
    tom: modo.tom,
    rotulo: rotuloDoModo(modo, conversa, outro),
    linha,
    detalhe,
    proximo,
    sugereEncerrar: proximo?.chave === 'encerrar-ou-aguardar',
  }
}

// #2134: o Assistente está de fato respondendo esta conversa. Responder por cima dele são duas
// vozes falando com o cliente, então quem escreve assume antes (convenção do Intercom e do Front).
// Automático desligado na loja não responde ninguém: não há o que assumir.
export function assistenteAtendendo(conversa, pausado = false, bloqueada = false) {
  return modoDoAtendimento(conversa, pausado, bloqueada).chave === MODOS.LIGADO
    && !automaticoDesligadoNaConversa(conversa)
}
