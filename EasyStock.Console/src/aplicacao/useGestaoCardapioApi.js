import { useCallback, useEffect, useMemo, useState } from 'react'
import { criarGestaoCardapio, lerGestao } from './gestaoCardapio'

// Lista de gestão do cardápio lida da API (M1.1, #1481). Nada fica no reducer: a lista é da tela
// de gestão, e o cardápio da comanda continua vindo da sincronização.
//
// estado: carregando | ok | erro
export function useGestaoCardapioApi() {
  const [carga, setCarga] = useState({ estado: 'carregando', itens: [], erro: null, aviso: null })

  const recarregar = useCallback(() => lerGestao()
    .then((itens) => setCarga((atual) => ({ ...atual, estado: 'ok', itens, erro: null })))
    .catch((erro) => setCarga((atual) => ({ ...atual, estado: 'erro', erro: erro.message }))), [])

  useEffect(() => {
    let vivo = true
    lerGestao()
      .then((itens) => { if (vivo) setCarga((atual) => ({ ...atual, estado: 'ok', itens, erro: null })) })
      .catch((erro) => { if (vivo) setCarga((atual) => ({ ...atual, estado: 'erro', erro: erro.message })) })
    return () => { vivo = false }
  }, [])

  const { itens } = carga
  const acoes = useMemo(() => criarGestaoCardapio({
    obterItens: () => itens,
    recarregar,
    aoErro: (mensagem) => setCarga((atual) => ({ ...atual, aviso: mensagem })),
  }), [itens, recarregar])

  const fecharAviso = useCallback(() => setCarga((atual) => ({ ...atual, aviso: null })), [])
  return { ...carga, recarregar, fecharAviso, ...acoes }
}
