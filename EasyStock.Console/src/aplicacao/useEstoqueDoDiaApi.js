import { useCallback, useEffect, useMemo, useState } from 'react'
import { criarEstoqueDoDia, lerEstoqueDoDia } from './estoqueDoDia'

// Estoque do dia lido da API (M2.1, #1490). estado: carregando | ok | erro
export function useEstoqueDoDiaApi() {
  const [carga, setCarga] = useState({ estado: 'carregando', pratos: [], alertas: [], erro: null, aviso: null, feito: null })

  const recarregar = useCallback(() => lerEstoqueDoDia()
    .then((r) => setCarga((atual) => ({ ...atual, ...r, estado: 'ok', erro: null })))
    .catch((erro) => setCarga((atual) => ({ ...atual, estado: 'erro', erro: erro.message }))), [])

  useEffect(() => {
    let vivo = true
    lerEstoqueDoDia()
      .then((r) => { if (vivo) setCarga((atual) => ({ ...atual, ...r, estado: 'ok', erro: null })) })
      .catch((erro) => { if (vivo) setCarga((atual) => ({ ...atual, estado: 'erro', erro: erro.message })) })
    return () => { vivo = false }
  }, [])

  const acoes = useMemo(() => criarEstoqueDoDia({
    recarregar,
    aoErro: (mensagem) => setCarga((atual) => ({ ...atual, aviso: mensagem, feito: null })),
    aoFeito: (mensagem) => setCarga((atual) => ({ ...atual, feito: mensagem, aviso: null })),
  }), [recarregar])

  const fecharAviso = useCallback(() => setCarga((atual) => ({ ...atual, aviso: null, feito: null })), [])
  return { ...carga, recarregar, fecharAviso, ...acoes }
}
