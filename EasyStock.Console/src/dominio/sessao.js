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
  // #1582: o botão ao lado deste aviso é "Sair", que apaga os rascunhos. O texto não manda sair.
  return minutos > 0
    ? `Sessão vence em ${minutos} min. Quando vencer, entre de novo: o rascunho fica guardado.`
    : 'Sessão vencida. Entre de novo; o rascunho fica guardado.'
}

// Mostrado no login quando a sessão acabou sozinha (#1582); sair por vontade própria não mostra.
export const AVISO_SESSAO_VENCIDA = 'Sua sessão venceu. Entre de novo para continuar de onde parou.'

// "Nome · Empresa" da faixa da sessão (#1447). Empresa sem nome some, em vez de
// aparecer "· null" (visto na homologação de 07/10).
export function rotuloDaSessao(sessao) {
  return [sessao?.usuario?.nome, sessao?.empresa?.nome].filter(Boolean).join(' · ')
}
