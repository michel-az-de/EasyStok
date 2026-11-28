import { useCallback, useSyncExternalStore } from 'react'

// #3392 (WCAG 2.1.4): guarda única dos atalhos de uma tecla (A, J, K e "/"), tirada do `A` da BarraModo (#3374).
// Só vale sem modificador, sem repetição, fora de campo de texto, menu, janela e popover, e com o interruptor ligado.
// Exceção: a "/" pode vir com Shift, porque alguns teclados só a fazem assim.
const CHAVE = 'easystok.atalhosDeUmaTecla.v1'
let daVisita = true

export function lerAtalhosLigados(armazenamento) {
  try {
    const guardado = (armazenamento ?? globalThis.localStorage).getItem(CHAVE)
    if (guardado === null) return daVisita
    daVisita = guardado !== 'false'
    return daVisita
  } catch { return daVisita }
}

export function gravarAtalhosLigados(ligado, armazenamento) {
  daVisita = ligado
  try { (armazenamento ?? globalThis.localStorage).setItem(CHAVE, String(ligado)) } catch { /* vale só na visita */ }
}

const ouvintes = new Set()
function assinar(ouvinte) {
  ouvintes.add(ouvinte)
  window.addEventListener('storage', ouvinte)
  return () => { ouvintes.delete(ouvinte); window.removeEventListener('storage', ouvinte) }
}

// [ligado, alternar]
export function useAtalhosLigados() {
  const ligado = useSyncExternalStore(assinar, () => lerAtalhosLigados(), () => true)
  const alternar = useCallback(() => {
    gravarAtalhosLigados(!lerAtalhosLigados())
    ouvintes.forEach((ouvinte) => ouvinte())
  }, [])
  return [ligado, alternar]
}

export function atalhoLivre(e) {
  if (!lerAtalhosLigados()) return false
  if (e.ctrlKey || e.altKey || e.metaKey || e.repeat || e.defaultPrevented || e.isComposing) return false
  if (e.shiftKey && e.key !== '/') return false
  if (e.target?.closest?.('input, textarea, select, [contenteditable]:not([contenteditable="false"]), [role=group][popover], [role=dialog], [role=menu], [role=listbox], [role=combobox], dialog')) return false
  return !document.querySelector('dialog[open]')
}

// #3392: aviso para leitor de tela que sobrevive à lista escondida (aba Conversa no celular): um nó fixo no corpo da página.
let anunciador = null
export function anunciar(texto) {
  if (typeof document === 'undefined') return
  if (!anunciador?.isConnected) {
    anunciador = document.createElement('div')
    anunciador.className = 'sr'
    anunciador.setAttribute('role', 'status')
    anunciador.setAttribute('aria-live', 'polite')
    document.body.appendChild(anunciador)
  }
  anunciador.textContent = ''
  setTimeout(() => { if (anunciador) anunciador.textContent = texto }, 50)
}
