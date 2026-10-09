import { useCallback, useEffect, useMemo, useState } from 'react'
import { criarGestaoPerdas, lerResumoDePerdas, lerVencidos } from './perdas'
import { lerEstoqueDoDia } from './estoqueDoDia'
import { lerInsumos } from './insumos'

// Perdas lidas da API (M2.6, #1511): resumo do período, lotes vencidos e o que pode ser perdido
// (pratos do estoque do dia e insumos). estado: carregando | ok | erro
const opcoesDe = (estoque, insumos) => [
  ...(estoque?.pratos ?? []).filter((p) => p.produtoId).map((p) => ({ produtoId: p.produtoId, nome: p.nome, tipo: 'prato', saldo: p.saldo })),
  ...(insumos ?? []).map((i) => ({ produtoId: i.id, nome: i.nome, tipo: 'insumo', saldo: i.saldo, unidade: i.unidade })),
]

export function usePerdasApi() {
  const [periodo, setPeriodo] = useState({ de: null, ate: null })
  const [carga, setCarga] = useState({ estado: 'carregando', resumo: null, vencidos: [], opcoes: [], erro: null, aviso: null, feito: null })

  const ler = useCallback(async () => {
    const [resumo, vencidos, estoque, insumos] = await Promise.all([
      lerResumoDePerdas(periodo.de, periodo.ate), lerVencidos(), lerEstoqueDoDia().catch(() => null), lerInsumos().catch(() => []),
    ])
    return { resumo, vencidos, opcoes: opcoesDe(estoque, insumos) }
  }, [periodo.de, periodo.ate])

  const recarregar = useCallback(() => ler()
    .then((dados) => setCarga((atual) => ({ ...atual, ...dados, estado: 'ok', erro: null })))
    .catch((erro) => setCarga((atual) => ({ ...atual, estado: 'erro', erro: erro.message }))), [ler])

  useEffect(() => {
    let vivo = true
    ler()
      .then((dados) => { if (vivo) setCarga((atual) => ({ ...atual, ...dados, estado: 'ok', erro: null })) })
      .catch((erro) => { if (vivo) setCarga((atual) => ({ ...atual, estado: 'erro', erro: erro.message })) })
    return () => { vivo = false }
  }, [ler])

  const acoes = useMemo(() => criarGestaoPerdas({
    recarregar,
    aoErro: (mensagem) => setCarga((atual) => ({ ...atual, aviso: mensagem, feito: null })),
    aoFeito: (mensagem) => setCarga((atual) => ({ ...atual, feito: mensagem, aviso: null })),
  }), [recarregar])

  const fecharAviso = useCallback(() => setCarga((atual) => ({ ...atual, aviso: null, feito: null })), [])
  return { ...carga, periodo, setPeriodo, recarregar, fecharAviso, ...acoes }
}
