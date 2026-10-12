import { useCallback, useEffect, useMemo, useState } from 'react'
import { criarGestaoSecoes, lerSecoes } from './gestaoSecoes'

// Categorias do cardápio lidas da API (M1.3, #1483). Lista da tela; nada no reducer.
export function useSecoesCardapioApi() {
  const [carga, setCarga] = useState({ estado: 'carregando', secoes: [], erro: null, aviso: null })

  const recarregar = useCallback(() => lerSecoes()
    .then((secoes) => setCarga((atual) => ({ ...atual, estado: 'ok', secoes, erro: null })))
    .catch((erro) => setCarga((atual) => ({ ...atual, estado: 'erro', erro: erro.message }))), [])

  useEffect(() => {
    let vivo = true
    lerSecoes()
      .then((secoes) => { if (vivo) setCarga((atual) => ({ ...atual, estado: 'ok', secoes, erro: null })) })
      .catch((erro) => { if (vivo) setCarga((atual) => ({ ...atual, estado: 'erro', erro: erro.message })) })
    return () => { vivo = false }
  }, [])

  const { secoes } = carga
  const acoes = useMemo(() => criarGestaoSecoes({
    obterSecoes: () => secoes,
    recarregar,
    aoErro: (mensagem) => setCarga((atual) => ({ ...atual, aviso: mensagem })),
    aoAviso: (mensagem) => setCarga((atual) => ({ ...atual, aviso: mensagem })),
  }), [secoes, recarregar])

  const fecharAviso = useCallback(() => setCarga((atual) => ({ ...atual, aviso: null })), [])
  return { ...carga, recarregar, fecharAviso, ...acoes }
}
