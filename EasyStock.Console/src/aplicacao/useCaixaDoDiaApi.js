import { useCallback, useEffect, useState } from 'react'
import { listarFechamentos, obterCaixaDoDia } from '../infra/api/caixaApi'

// Caixa do dia lido da API (#1443): o resumo, os lançamentos e os últimos fechamentos. A aba Caixa e
// o gesto de abrir a loja leem daqui e chamam `recarregar` depois de cada ação; nada fica no
// reducer, porque o saldo esperado é conta da API, não do navegador.
//
// estado: carregando | ok | erro
export function useCaixaDoDiaApi({ fechamentos: quantosFechamentos = 7 } = {}) {
  const [carga, setCarga] = useState({ estado: 'carregando', dia: null, fechamentos: [], erro: null })

  const recarregar = useCallback(() => Promise.all([obterCaixaDoDia(), listarFechamentos(quantosFechamentos)])
    .then(([dia, fechamentos]) => setCarga({ estado: 'ok', dia, fechamentos, erro: null }))
    .catch((erro) => setCarga((atual) => ({ ...atual, estado: 'erro', erro: erro.message }))), [quantosFechamentos])

  useEffect(() => {
    let vivo = true
    Promise.all([obterCaixaDoDia(), listarFechamentos(quantosFechamentos)])
      .then(([dia, fechamentos]) => { if (vivo) setCarga({ estado: 'ok', dia, fechamentos, erro: null }) })
      .catch((erro) => { if (vivo) setCarga((atual) => ({ ...atual, estado: 'erro', erro: erro.message })) })
    return () => { vivo = false }
  }, [quantosFechamentos])

  return { ...carga, recarregar }
}
