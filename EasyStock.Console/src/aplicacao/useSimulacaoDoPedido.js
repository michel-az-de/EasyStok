import { useEffect, useMemo, useState } from 'react'
import { simulacaoVale } from '../dominio/simulacaoPedido'
import { corpoDaSimulacao, simularPedido } from '../infra/api/comandaApi'

const ESPERA_MS = 350

// #1884 ("Quanto fica?"): pede ao servidor o frete, o total, o sinal e o restante da comanda ainda não
// criada. Um pedido por mudança da comanda (itens, porção, janela), com espera curta e a chamada anterior
// abortada. Falhou ou a comanda ainda não dá para simular (sem endereço, janela esgotada, item fora do
// cardápio): fica sem simulação e a tela segue com o texto de antes, sem alarme. O que impede de gerar a
// cobrança já aparece na barra.
export function useSimulacaoDoPedido({ conversaId, pedido, ativo }) {
  const corpo = useMemo(
    () => (ativo && conversaId && simulacaoVale(pedido) ? JSON.stringify(corpoDaSimulacao(pedido)) : null),
    [ativo, conversaId, pedido],
  )
  const [resultado, setResultado] = useState({ chave: null, simulacao: null })
  const chave = corpo ? `${conversaId}|${corpo}` : null

  useEffect(() => {
    if (!corpo) return undefined
    const controle = new AbortController()
    const espera = setTimeout(() => {
      simularPedido(conversaId, JSON.parse(corpo), controle.signal)
        .then((simulacao) => { if (!controle.signal.aborted) setResultado({ chave, simulacao }) })
        .catch(() => { if (!controle.signal.aborted) setResultado({ chave, simulacao: null }) })
    }, ESPERA_MS)
    return () => { clearTimeout(espera); controle.abort() }
  }, [conversaId, corpo, chave])

  // Resposta de outra comanda ou de uma versão antiga da mesma não vale.
  return resultado.chave === chave ? resultado.simulacao : null
}
