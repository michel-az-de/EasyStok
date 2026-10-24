import { useEffect, useRef, useState } from 'react'

// Emitir uma cobrança é ida e volta a um provedor. No protótipo isso é instantâneo,
// mas a tela mostra o estado de carregando mesmo assim: quem usa precisa aprender
// que existe uma espera ali, senão estranha no dia em que o Pix real demorar.
const MS_DE_EMISSAO = 700

// #1287: no modo API a emissão devolve a promessa da ida ao EasyStok; o "emitindo" só
// desliga quando ela volta, e o clique repetido no meio do caminho é ignorado.
// #2005 (console-05): a espera de 700 ms é da demonstração. No modo API (`semEspera`) a ação sai na hora;
// na demonstração, fechar a tela dentro da espera manda o que estava pendente em vez de descartar.
export function useEmissao(aoEmitir, { semEspera = false } = {}) {
  const [emitindo, setEmitindo] = useState(false)
  const pendente = useRef(null)
  const ocupado = useRef(false)

  useEffect(() => () => {
    const emEspera = pendente.current
    if (!emEspera) return
    clearTimeout(emEspera.espera)
    emEspera.sair()
  }, [])

  const emitir = (...argumentos) => {
    if (ocupado.current) return
    ocupado.current = true
    setEmitindo(true)
    const sair = () => {
      pendente.current = null
      const liberar = () => { ocupado.current = false; setEmitindo(false) }
      const ida = aoEmitir(...argumentos)
      if (ida?.finally) ida.finally(liberar)
      else liberar()
    }
    if (semEspera) {
      sair()
      return
    }
    pendente.current = { espera: setTimeout(sair, MS_DE_EMISSAO), sair }
  }

  return { emitindo, emitir }
}
