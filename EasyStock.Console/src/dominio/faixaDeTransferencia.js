// #2232 (A2): a faixa de transferência no topo da conversa. Antes era uma pílula "Passou para você" com um
// quadrado de 8 px e a frase escondida; agora a faixa diz quem passou e por quê numa linha só, e a frase
// inteira (com a hora) fica no `title`. Puro: a frase longa é de `origemDaPassagem`, só lida.
import { origemDaPassagem } from './passagem.js'
import { AUTOMATICO_DESLIGADO } from './motivoDeEscalada.js'

const TITULO = 'O automático passou para você'

export function faixaDaTransferencia(passagem) {
  const origem = origemDaPassagem(passagem)
  if (!origem) return null
  let titulo = TITULO
  if (passagem.origem === 'chat-site') titulo = 'O chat do site passou para você'
  else if (passagem.origem === 'reabertura') titulo = 'A conversa voltou para você'
  else if (origem.motivo === AUTOMATICO_DESLIGADO) titulo = 'A conversa veio para você'
  return {
    titulo,
    motivo: origem.motivo,
    texto: `${titulo} · ${origem.motivo}`,
    longo: origem.longo,
  }
}
