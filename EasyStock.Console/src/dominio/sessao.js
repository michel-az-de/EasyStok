// Aviso de sessão perto de vencer (F07, item 6). Puro.
//
// O JWT da API vale 8 h e não há refresh nesta versão: no fim do turno o 401
// devolve a dona para o login. O aviso aparece 10 min antes, com tempo para
// ela terminar a mensagem; o rascunho fica guardado de qualquer jeito.
export const AVISO_ANTES_DO_VENCIMENTO_MS = 10 * 60000

export function avisoDeVencimento(venceEm, agora) {
  if (!venceEm) return null
  const resta = venceEm - agora
  if (resta > AVISO_ANTES_DO_VENCIMENTO_MS) return null
  const minutos = Math.ceil(resta / 60000)
  return minutos > 0
    ? `Sessão vence em ${minutos} min. Entre de novo; o rascunho fica guardado.`
    : 'Sessão vencida. Entre de novo; o rascunho fica guardado.'
}

// "Nome · Empresa" da faixa da sessão (#1447). Empresa sem nome some, em vez de
// aparecer "· null" (visto na homologação de 07/10).
export function rotuloDaSessao(sessao) {
  return [sessao?.usuario?.nome, sessao?.empresa?.nome].filter(Boolean).join(' · ')
}
