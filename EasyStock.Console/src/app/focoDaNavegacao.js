export function focarInicioDaTela(documento) {
  if (documento.querySelector('dialog[open]')) return false
  const destino = documento.querySelector('[data-inicio-tela]')
  if (!destino) return false
  destino.focus({ preventScroll: true })
  return true
}
