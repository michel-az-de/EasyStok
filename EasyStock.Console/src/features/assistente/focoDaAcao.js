export function focarResultadoDaAcao(origem, documento, estado = 'feita') {
  if (!origem?.item?.isConnected) return false
  if (documento.activeElement !== origem.botao && documento.activeElement !== documento.body) return false
  const destino = origem.item.querySelector(estado === 'falhou' ? 'button:not(:disabled)' : '[data-status-acao]')
  if (!destino) return false
  destino.focus({ preventScroll: true })
  return true
}
