import { useEffect, useState } from 'react'
import { obterResumoDoCliente } from '../infra/api/conversasApi'

// #1839: resumo do cliente pela IA, uma leitura por conversa enquanto o console está aberto (cada
// leitura é uma chamada paga). Falha ou IA desligada viram `null`: a tela mostra só os números.
const lidos = new Map()

export function useResumoDoCliente(conversaId, ativo) {
  const [, setVersao] = useState(0)
  useEffect(() => {
    if (!ativo || !conversaId || lidos.has(conversaId)) return undefined
    const controle = new AbortController()
    lidos.set(conversaId, { carregando: true })
    obterResumoDoCliente(conversaId, controle.signal)
      .then((r) => { lidos.set(conversaId, { texto: r?.texto ?? null }) })
      .catch(() => { if (controle.signal.aborted) lidos.delete(conversaId); else lidos.set(conversaId, { texto: null }) })
      .finally(() => setVersao((v) => v + 1))
    return () => controle.abort()
  }, [conversaId, ativo])
  return (ativo && lidos.get(conversaId)) || { texto: null }
}
