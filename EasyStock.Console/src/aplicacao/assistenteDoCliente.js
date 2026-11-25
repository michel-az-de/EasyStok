import { useEffect, useState } from 'react'
import { perguntarAssistente } from '../infra/api/assistenteApi'
import { listarPedidosDoCliente, obterPedido } from './pedidosCadastro'
import { pedidoValido } from '../dominio/pratosParaOferecer'

// #3344: o Assistente age na Ficha só no clique da atendente, com a conversa mais recente do cliente quando há (a API
// aceita sem conversa). A resposta é só texto para revisar; nada é enviado ao cliente e as ações propostas não rodam aqui.
export async function perguntarSobreOCliente(pergunta, conversaId, { sinal } = {}) {
  const { texto } = await perguntarAssistente(pergunta, conversaId ?? null, { sinal })
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
