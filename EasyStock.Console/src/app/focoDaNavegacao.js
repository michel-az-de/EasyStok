export function focarInicioDaTela(documento) {
  const dialogos = documento.querySelectorAll('dialog[open]')
  if ([...dialogos].some(dialogo => dialogo.getAttribute('aria-modal') !== 'false')) return false
  const destino = documento.querySelector('[data-inicio-tela]')
  if (!destino) return false
  destino.focus({ preventScroll: true })
  return true
}
