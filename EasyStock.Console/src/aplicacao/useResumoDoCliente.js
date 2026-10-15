import { useEffect, useState } from 'react'
import { obterResumoDoCliente } from '../infra/api/conversasApi'

// #1839: resumo do cliente pela IA. O cache guarda a resposta pela conversa e pelo que muda o resumo
// (pedidos e notas): compra ou nota nova pede outro. Falha fica marcada só para esta combinação, sem
// tentar em laço. Efeito abortado (StrictMode, troca de conversa) não deixa marca: o próximo busca de novo.
const lidos = new Map()
const falhas = new Set()

export function useResumoDoCliente(conversaId, chaveDoCliente, ativo) {
  const chave = `${conversaId}|${chaveDoCliente}`
  const [, setVersao] = useState(0)
  useEffect(() => {
    if (!ativo || !conversaId || lidos.has(chave) || falhas.has(chave)) return undefined
    const controle = new AbortController()
    obterResumoDoCliente(conversaId, controle.signal)
      .then((r) => { lidos.set(chave, r?.texto ?? null) })
      .catch(() => { if (!controle.signal.aborted) falhas.add(chave) })
      .finally(() => { if (!controle.signal.aborted) setVersao((v) => v + 1) })
    return () => controle.abort()
  }, [chave, conversaId, ativo])
  if (!ativo) return { texto: null }
  return { texto: lidos.get(chave) ?? null, carregando: !lidos.has(chave) && !falhas.has(chave) }
}
