import { useEffect, useMemo, useState } from 'react'
import { simulacaoVale } from '../dominio/simulacaoPedido'
import { corpoDaSimulacao, simularPedido } from '../infra/api/comandaApi'

const ESPERA_MS = 350

// #1884 ("Quanto fica?"): pede ao servidor o frete, o total, o sinal e o restante da comanda ainda não
// criada. Um pedido por mudança da comanda (itens, porção, janela), com espera curta e a chamada anterior
// abortada. Falhou ou a comanda ainda não dá para simular (sem endereço, janela esgotada, item fora do
// cardápio): fica sem simulação e a tela segue com o texto de antes, sem alarme. O que impede de gerar a
// cobrança já aparece na barra.
export const useSimulacaoDoPedido = (opcoes) => useSimulacaoDoPedidoComErro(opcoes).simulacao

// #2921: a janela "Novo pedido" não tem a barra da Ficha, então mostra por que a simulação não veio (o 409 de endereço).
export function useSimulacaoDoPedidoComErro({ conversaId, clienteId, endereco, pedido, ativo }) {
  const corpo = useMemo(
    () => (ativo && conversaId && simulacaoVale(pedido) ? JSON.stringify(corpoDaSimulacao(pedido)) : null),
    [ativo, conversaId, pedido],
  )
  const [resultado, setResultado] = useState({ chave: null, simulacao: null, erro: null })
  // O frete usa o cadastro salvo, que pode mudar sem alterar os itens ou a janela.
  const chave = corpo ? JSON.stringify([conversaId, clienteId ?? null, endereco ?? null, corpo]) : null

  useEffect(() => {
    if (!corpo) return undefined
    const controle = new AbortController()
    const espera = setTimeout(() => {
      simularPedido(conversaId, JSON.parse(corpo), controle.signal)
        .then((simulacao) => { if (!controle.signal.aborted) setResultado({ chave, simulacao, erro: null }) })
        .catch((erro) => { if (!controle.signal.aborted) setResultado({ chave, simulacao: null, erro }) })
    }, ESPERA_MS)
    return () => { clearTimeout(espera); controle.abort() }
  }, [conversaId, corpo, chave])

  // Resposta de outra comanda ou de uma versão antiga da mesma não vale.
  return resultado.chave === chave ? resultado : { simulacao: null, erro: null }
}
