// #2230: menu em <details> fecha com toque fora e com Escape, mesmo com o foco fora dele.
// A janela aberta de dentro do menu (Modal inline) conta como dentro, e o Escape dela fecha só ela.
// Sem React: a prova liga isto num documento falso.

export function fecharMenu(menu, doc = menu?.ownerDocument) {
  if (!menu?.open) return
  const comFoco = menu.contains(doc?.activeElement)
  menu.open = false
  if (comFoco) menu.querySelector('summary')?.focus({ preventScroll: true })
}

export function ligarMenuDetalhes(doc, obterMenu) {
  const aoTocar = (evento) => {
    const menu = obterMenu()
    if (menu?.open && !menu.contains(evento.target)) fecharMenu(menu, doc)
  }
  const aoTeclar = (evento) => {
    const menu = obterMenu()
    if (!menu?.open || evento.key !== 'Escape' || evento.defaultPrevented) return
    if (menu.querySelector('dialog[open]') || evento.target?.closest?.('dialog')) return
    evento.preventDefault()
    fecharMenu(menu, doc)
  }
  doc.addEventListener('pointerdown', aoTocar)
  doc.addEventListener('keydown', aoTeclar)
  return () => {
    doc.removeEventListener('pointerdown', aoTocar)
    doc.removeEventListener('keydown', aoTeclar)
  }
}
