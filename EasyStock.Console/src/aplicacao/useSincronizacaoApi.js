import { useEffect, useRef } from 'react'
import * as acao from './acoes'
import { lerAlertasDeEstoque, lerCardapio } from './api/cardapio'
import { avisoDoCardapio } from './planoDeSincronizacao'
import { criarCicloDeSincronizacao, criarMemoriaDoLaco } from './cicloDeSincronizacao'
import { conectarEventosOperacao } from '../infra/api/eventosOperacao'

// Polling da inbox (F01), com releitura dirigida pelos eventos de pedido (S18).
// A volta está em `cicloDeSincronizacao.js` e o que ela relê em
// `planoDeSincronizacao.js`: a conversa aberta sempre, as outras só quando mudaram.
// #1576: a inbox inteira (F07) vem ao entrar e a cada minuto; as outras voltas leem a página 1.
//
// F03: o pedido da conversa (`pedidoEmAndamentoId`) vem junto. O cardápio da vitrine
// vem ao entrar e a cada minuto (saldo).
const INTERVALO_MS = 5000
const CICLOS_POR_MINUTO = 12
const AGRUPAR_EVENTOS_MS = 300

// `memoria` (#1576): os caches do laço, criados acima do Balcão para a volta do Hall, da Cozinha ou
// das Entregas não reler tudo. Sem ela (demonstração, provas) o laço usa a sua, que morre com ele.
export function useSincronizacaoApi({ ativo, comConversas = true, usuario, despachar, selecionadaId = null, memoria = null }) {
  // A seleção muda a cada clique; num ref, o laço não recomeça por causa dela.
  const selecionadaRef = useRef(selecionadaId)
  useEffect(() => { selecionadaRef.current = selecionadaId }, [selecionadaId])
  // O refresh do token recria o objeto do usuário, mesma pessoa. Num ref o laço não recomeça: recomeçar
  // refaz a varredura da inbox e relê o pedido aberto de toda conversa.
  const usuarioRef = useRef(usuario)
  useEffect(() => { usuarioRef.current = usuario }, [usuario])
  const usuarioId = usuario?.id ?? null

  useEffect(() => {
    if (!ativo) return undefined
    let vivo = true
    let proximo = null
    let ciclos = 0
    let lendo = false
    let pedidosPendentes = false
    let voltaPendente = false
    let esperaPedidos = null
    let reconexao = null
    let fecharEventos = () => {}
    const sincronizar = criarCicloDeSincronizacao({
      despachar, comConversas, memoria: memoria ?? criarMemoriaDoLaco(), dono: usuarioId,
      lerSelecionada: () => selecionadaRef.current, lerUsuario: () => usuarioRef.current, estaVivo: () => vivo,
    })

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

    async function ciclo(somentePedidos = false) {
      if (!vivo) return
      if (lendo) {
        if (somentePedidos) pedidosPendentes = true
        else voltaPendente = true
        return
      }
      if (!somentePedidos) clearTimeout(proximo)
      clearTimeout(esperaPedidos)
      lendo = true
      const cicloLento = !somentePedidos && ciclos % CICLOS_POR_MINUTO === 0
      if (!somentePedidos) {
        if (cicloLento) await cardapio()
        ciclos += 1
      }
      await sincronizar({ cicloLento, somentePedidos })
      lendo = false
      if (!vivo) return
      if (!somentePedidos) proximo = setTimeout(ciclo, INTERVALO_MS)
      if (voltaPendente) {
        voltaPendente = false
        pedidosPendentes = false
        clearTimeout(proximo)
        proximo = setTimeout(ciclo, 0)
      } else if (pedidosPendentes) {
        pedidosPendentes = false
        esperaPedidos = setTimeout(() => ciclo(true), AGRUPAR_EVENTOS_MS)
      }
    }

    function conectar() {
      fecharEventos = conectarEventosOperacao({
        aoEvento: ({ evento, dados }) => {
          if (!vivo || (evento !== 'ready' && !(evento?.startsWith('pedido.') && dados?.pedidoId))) return
          if (!sincronizar.invalidarPedidos(evento === 'ready' ? null : dados.pedidoId)) return
          if (lendo) { pedidosPendentes = true; return }
          clearTimeout(esperaPedidos)
          esperaPedidos = setTimeout(() => ciclo(true), AGRUPAR_EVENTOS_MS)
        },
        aoCair: () => { if (vivo) reconexao = setTimeout(conectar, INTERVALO_MS) },
      })
    }

    ciclo()
    if (comConversas) conectar()
    return () => {
      vivo = false
      clearTimeout(proximo)
      clearTimeout(esperaPedidos)
      clearTimeout(reconexao)
      fecharEventos()
    }
  }, [ativo, comConversas, usuarioId, despachar, memoria])
}
