import { useCallback, useEffect, useRef, useState } from 'react'
import * as api from '../infra/api/entregasApi'
import { conectarEventosOperacao } from '../infra/api/eventosOperacao'
import { STATUS_KDS_ENTREGAS, recarregaEntregasCom } from '../dominio/entregasApi'

// Entregas no modo API (F04). Pedidos e viagens recarregam a cada evento de
// pedido do SSE de operação (S18), o mesmo da cozinha (F05). Com o SSE caído,
// busca a cada 15 s e tenta reconectar a cada 10 s.
const ESPERA_RECARGA_MS = 300
const POLLING_SEM_SSE_MS = 15000
const RECONECTAR_MS = 10000

export function useEntregasApi() {
  const [pedidos, setPedidos] = useState(null)
  const [viagens, setViagens] = useState([])
  const [entregadores, setEntregadores] = useState([])
  const [chamados, setChamados] = useState([])
  const [erro, setErro] = useState(null)
  const [aoVivo, setAoVivo] = useState(false)
  const [ocupado, setOcupado] = useState(false)
  const vivoRef = useRef(true)

  const recarregar = useCallback(() => Promise.all([
    api.listarPedidosEntrega(STATUS_KDS_ENTREGAS),
    api.listarViagens(),
    api.listarEntregadores(),
    api.listarChamados(),
  ])
    .then(([p, v, e, c]) => {
      if (!vivoRef.current) return
      setPedidos(p ?? [])
      setViagens(v ?? [])
      setEntregadores(e ?? [])
      setChamados(c ?? [])
      setErro(null)
    })
    .catch((e) => { if (vivoRef.current) setErro(`As entregas não carregaram: ${e.message}`) }), [])

  useEffect(() => {
    vivoRef.current = true
    let fechar = () => {}
    let espera = null
    let reconexao = null
    let polling = null

    const conectar = () => {
      fechar = conectarEventosOperacao({
        aoEvento: ({ evento }) => {
          if (evento === 'ready') {
            setAoVivo(true)
            clearInterval(polling)
            polling = null
          }
          if (recarregaEntregasCom(evento)) {
            clearTimeout(espera)
            espera = setTimeout(recarregar, ESPERA_RECARGA_MS)
          }
        },
        aoCair: () => {
          if (!vivoRef.current) return
          setAoVivo(false)
          if (!polling) polling = setInterval(recarregar, POLLING_SEM_SSE_MS)
          reconexao = setTimeout(conectar, RECONECTAR_MS)
        },
      })
    }

    recarregar()
    conectar()
    return () => {
      vivoRef.current = false
      fechar()
      clearTimeout(espera)
      clearTimeout(reconexao)
      clearInterval(polling)
    }
  }, [recarregar])

  // Toda ação: trava os botões, chama a API, recarrega. O erro da API vira a faixa.
  const executar = useCallback((chamada, falha) => {
    setOcupado(true)
    return chamada()
      .then(() => recarregar())
      .catch((e) => setErro(`${falha}: ${e.message}`))
      .finally(() => setOcupado(false))
  }, [recarregar])

  const acoes = {
    criarViagem: (entregadorId) => executar(() => api.criarViagem(entregadorId), 'A viagem não foi criada'),
    definirEntregador: (id, entregadorId) => executar(() => api.definirEntregadorViagem(id, entregadorId), 'O entregador não mudou'),
    incluirParada: (id, pedidoId) => executar(() => api.incluirParada(id, pedidoId), 'O pedido não entrou na viagem'),
    retirarParada: (id, pedidoId) => executar(() => api.retirarParada(id, pedidoId), 'O pedido não saiu da viagem'),
    reordenar: (id, pedidoId, ordem) => executar(() => api.reordenarParada(id, pedidoId, ordem), 'A ordem não mudou'),
    sair: (id) => executar(() => api.sairParaEntrega(id), 'A viagem não saiu'),
    entregue: (id, pedidoId) => executar(() => api.marcarParadaEntregue(id, pedidoId), 'A entrega não foi marcada'),
    desfazer: (id) => executar(() => api.desfazerViagem(id), 'A viagem não foi desfeita'),
    criarEntregador: (corpo) => executar(() => api.criarEntregador(corpo), 'O entregador não foi cadastrado'),
    desativarEntregador: (id) => executar(() => api.desativarEntregador(id), 'O entregador não foi desativado'),
    abrirChamado: (texto, viagemId) => executar(() => api.abrirChamado(texto, viagemId), 'O chamado não foi aberto'),
    atenderChamado: (id) => executar(() => api.atenderChamado(id), 'O chamado não mudou'),
    cancelarChamado: (id) => executar(() => api.cancelarChamado(id), 'O chamado não mudou'),
    aprovar: (id) => executar(() => api.aprovarPedido(id), 'O pedido não foi aprovado'),
    recusar: (id) => executar(() => api.recusarPedido(id, 'operacional'), 'O pedido não foi recusado'),
  }

  return { pedidos, viagens, entregadores, chamados, erro, aoVivo, ocupado, acoes, limparErro: () => setErro(null) }
}
