import { useEffect, useRef, useState } from 'react'
import * as api from '../infra/api/fretesApi'
import { useAcessoModulos } from './acessoModulos'

// A intenção conserva a chave depois de falhas de rede; repetir não cria outra despesa/corrida.
export function useFretesApi({ aoAlterado } = {}) {
  const { acoes: capacidades, permite } = useAcessoModulos()
  const [entrada, setEntrada] = useState(null)
  const [contexto, setContexto] = useState(null)
  const [selecionados, setSelecionados] = useState([])
  const [agrupar, setAgrupar] = useState(true)
  const [incluirManual, setIncluirManual] = useState(false)
  const [valorManual, setValorManual] = useState('')
  const [cotacoes, setCotacoes] = useState(null)
  const [escolhida, setEscolhida] = useState(null)
  const [erro, setErro] = useState(null)
  const [aviso, setAviso] = useState(null)
  const [ocupado, setOcupado] = useState(false)
  const trava = useRef(false)
  const chaves = useRef(new Map())
  const geracao = useRef(0)
  useEffect(() => () => { geracao.current += 1 }, [])

  const executar = async (acao, mensagem) => {
    if (trava.current) return
    trava.current = true
    setOcupado(true)
    setErro(null)
    setAviso(null)
    const atual = geracao.current
    try {
      await acao()
      if (atual === geracao.current && mensagem) setAviso(mensagem)
    } catch (e) {
      if (atual === geracao.current) setErro(e.message)
    } finally {
      trava.current = false
      if (atual === geracao.current) setOcupado(false)
    }
  }
  const carregar = async (origem = entrada) => {
    const atual = geracao.current
    const dados = await api.obterContextoFrete(origem)
    if (atual === geracao.current) setContexto(dados)
    return dados
  }
  const abrir = (origem) => {
    if (trava.current) return
    geracao.current += 1
    setEntrada(origem)
    setContexto(null)
    setSelecionados(origem.selecionarTodos === false ? [] : origem.pedidos.map((p) => p.id))
    setCotacoes(null)
    setEscolhida(null)
    setAgrupar(true)
    setIncluirManual(false)
    setValorManual('')
    executar(() => carregar(origem))
  }
  const fechar = () => { if (!trava.current) { geracao.current += 1; setEntrada(null) } }
  const chave = (intencao) => {
    if (!chaves.current.has(intencao)) chaves.current.set(intencao, crypto.randomUUID())
    return chaves.current.get(intencao)
  }
  const atualizarDespacho = (despacho) => {
    setContexto((atual) => ({ ...atual, despachos: [despacho, ...(atual?.despachos ?? []).filter((d) => d.id !== despacho.id)] }))
    aoAlterado?.()
  }
  const selecionar = (id) => {
    setSelecionados((ids) => ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id])
    setCotacoes(null)
    setEscolhida(null)
  }
  const mudarAgrupamento = (valor) => { setAgrupar(valor); setCotacoes(null); setEscolhida(null) }
  return {
    entrada, contexto, selecionados, agrupar, incluirManual, valorManual, cotacoes, escolhida, erro, aviso, ocupado,
    podePagar: capacidades.gerenciarCaixa === true,
    podeConfigurar: capacidades.editarAtendimento === true,
    podeAbrirEntregas: permite('entregas'),
    abrir, fechar, selecionar, mudarAgrupamento, escolher: setEscolhida,
    mudarManual: (valor) => { setIncluirManual(valor); setCotacoes(null); setEscolhida(null) },
    mudarValorManual: (valor) => { setValorManual(valor); setCotacoes(null); setEscolhida(null) },
    atualizar: () => executar(() => carregar()),
    cotar: () => executar(async () => {
      setEscolhida(null)
      setCotacoes(await api.cotarFretes({ pedidoIds: selecionados, agrupar, incluirManual,
        valorManualCentavos: incluirManual && valorManual !== '' ? Math.round(Number(valorManual) * 100) : null }))
    }),
    contratar: () => executar(async () => {
      if (!escolhida) return
      const despacho = await api.contratarFrete({ cotacaoId: escolhida.id, chaveIdempotencia: chave(`contratar:${escolhida.id}`) })
      atualizarDespacho(despacho)
      setEscolhida(null)
      setCotacoes(null)
    }, 'Contratação registrada. Acompanhe a entrega abaixo.'),
    automatico: () => executar(async () => {
      atualizarDespacho(await api.contratarAutomatico({ pedidoIds: selecionados, agrupar,
        chaveIdempotencia: chave(`automatico:${[...selecionados].sort().join(',')}:${agrupar}`) }))
      setCotacoes(null)
      setEscolhida(null)
    }, 'Entrega escolhida dentro das regras salvas. Confira o transporte abaixo.'),
    prepararDia: () => executar(async () => {
      await api.prepararDemonstracao({ dia: entrada.dia })
      await carregar()
      aoAlterado?.()
    }, 'Dia de teste preparado com 10 pedidos.'),
    pagar: (id, meio, referencia) => executar(async () => {
      atualizarDespacho(await api.pagarFrete(id, { meio, referencia, chaveIdempotencia: chave(`pagar:${id}:${meio}:${referencia}`) }))
    }, 'Pagamento registrado no caixa.'),
    evento: (id, tipo, motivo) => executar(async () => {
      atualizarDespacho(await api.registrarEventoFrete(id, { tipo, motivo, chaveIdempotencia: chave(`evento:${id}:${tipo}:${motivo}`) }))
    }, 'Ocorrência registrada.'),
    salvarPolitica: (politica) => executar(async () => {
      await api.salvarPoliticaFrete(politica)
      await carregar()
    }, 'Preferências de contratação salvas.'),
  }
}
