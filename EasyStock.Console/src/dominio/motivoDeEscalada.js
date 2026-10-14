// Motivo da passagem para a equipe na fala do balcão (#1761). O EasyStok grava o motivo como o
// sistema escreve ("agente desligado (LLM indisponível)") e a nota interna com o prefixo
// "escalado para a dona: ". A atendente lia "LLM" e "escalado" no cartão, na faixa e na conversa.
// Puro: recebe o texto gravado, devolve o que a tela mostra.

import { dataHora } from './formato'

export const AUTOMATICO_DESLIGADO = 'o atendimento automático está desligado'

const MOTIVOS_CONHECIDOS = [
  [/^agente desligado/i, AUTOMATICO_DESLIGADO],
  [/^atendimento automático desligado/i, AUTOMATICO_DESLIGADO],
]

// Motivo desconhecido segue como veio, sem o trecho técnico entre parênteses.
export function motivoDeEscalada(motivo) {
  const texto = String(motivo ?? '').trim()
  if (!texto) return null
  const conhecido = MOTIVOS_CONHECIDOS.find(([padrao]) => padrao.test(texto))
  if (conhecido) return conhecido[1]
  return texto.replace(/\s*\([^)]*\)/g, '').replace(/\s{2,}/g, ' ').trim() || null
}

// `EscalarConversaUseCase.PrefixoMotivo`: a nota que o EasyStok põe na conversa ao passar para a equipe.
const NOTA_DE_ESCALADA = /^escalado para a dona:\s*/i

// #1761 (L6): com o automático desligado na loja, "Devolver ao automático" promete o que não
// acontece. O Console não lê essa chave da loja no EasyStok; o sinal que ele tem é o motivo da
// passagem, que some quando alguém assume ou troca quando a conversa é reaberta, e a nota que o
// EasyStok deixou na conversa. Vale a nota de passagem mais recente.
export function automaticoDesligadoNaConversa(conversa) {
  if (conversa?.passagem?.motivo === AUTOMATICO_DESLIGADO) return true
  const mensagens = conversa?.mensagens ?? []
  const indice = mensagens.findLastIndex((m) => m.dir === 'sistema' && m.texto?.startsWith('Passou para você'))
  if (indice < 0 || mensagens[indice].texto !== `Passou para você: ${AUTOMATICO_DESLIGADO}`) return false
  // Se o automático respondeu depois da nota, a loja o religou: o sinal antigo não vale mais.
  return !mensagens.slice(indice + 1).some((m) => m.origemAutomatica === 'agente')
}

export function notaDeSistema(texto) {
  const cru = String(texto ?? '')
  if (!NOTA_DE_ESCALADA.test(cru)) return cru
  const motivo = motivoDeEscalada(cru.replace(NOTA_DE_ESCALADA, ''))
  return motivo ? `Passou para você: ${motivo}` : 'Passou para você'
}

// Só a nota interna reconhecida pela tradução usa este formato. O histórico gravado fica intacto.
export function notaDeReabertura(texto) {
  const partes = /^(Conversa reaberta por .+\. Encerramento anterior: )(\d{2})\/(\d{2})\/(\d{4}) (\d{2}):(\d{2}):(\d{2}) UTC\.$/.exec(texto)
  if (!partes) return texto
  const [, prefixo, dia, mes, ano, hora, minuto, segundo] = partes
  const iso = `${ano}-${mes}-${dia}T${hora}:${minuto}:${segundo}.000Z`
  const data = new Date(iso)
  // Date aceita alguns dias inexistentes por transbordo (31/02). Não reinterpretar esses registros.
  if (Number.isNaN(data.getTime()) || data.toISOString() !== iso) return texto
  return `${prefixo}${dataHora(data)} (horário da loja).`
}
