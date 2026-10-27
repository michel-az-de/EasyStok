import { useEffect, useRef } from 'react'
import { entradaDoPerfil } from '../dominio/acessoModulos'
import { deveAbrirSozinho, passoDaRetomada } from '../dominio/configuracaoInicial'
import { hashDoPasso } from '../dominio/rota'

// #2070: a entrada do Console abre os Primeiros passos sozinha. Espera o acesso (`dados`) e o estado lido do servidor
// (nulo sem `configurarLoja`, que nem lê) e decide uma vez por montagem: o estado relido depois (marcar um passo,
// voltar ao hall) não redireciona de novo. A visita da sessão diz se já abriu (`abriuNestaVisita`) e `aoAbrir` a marca.
export function useAberturaDosPrimeirosPassos({ dados, estado, hashNaEntrada, abriuNestaVisita, aoAbrir }) {
  const decidiu = useRef(false)
  const visita = useRef({ abriuNestaVisita, aoAbrir })
  useEffect(() => { visita.current = { abriuNestaVisita, aoAbrir } })
  useEffect(() => {
    if (decidiu.current || !dados || !estado) return
    decidiu.current = true
    const abrir = deveAbrirSozinho(estado, {
      hashAtual: window.location.hash, hashNaEntrada, hashDaPorta: entradaDoPerfil(dados), jaAbriuNaVisita: visita.current.abriuNestaVisita() === true,
    })
    if (!abrir) return
    visita.current.aoAbrir()
    window.location.hash = hashDoPasso(passoDaRetomada(estado))
  }, [dados, estado, hashNaEntrada])
}
