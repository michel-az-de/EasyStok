// Canal fora da aba (rodada 10, item "falta construir" 6): a Notification API
// do navegador, para os motivos de "Precisa de você" chegarem mesmo com a
// aba minimizada ou outra janela em foco (US-013/UC-02, "todos os
// dispositivos logados"; pedido literal do dono, "não tem notificações de
// sistema"). Central de verdade (empurrar para celular/tablet de verdade)
// depende de servidor, fora do escopo de uma correção pequena: a fila já tem
// o app nativo (`auditoria/decisoes/27-som.md`) como a solução definitiva.
// Isto aqui é só o degrau de dentro do navegador.
//
// Permissão é sempre um gesto explícito da dona (`pedirPermissao`), nunca
// pedida sozinha: navegador nenhum concede notificação sem um clique atrás.

const suportado = () => typeof window !== 'undefined' && 'Notification' in window

export function permissaoDeNotificacao() {
  return suportado() ? Notification.permission : 'indisponivel'
}

export function pedirPermissaoDeNotificacao() {
  if (!suportado()) return Promise.resolve('indisponivel')
  return Notification.requestPermission()
}

// `tag` por conversa: uma segunda notificação da MESMA conversa substitui a
// primeira (não empilha), mas conversas diferentes não se atropelam.
export function notificarPrecisaDeVoce(conversaId, titulo, corpo) {
  if (!suportado() || Notification.permission !== 'granted') return false
  try {
    // eslint-disable-next-line no-new
    new Notification(titulo, { body: corpo, tag: `casa-da-baba-precisa-${conversaId}` })
    return true
  } catch {
    return false
  }
}
