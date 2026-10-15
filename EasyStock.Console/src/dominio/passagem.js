// Origem do "Passou para você" (rodada 12, issue #16). Pergunta da Thatiane no
// áudio de 26/09/2026: o cartão da Beatriz dizia "Passou para você" e não
// dizia QUEM passou, POR QUÊ nem QUANDO. A resposta é uma frase só, a mesma
// no cartão do Balcão, na barra da conversa e no lembrete, para as três
// telas nunca contarem histórias diferentes.
//
// Arquivo próprio porque `automatico.js` e `lembrete.js` precisam dele, e
// `automatico.js` já importa `lembrete.js` (import circular se morasse lá).
//
// Puro. A hora sai de `passagem.em`, gravada quando o agente devolveu.

import { horaCurta } from './formato'
import { AUTOMATICO_DESLIGADO } from './motivoDeEscalada'
import { pedidoEncerrado } from './pedido'
import { ultimaFala } from './mensagem'

export const MOTIVO_PADRAO = 'O agente preferiu não responder sozinho'

// #1804: reabrir a conversa de um pedido já entregue ou cancelado não cria pendência. Ela só
// volta a "Precisa de você" quando a última fala é do cliente; antes disso, é só encerrar. Sem
// mensagens carregadas (conversa fora da tela no modo API), vale o número de não lidas.
export function passagemPendente(conversa) {
  const passagem = conversa?.passagem
  if (!passagem || passagem.assumida) return false
  if (passagem.origem !== 'reabertura' || !pedidoEncerrado(conversa.pedido)) return true
  if (!conversa.mensagens?.length) return Number(conversa.naoLidas) > 0
  return ultimaFala(conversa)?.dir === 'in'
}

const minuscula = (texto) => texto.charAt(0).toLowerCase() + texto.slice(1)

export function origemDaPassagem(passagem) {
  if (!passagem) return null
  const motivo = passagem.motivo ?? MOTIVO_PADRAO
  const hora = passagem.em ? horaCurta(passagem.em) : null
  if (passagem.origem === 'chat-site') return { motivo, hora, curto: 'Chat do site', longo: motivo }
  if (passagem.origem === 'reabertura') return { motivo, hora, curto: motivo, longo: motivo }
  // #1761: com o automático desligado ninguém "não soube responder": a conversa só veio para a equipe.
  if (motivo === AUTOMATICO_DESLIGADO) {
    return {
      motivo, hora,
      // O cartão e o lembrete já dizem "Passou para você" na linha de estado: aqui vai só o porquê.
      curto: `Sem automático${hora ? ` (${hora})` : ''}`,
      longo: `A conversa passou para você${hora ? ` às ${hora}` : ''} porque ${motivo}.`,
    }
  }
  return {
    motivo,
    hora,
    // Cartão do Balcão e lembrete: uma linha que cabe em duas no cartão.
    curto: `Automático não soube responder${hora ? ` (${hora})` : ''}: ${minuscula(motivo)}`,
    // Barra da conversa: a frase inteira, com espaço para ler.
    longo: `O atendimento automático não soube responder e passou para você${hora ? ` às ${hora}` : ''}. `
      + `Motivo: ${minuscula(motivo)}.`,
  }
}
