import { useEffect } from 'react'
import * as acao from './acoes'
import { listarConversas, listarMensagens } from '../infra/api/conversasApi'
import { conversaDaApi } from '../infra/api/traducaoConversas'
import { listarCardapio, obterPedido, pedidoDaApi } from '../infra/api/comandaApi'

// Polling da inbox (F01): não existe SSE de conversas ainda (S18). A lista vem a
// cada ciclo; as mensagens só são buscadas de novo quando a conversa mudou
// (`ultimaMensagemEm`), para 30 conversas não virarem 30 chamadas a cada 5 s.
//
// F03: o pedido da conversa (`pedidoEmAndamentoId`) vem junto. Pagamento confirmado pelo
// Mercado Pago não mexe na conversa, então pedido ainda aberto é relido a cada ciclo; pedido
// entregue ou cancelado fica no cache até o id mudar. O cardápio da vitrine vem ao entrar e a
// cada minuto (saldo).
const INTERVALO_MS = 5000
const CICLOS_DO_CARDAPIO = 12
const STATUS_FINAIS = new Set(['entregue', 'cancelado'])

export function useSincronizacaoApi({ ativo, usuario, despachar }) {
  useEffect(() => {
    if (!ativo) return undefined
    let vivo = true
    let proximo = null
    let ciclos = 0
    const cache = new Map()
    const pedidos = new Map()

    async function mensagensDe(resumo) {
      const guardado = cache.get(resumo.id)
      if (guardado && guardado.ultima === resumo.ultimaMensagemEm) return guardado.mensagens
      try {
        const mensagens = await listarMensagens(resumo.id)
        cache.set(resumo.id, { ultima: resumo.ultimaMensagemEm, mensagens })
        return mensagens
      } catch {
        return guardado?.mensagens ?? []
      }
    }

    async function pedidoDe(resumo) {
      if (!resumo.pedidoEmAndamentoId) return null
      const guardado = pedidos.get(resumo.id)
      if (guardado?.pedidoId === resumo.pedidoEmAndamentoId && STATUS_FINAIS.has(guardado.dados?.status)) {
        return guardado.dados
      }
      try {
        const dados = await obterPedido(resumo.id)
        pedidos.set(resumo.id, { pedidoId: resumo.pedidoEmAndamentoId, dados })
        return dados
      } catch {
        return guardado?.dados ?? null
      }
    }

    async function cardapio() {
      try {
        const itens = await listarCardapio()
        if (vivo) despachar({ tipo: acao.SINCRONIZAR_CARDAPIO, cardapio: itens })
      } catch (erro) {
        if (vivo) despachar({ tipo: acao.AVISO_API, mensagem: `Cardápio: ${erro.message}` })
      }
    }

    async function ciclo() {
      if (ciclos % CICLOS_DO_CARDAPIO === 0) await cardapio()
      ciclos += 1
      try {
        const lista = await listarConversas()
        const conversas = await Promise.all(lista.map(async (resumo) => {
          const [mensagens, pedido] = await Promise.all([mensagensDe(resumo), pedidoDe(resumo)])
          return { ...conversaDaApi(resumo, mensagens, usuario), pedido: pedidoDaApi(pedido) }
        }))
        if (vivo) despachar({ tipo: acao.SINCRONIZAR_CONVERSAS, conversas })
      } catch (erro) {
        if (vivo) despachar({ tipo: acao.SINCRONIZACAO_FALHOU, mensagem: erro.message })
      }
      if (vivo) proximo = setTimeout(ciclo, INTERVALO_MS)
    }

    ciclo()
    return () => {
      vivo = false
      clearTimeout(proximo)
    }
  }, [ativo, usuario, despachar])
}
