import { useEffect, useRef } from 'react'
import * as acao from './acoes'
import { listarTodasConversas, listarMensagens } from '../infra/api/conversasApi'
import { conversaDaApi } from '../infra/api/traducaoConversas'
import { obterPedido, pedidoDaApi } from '../infra/api/comandaApi'
import { lerAlertasDeEstoque, lerCardapio } from './api/cardapio'
import { avisoDoCardapio, deveRelerMensagens, deveRelerPedido, quedaDaSincronizacao } from './planoDeSincronizacao'

// Polling da inbox (F01): não existe SSE de conversas ainda (S18). A lista vem a
// cada ciclo, paginada (F07); o que mais se relê está em `planoDeSincronizacao.js`:
// a conversa aberta sempre, as outras só quando mudaram.
//
// F03: o pedido da conversa (`pedidoEmAndamentoId`) vem junto. O cardápio da vitrine
// vem ao entrar e a cada minuto (saldo).
const INTERVALO_MS = 5000
const CICLOS_POR_MINUTO = 12

export function useSincronizacaoApi({ ativo, comConversas = true, usuario, despachar, selecionadaId = null }) {
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
      if (!deveRelerMensagens(resumo, guardado, selecionadaRef.current)) return guardado?.mensagens ?? []
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

    // #1241: com os itens tirados (Repor) e os alertas de produto vendido sem saldo.
    // #1474: o aviso do cardápio (loja online desligada) é estado de configuração, não de ação:
    // vai como persistente (trocar de tela não o apaga) e sai sozinho quando o cardápio volta.
    let avisouCardapio = false
    async function cardapio() {
      try {
        const itens = await lerCardapio({ incluirFora: comConversas })
        if (!vivo) return
        despachar({ tipo: acao.SINCRONIZAR_CARDAPIO, cardapio: itens })
        if (avisouCardapio) {
          avisouCardapio = false
          despachar({ tipo: acao.AVISO_API, mensagem: null, persistente: true })
        }
      } catch (erro) {
        if (!vivo) return
        avisouCardapio = true
        despachar({ tipo: acao.AVISO_API, mensagem: avisoDoCardapio(erro), persistente: true })
      }
      const alertas = await lerAlertasDeEstoque()
      if (vivo) despachar({ tipo: acao.ALERTAS_DE_ESTOQUE_DA_API, alertas })
    }

    async function ciclo() {
      const cicloLento = ciclos % CICLOS_POR_MINUTO === 0
      if (cicloLento) await cardapio()
      ciclos += 1
      try {
        const lista = comConversas ? await listarTodasConversas() : []
        const conversas = await Promise.all(lista.map(async (resumo) => {
          const [mensagens, pedido] = await Promise.all([mensagensDe(resumo), pedidoDe(resumo, cicloLento)])
          return { ...conversaDaApi(resumo, mensagens, usuario), pedido: pedidoDaApi(pedido) }
        }))
        if (vivo) {
          despachar({ tipo: acao.SINCRONIZAR_CONVERSAS, conversas })
          // #1241: a volta da rede abre o lançamento do papel (o reducer ignora se já estava online).
          despachar({ tipo: acao.CONEXAO_VOLTOU, agora: Date.now() })
        }
      } catch (erro) {
        if (vivo) {
          despachar({ tipo: acao.SINCRONIZACAO_FALHOU, mensagem: erro.message })
          // #1241: sem rede, a faixa do papel liga e guarda quem estava aberto.
          if (quedaDaSincronizacao(erro)) despachar({ tipo: acao.CONEXAO_CAIU, agora: Date.now() })
        }
      }
      if (vivo) proximo = setTimeout(ciclo, INTERVALO_MS)
    }

    ciclo()
    return () => {
      vivo = false
      clearTimeout(proximo)
    }
  }, [ativo, comConversas, usuario, despachar])
}
