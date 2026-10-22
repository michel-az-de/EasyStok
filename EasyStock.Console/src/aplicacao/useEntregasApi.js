import { useCallback, useEffect, useRef, useState } from 'react'
import * as api from '../infra/api/entregasApi'
import { conectarEventosOperacao } from '../infra/api/eventosOperacao'
import {
  STATUS_KDS_DIA, STATUS_KDS_ENTREGAS, recarregaEntregasCom, releCadastroDeEntregas,
} from '../dominio/entregasApi'
import { dataIsoNoFuso } from '../dominio/formato'
import { pedeConfirmacaoDoSaldo } from '../dominio/pagamento'
import { criarRecargaDeAcao } from './recargaDeAcao'
import { chaveDaEntrega, chaveDaSaida, criarEnvioComDesfazer } from './envioComDesfazer'

// Entregas no modo API (F04). Pedidos e viagens recarregam a cada evento de
// pedido do SSE de operação (S18), o mesmo da cozinha (F05). Com o SSE caído,
// busca a cada 15 s e tenta reconectar a cada 10 s.
const POLLING_SEM_SSE_MS = 15000
const RECONECTAR_MS = 10000

// #1577: a recarga por evento, por polling e por ação de viagem relê só a operação (4 GETs);
// o cadastro (mais 3) volta quando `releCadastroDeEntregas` manda.
const SO_OPERACAO = 'operacao'
// O que cada ação relê depois de aceita, conferido nos use cases da API. A saída e a entrega
// publicam `pedido.mudou_status` no SSE: com ele vivo, quem relê é o evento.
const RELE_PELO_EVENTO = 'evento'
// Entregador novo ou desativado mexe no cadastro. As demais (viagem sem mudar pedido, chamado,
// aprovação) não saem no SSE e releem a operação na resposta.
const RELE_TUDO = 'tudo'

const lerCadastro = (data) => Promise.all([
  api.listarEntregadores(),
  api.listarJanelas().catch(() => null),
  api.listarBloqueios(data, data).catch(() => null),
]).then(([entregadores, janelas, bloqueios]) => ({
  dia: data, lidoEm: Date.now(), entregadores: entregadores ?? [], janelas, bloqueios,
}))

export function useEntregasApi() {
  const [pedidos, setPedidos] = useState(null)
  const [viagens, setViagens] = useState([])
  const [entregadores, setEntregadores] = useState([])
  const [chamados, setChamados] = useState([])
  const [erro, setErro] = useState(null)
  const [aoVivo, setAoVivo] = useState(false)
  const [ocupado, setOcupado] = useState(false)
  const vivoRef = useRef(true)
  // #1440: o dia do roteiro (hoje por padrão; amanhã para preparar a véspera). Pedidos do dia
  // em todos os status, janelas e bloqueios da S45 (Admin: sem permissão, a tela agrupa só
  // pelo que vem no pedido).
  const [dia, setDia] = useState(() => dataIsoNoFuso(Date.now()))
  const diaRef = useRef(dia)
  // #1510: enquanto ela não escolhe outro dia, "hoje" é resolvido a cada recarga; o tablet
  // aberto de um dia para o outro não fica mostrando o roteiro de ontem.
  const seguindoHojeRef = useRef(true)
  const [doDia, setDoDia] = useState(null)
  const cadastroRef = useRef(null)
  const recargaRef = useRef(null)
  // #1686 (item 5): "Saiu para entrega" e "Entregue" avisam o cliente. O toque espera alguns
  // segundos com "Desfazer" à vista; sair da tela com envio pendente manda na hora.
  const [adiados, setAdiados] = useState(() => new Map())
  const [envio] = useState(() => criarEnvioComDesfazer({
    aoMudar: (lista) => setAdiados(new Map(lista.map((a) => [a.chave, a.ate]))),
  }))
  useEffect(() => () => envio.descarregar(), [envio])

  const recarregar = useCallback((alcance) => {
    const hoje = dataIsoNoFuso(Date.now())
    if (seguindoHojeRef.current && diaRef.current !== hoje) {
      diaRef.current = hoje
      setDia(hoje)
      setDoDia(null)
    }
    const data = diaRef.current
    const comCadastro = releCadastroDeEntregas({
      soOperacao: alcance === SO_OPERACAO, dia: data, cadastro: cadastroRef.current, agora: Date.now(),
    })
    return Promise.all([
      api.listarPedidosEntrega(STATUS_KDS_ENTREGAS),
      api.listarViagens(),
      api.listarChamados(),
      api.listarPedidosEntrega(STATUS_KDS_DIA, data),
      comCadastro ? lerCadastro(data) : null,
    ])
      .then(([p, v, c, pedidosDoDia, lido]) => {
        if (!vivoRef.current) return
        if (lido) cadastroRef.current = lido
        // Lido só aqui: uma leitura inteira pode ter chegado enquanto esta, só de operação, viajava.
        const cadastro = cadastroRef.current
        setPedidos(p ?? [])
        setViagens(v ?? [])
        setEntregadores(cadastro.entregadores)
        setChamados(c ?? [])
        if (diaRef.current === data && cadastro.dia === data) {
          setDoDia({ data, pedidos: pedidosDoDia ?? [], janelas: cadastro.janelas, bloqueios: cadastro.bloqueios })
        }
        setErro(null)
      })
      .catch((e) => { if (vivoRef.current) setErro(`As entregas não carregaram: ${e.message}`) })
  }, [])

  const mudarDia = useCallback((data) => {
    if (!data) return
    seguindoHojeRef.current = data === dataIsoNoFuso(Date.now())
    if (data === diaRef.current) return
    diaRef.current = data
    setDia(data)
    setDoDia(null)
    recarregar()
  }, [recarregar])

  useEffect(() => {
    vivoRef.current = true
    let fechar = () => {}
    let reconexao = null
    let polling = null
    let noAr = false
    const recarga = criarRecargaDeAcao({
      recarregar: (inteira) => recarregar(inteira ? undefined : SO_OPERACAO),
      aoVivo: () => noAr,
    })
    recargaRef.current = recarga

    const conectar = () => {
      fechar = conectarEventosOperacao({
        aoEvento: ({ evento }) => {
          if (evento === 'ready') {
            noAr = true
            setAoVivo(true)
            clearInterval(polling)
            polling = null
          }
          if (recarregaEntregasCom(evento)) recarga.aoEvento(evento === 'ready')
        },
        aoCair: () => {
          if (!vivoRef.current) return
          noAr = false
          setAoVivo(false)
          if (!polling) polling = setInterval(() => recarregar(SO_OPERACAO), POLLING_SEM_SSE_MS)
          reconexao = setTimeout(conectar, RECONECTAR_MS)
        },
      })
    }

    recarregar()
    conectar()
    return () => {
      vivoRef.current = false
      fechar()
      recarga.desligar()
      clearTimeout(reconexao)
      clearInterval(polling)
    }
  }, [recarregar])

  // Toda ação: trava os botões, chama a API e só solta com a tela relida. O erro da API vira a faixa.
  // #1776 (item 19): a recusa que só pede confirmação (saldo a receber) não é erro: volta para a tela
  // perguntar, como `{ confirmar: valor }`.
  const executar = useCallback((chamada, falha, rele, pedeConfirmacao = null) => {
    setOcupado(true)
    const recarga = recargaRef.current
    const marca = recarga.marcar()
    return chamada()
      .then(async () => {
        if (rele === RELE_PELO_EVENTO) await recarga.depoisDaAcao(marca)
        else await recarregar(rele === RELE_TUDO ? undefined : SO_OPERACAO)
        return true
      })
      .catch((e) => {
        if (pedeConfirmacao?.(e)) return { confirmar: Number(e.dados?.valor) || 0 }
        setErro(`${falha}: ${e.message}`)
        return false
      })
      .finally(() => setOcupado(false))
  }, [recarregar])

  const acoes = {
    // Pedido de quem está na tela (botão, reagendamento): a leitura inteira.
    recarregar: () => recarregar(),
    criarViagem: (entregadorId) => executar(() => api.criarViagem(entregadorId), 'A viagem não foi criada'),
    definirEntregador: (id, entregadorId) => executar(() => api.definirEntregadorViagem(id, entregadorId), 'O entregador não mudou'),
    incluirParada: (id, pedidoId) => executar(() => api.incluirParada(id, pedidoId), 'O pedido não entrou na viagem'),
    retirarParada: (id, pedidoId) => executar(() => api.retirarParada(id, pedidoId), 'O pedido não saiu da viagem'),
    reordenar: (id, pedidoId, ordem) => executar(() => api.reordenarParada(id, pedidoId, ordem), 'A ordem não mudou'),
    sair: (id) => envio.agendar(chaveDaSaida(id),
      () => executar(() => api.sairParaEntrega(id), 'A viagem não saiu', RELE_PELO_EVENTO)),
    entregue: (id, pedidoId, confirmarSaldo = false) => envio.agendar(chaveDaEntrega(id, pedidoId),
      () => executar(() => api.marcarParadaEntregue(id, pedidoId, confirmarSaldo),
        'A entrega não foi marcada', RELE_PELO_EVENTO, pedeConfirmacaoDoSaldo)),
    adiadoAte: (chave) => adiados.get(chave) ?? null,
    desfazerEnvio: (chave) => envio.desfazer(chave),
    desfazer: (id) => executar(() => api.desfazerViagem(id), 'A viagem não foi desfeita'),
    criarEntregador: (corpo) => executar(() => api.criarEntregador(corpo), 'O entregador não foi cadastrado', RELE_TUDO),
    desativarEntregador: (id) => executar(() => api.desativarEntregador(id), 'O entregador não foi desativado', RELE_TUDO),
    abrirChamado: (texto, viagemId) => executar(() => api.abrirChamado(texto, viagemId), 'O chamado não foi aberto'),
    atenderChamado: (id) => executar(() => api.atenderChamado(id), 'O chamado não mudou'),
    cancelarChamado: (id) => executar(() => api.cancelarChamado(id), 'O chamado não mudou'),
    aprovar: (id) => executar(() => api.aprovarPedido(id), 'O pedido não foi aprovado'),
    recusar: (id) => executar(() => api.recusarPedido(id, 'operacional'), 'O pedido não foi recusado'),
  }

  return {
    pedidos, viagens, entregadores, chamados, erro, aoVivo, ocupado, acoes, limparErro: () => setErro(null),
    dia, mudarDia, doDia,
  }
}
