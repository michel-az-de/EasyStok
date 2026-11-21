import { useCallback, useEffect, useMemo, useState } from 'react'
import { criarGestaoReceitas, lerReceitas } from './receitas'
import { lojaDesligada } from './planoDeSincronizacao'

// Receitas lidas da API (M2.4a, #1498). estado: carregando | ok | erro
export function useReceitasApi() {
  const [carga, setCarga] = useState({ estado: 'carregando', receitas: [], erro: null, aviso: null })

  const recarregar = useCallback(() => lerReceitas()
    .then((receitas) => setCarga((atual) => ({ ...atual, estado: 'ok', receitas, erro: null, semLoja: false })))
    .catch((erro) => setCarga((atual) => ({ ...atual, estado: 'erro', erro: erro.message, semLoja: lojaDesligada(erro) }))), [])

  useEffect(() => {
    let vivo = true
    lerReceitas()
      .then((receitas) => { if (vivo) setCarga((atual) => ({ ...atual, estado: 'ok', receitas, erro: null, semLoja: false })) })
      .catch((erro) => { if (vivo) setCarga((atual) => ({ ...atual, estado: 'erro', erro: erro.message, semLoja: lojaDesligada(erro) })) })
    return () => { vivo = false }
  }, [])

  const acoes = useMemo(() => criarGestaoReceitas({
    recarregar,
    aoErro: (mensagem) => setCarga((atual) => ({ ...atual, aviso: mensagem })),
  }), [recarregar])

  const fecharAviso = useCallback(() => setCarga((atual) => ({ ...atual, aviso: null })), [])
  return { ...carga, recarregar, fecharAviso, ...acoes }
}
