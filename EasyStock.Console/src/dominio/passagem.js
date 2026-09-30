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

export const MOTIVO_PADRAO = 'O agente preferiu não responder sozinho'

const minuscula = (texto) => texto.charAt(0).toLowerCase() + texto.slice(1)

export function origemDaPassagem(passagem) {
  if (!passagem) return null
  const motivo = passagem.motivo ?? MOTIVO_PADRAO
  const hora = passagem.em ? horaCurta(passagem.em) : null
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
