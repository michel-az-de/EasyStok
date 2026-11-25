import { useEffect, useState } from 'react'
import { perguntarAssistente } from '../infra/api/assistenteApi'
import { listarPedidosDoCliente, obterPedido } from './pedidosCadastro'
import { pedidoValido } from '../dominio/pratosParaOferecer'

// #3344: o Assistente age na Ficha só no clique da atendente. A pergunta já leva o que precisa, então vai sem conversa:
// o histórico de mensagens não segue para o modelo. A resposta é só texto para revisar; nada é enviado ao cliente, as
// ações propostas não rodam aqui, e a espera tem teto de 15 s (sem isso o botão ficaria preso em "Pensando…").
const PRAZO_MS = 15_000
export async function perguntarSobreOCliente(pergunta, { sinal } = {}) {
  const { texto } = await perguntarAssistente(pergunta, null, { sinal: AbortSignal.any([sinal, AbortSignal.timeout(PRAZO_MS)].filter(Boolean)) })
  const limpo = String(texto ?? '').trim()
  if (!limpo) throw new Error('O Assistente não respondeu. Tente de novo.')
  return limpo
}

// O último pedido que valeu (não cancelado) com os itens: 1 leitura da lista (já feita pela aba Pedidos) e 1 do detalhe.
// Sem permissão ou com falha, fica `null`: o favorito do dossiê segue sendo a sugestão.
export function useUltimoPedidoDoCliente(clienteId, contexto, habilitado) {
  const chave = `${clienteId}:${contexto.empresaId}:${contexto.identidade}`
  const [estado, definir] = useState({ chave: null, pedido: null })
  useEffect(() => {
    if (!habilitado || !clienteId) return undefined
    const controle = new AbortController()
    listarPedidosDoCliente(clienteId, contexto, controle.signal)
      .then((itens) => { const alvo = itens.find(pedidoValido); return alvo ? obterPedido(alvo.id, contexto, controle.signal) : null })
      .then((pedido) => { if (!controle.signal.aborted) definir({ chave, pedido }) })
      .catch(() => { if (!controle.signal.aborted) definir({ chave, pedido: null }) })
    return () => controle.abort()
  }, [chave, clienteId, habilitado]) // eslint-disable-line react-hooks/exhaustive-deps
  return estado.chave === chave ? estado.pedido : null
}
