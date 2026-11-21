// A Ajuda do Console abre de dois lugares: do botão do topo (ControleAjuda) e do menu da conta no pé do
// trilho (#3004). O menu pede, o controle do topo (sempre montado na moldura) abre o diálogo.
import { useEffect } from 'react'

const EVENTO = 'easystok:pedir-ajuda'

export const pedirAjuda = () => window.dispatchEvent(new Event(EVENTO))

export function useQuandoPedirAjuda(abrir) {
  useEffect(() => {
    window.addEventListener(EVENTO, abrir)
    return () => window.removeEventListener(EVENTO, abrir)
  }, [abrir])
}
