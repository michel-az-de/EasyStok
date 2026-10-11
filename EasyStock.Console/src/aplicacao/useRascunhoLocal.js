import { useEffect, useLayoutEffect, useState, useSyncExternalStore } from 'react'
import { useAcoes } from './contextos'
import { criarRascunhoLocal } from './rascunhoLocal'

// Texto do campo de escrever como estado só do campo (#1577): a tecla re-renderiza quem chama este hook e
// mais ninguém. O que vai ao estado global, e quando, é de `rascunhoLocal.js`.
export function useRascunhoLocal({ conversaId, rascunho, escritaDeFora }) {
  const { descarregarRascunho } = useAcoes()
  const [campo] = useState(() => criarRascunhoLocal({
    conversaId, rascunho, escritaDeFora, aoDespachar: descarregarRascunho,
  }))
  const texto = useSyncExternalStore(campo.assinar, campo.texto)

  // Antes de pintar: sugestão, resposta do assistente e o limpar do envio chegam ao campo no mesmo quadro.
  useLayoutEffect(() => {
    campo.ver({ conversaId, rascunho, escritaDeFora })
  }, [campo, conversaId, rascunho, escritaDeFora])

  // Tela desmontando (401, ida ao Hall, troca de arranjo ao girar o tablet) ou aba saindo: o atraso não
  // espera. Só a desmontagem avisa `desmontando`: o campo que nascer no lugar precisa adotar o texto.
  useEffect(() => {
    const aoSair = () => campo.descarregar({ ja: true })
    const aoEsconder = () => { if (document.visibilityState === 'hidden') aoSair() }
    window.addEventListener('pagehide', aoSair)
    document.addEventListener('visibilitychange', aoEsconder)
    return () => {
      window.removeEventListener('pagehide', aoSair)
      document.removeEventListener('visibilitychange', aoEsconder)
      campo.descarregar({ ja: true, desmontando: true })
    }
  }, [campo])

  return { texto, digitar: campo.digitar, escrever: campo.escrever, descarregar: campo.descarregar }
}
