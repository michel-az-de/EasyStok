// Falha de envio na fala de quem atende (#1761). O EasyStok grava o erro cru do provedor
// ("Message undeliverable", "Re-engagement message", tempo esgotado do .NET) e a bolha e a lista
// "Não entregues" mostravam isso em inglês. Aqui o motivo vira uma frase que diz o que houve e o
// que fazer. Puro: recebe o texto do erro, devolve o tipo e a frase.

export const TIPOS_DE_FALHA = {
  NAO_ENTREGAVEL: 'nao-entregavel',
  FORA_DA_JANELA: 'fora-da-janela',
  TEMPO_ESGOTADO: 'tempo-esgotado',
  RECUSA: 'recusa',
  DESCONHECIDA: 'desconhecida',
}

export const FRASE_DA_FALHA = {
  [TIPOS_DE_FALHA.NAO_ENTREGAVEL]: 'O WhatsApp não conseguiu entregar: confira se o número está certo.',
  [TIPOS_DE_FALHA.FORA_DA_JANELA]: 'Passaram 24 h desde a última mensagem do cliente: só modelo aprovado chega.',
  [TIPOS_DE_FALHA.TEMPO_ESGOTADO]: 'O WhatsApp demorou a responder. Pode ter chegado: confira com o cliente antes de reenviar.',
  [TIPOS_DE_FALHA.DESCONHECIDA]: 'Não foi enviada. Tente de novo.',
}

// Código da Meta ou trecho estável do texto. A ordem importa: o primeiro que casar vale.
const SINAIS = [
  [TIPOS_DE_FALHA.NAO_ENTREGAVEL, /undeliverable|\b131026\b/i],
  [TIPOS_DE_FALHA.FORA_DA_JANELA, /re-?engagement|\b131047\b|fora_da_janela_24h|fora da janela de 24 h/i],
  [TIPOS_DE_FALHA.TEMPO_ESGOTADO, /timeout|timed out|task was cancell?ed/i],
]

// Recusa escrita pelo próprio Console ou pelo EasyStok ("Não enviada: Sem conexão com o
// EasyStok.") já está em português e segue como veio. O detalhe do canal não: é o erro cru.
const PREFIXO_DA_RECUSA = 'Não enviada: '
const DETALHE_DO_CANAL = 'Falha ao enviar pelo canal'
// O EasyStok já mandou o modelo de retomada e espera o cliente: a frase dele diz isso, e segue.
const JA_EM_PORTUGUES = /modelo de retomada saiu/i

export function falhaDeEnvio(erro) {
  const texto = String(erro ?? '').trim()
  const desconhecida = { tipo: TIPOS_DE_FALHA.DESCONHECIDA, texto: FRASE_DA_FALHA[TIPOS_DE_FALHA.DESCONHECIDA] }
  if (!texto) return desconhecida
  const propria = Object.entries(FRASE_DA_FALHA).find(([, frase]) => frase === texto)
  if (propria) return { tipo: propria[0], texto }
  if (JA_EM_PORTUGUES.test(texto)) return { tipo: TIPOS_DE_FALHA.RECUSA, texto }
  const sinal = SINAIS.find(([, padrao]) => padrao.test(texto))
  if (sinal) return { tipo: sinal[0], texto: FRASE_DA_FALHA[sinal[0]] }
  if (texto.startsWith(PREFIXO_DA_RECUSA) && !texto.includes(DETALHE_DO_CANAL)) return { tipo: TIPOS_DE_FALHA.RECUSA, texto }
  return desconhecida
}

export const textoDaFalhaDeEnvio = (erro) => falhaDeEnvio(erro).texto

// Fora da janela o texto livre não sai: o caminho é o modelo aprovado.
export const pedeModeloAprovado = (erro) => falhaDeEnvio(erro).tipo === TIPOS_DE_FALHA.FORA_DA_JANELA
