import { useCallback, useEffect, useMemo, useState } from 'react'
import { criarGestaoInsumos, lerInsumos } from './insumos'

// Insumos lidos da API (M2.3, #1496). estado: carregando | ok | erro
export function useInsumosApi() {
  const [carga, setCarga] = useState({ estado: 'carregando', insumos: [], erro: null, aviso: null })

  const recarregar = useCallback(() => lerInsumos()
    .then((insumos) => setCarga((atual) => ({ ...atual, estado: 'ok', insumos, erro: null })))
    .catch((erro) => setCarga((atual) => ({ ...atual, estado: 'erro', erro: erro.message }))), [])

  useEffect(() => {
    let vivo = true
    lerInsumos()
      .then((insumos) => { if (vivo) setCarga((atual) => ({ ...atual, estado: 'ok', insumos, erro: null })) })
      .catch((erro) => { if (vivo) setCarga((atual) => ({ ...atual, estado: 'erro', erro: erro.message })) })
    return () => { vivo = false }
  }, [])

  const acoes = useMemo(() => criarGestaoInsumos({
    recarregar,
    aoErro: (mensagem) => setCarga((atual) => ({ ...atual, aviso: mensagem })),
  }), [recarregar])

  const fecharAviso = useCallback(() => setCarga((atual) => ({ ...atual, aviso: null })), [])
  return { ...carga, recarregar, fecharAviso, ...acoes }
}
