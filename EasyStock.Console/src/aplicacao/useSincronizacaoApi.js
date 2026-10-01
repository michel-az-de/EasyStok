import { useEffect, useRef } from 'react'
import * as acao from './acoes'
import { listarTodasConversas, listarMensagens } from '../infra/api/conversasApi'
import { conversaDaApi } from '../infra/api/traducaoConversas'
import { listarCardapio, obterPedido, pedidoDaApi } from '../infra/api/comandaApi'
import { deveRelerMensagens, deveRelerPedido } from './planoDeSincronizacao'

// Polling da inbox (F01): não existe SSE de conversas ainda (S18). A lista vem a
// cada ciclo, paginada (F07); o que mais se relê está em `planoDeSincronizacao.js`:
// a conversa aberta sempre, as outras só quando mudaram.
//
// F03: o pedido da conversa (`pedidoEmAndamentoId`) vem junto. O cardápio da vitrine
// vem ao entrar e a cada minuto (saldo).
const INTERVALO_MS = 5000
const CICLOS_POR_MINUTO = 12

export function useSincronizacaoApi({ ativo, usuario, despachar, selecionadaId = null }) {
  // A seleção muda a cada clique; num ref, o laço não recomeça por causa dela.
  const selecionadaRef = useRef(selecionadaId)
  useEffect(() => { selecionadaRef.current = selecionadaId }, [selecionadaId])

  useEffect(() => {
    if (!ativo) return undefined
    let vivo = true
    let proximo = null
    let ciclos = 0
    const cache = new Map()
    const pedidos = new Map()

    async function mensagensDe(resumo) {
      const guardado = cache.get(resumo.id)
      if (!deveRelerMensagens(resumo, guardado, selecionadaRef.current)) return guardado.mensagens
      try {
        const mensagens = await listarMensagens(resumo.id)
        cache.set(resumo.id, { ultima: resumo.ultimaMensagemEm, mensagens })
        return mensagens
      } catch {
        return guardado?.mensagens ?? []
      }
    }

    async function pedidoDe(resumo, cicloLento) {
      if (!resumo.pedidoEmAndamentoId) return null
      const guardado = pedidos.get(resumo.id)
      if (!deveRelerPedido(resumo, guardado, { selecionadaId: selecionadaRef.current, cicloLento })) {
        return guardado.dados
      }
      try {
        const dados = await obterPedido(resumo.id)
        pedidos.set(resumo.id, { pedidoId: resumo.pedidoEmAndamentoId, ultima: resumo.ultimaMensagemEm, dados })
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
      const cicloLento = ciclos % CICLOS_POR_MINUTO === 0
      if (cicloLento) await cardapio()
      ciclos += 1
      try {
        const lista = await listarTodasConversas()
        const conversas = await Promise.all(lista.map(async (resumo) => {
          const [mensagens, pedido] = await Promise.all([mensagensDe(resumo), pedidoDe(resumo, cicloLento)])
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
