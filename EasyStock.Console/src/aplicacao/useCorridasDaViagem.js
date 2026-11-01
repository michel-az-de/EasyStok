import { useCallback, useEffect, useRef, useState } from 'react'
import * as api from '../infra/api/entregasApi'
import { INTERVALO_CORRIDA_MS, corridaEmCurso } from '../dominio/entregasApi'

// Corridas externas de uma viagem: a anotada à mão com o custo (I9.2, #1691) e a chamada pela API da
// Lalamove (#2162). Lê ao montar e depois de cada gravação, dê certo ou não (#2168: a chamada que falhou pode
// ter criado a corrida lá); um envio por vez. Com corrida em curso, reconsulta a cada 15 s com a aba visível,
// sem empilhar leituras: o GET da corrida faz o servidor reler a Lalamove e aplicar o status.
export function useCorridasDaViagem(viagemId) {
  const [corridas, setCorridas] = useState(null)
  const [conectada, setConectada] = useState(false)
  const [erro, setErro] = useState(null)
  const [erroLeitura, setErroLeitura] = useState(null)
  const [enviando, setEnviando] = useState(false)
  const [atualizadaEm, setAtualizadaEm] = useState(null)
  const [semResposta, setSemResposta] = useState(false)
  const enviandoRef = useRef(false)
  const lendoRef = useRef(false)
  const leituraRef = useRef(0)
  const vivoRef = useRef(true)

  useEffect(() => {
    vivoRef.current = true
    return () => { vivoRef.current = false }
  }, [])

  // A leitura mais nova vence: resposta atrasada de uma leitura anterior não sobrescreve a tela.
  const recarregar = useCallback(() => {
    const minha = ++leituraRef.current
    return api.listarCorridas(viagemId)
      .then((lista) => {
        if (!vivoRef.current || minha !== leituraRef.current) return
        setCorridas(lista ?? [])
        setAtualizadaEm(Date.now())
        setErroLeitura(null)
      })
      .catch((e) => { if (vivoRef.current && minha === leituraRef.current) setErroLeitura(`As corridas não carregaram: ${e.message}`) })
  }, [viagemId])

  useEffect(() => { recarregar() }, [recarregar])

  // Sem conexão o botão some; falha da consulta vale "sem conexão" (a tela não oferece o que não existe).
  useEffect(() => {
    api.conexaoDespacho().then((r) => { if (vivoRef.current) setConectada(r?.lalamoveConectada === true) }, () => {})
  }, [])

  const atualizarAgora = useCallback(async () => {
    if (lendoRef.current || enviandoRef.current) return
    lendoRef.current = true
    try {
      const ativas = (corridas ?? []).filter((c) => c.origem === 'Api' && c.ativa)
      const respostas = await Promise.all(ativas.map((c) => api.atualizarCorrida(viagemId, c.id).then(() => true, () => false)))
      if (vivoRef.current) setSemResposta(respostas.includes(false))
      await recarregar()
    } finally {
      lendoRef.current = false
    }
  }, [corridas, viagemId, recarregar])

  const emCurso = corridaEmCurso(corridas)
  useEffect(() => {
    if (!emCurso) return undefined
    const relogio = setInterval(() => { if (document.visibilityState !== 'hidden') atualizarAgora() }, INTERVALO_CORRIDA_MS)
    const aoVoltar = () => { if (document.visibilityState === 'visible') atualizarAgora() }
    document.addEventListener('visibilitychange', aoVoltar)
    return () => {
      clearInterval(relogio)
      document.removeEventListener('visibilitychange', aoVoltar)
    }
  }, [emCurso, atualizarAgora])

  // Devolve o resultado (ou true) se deu certo e null se falhou: o formulário só fecha quando a API aceitou.
  // A mensagem da API já diz o que fazer; o prefixo só entra quando ela não explica (erro de rede).
  const gravar = async (chamada, falha) => {
    if (enviandoRef.current) return null
    enviandoRef.current = true
    setEnviando(true)
    setErro(null)
    try {
      return (await chamada()) ?? true
    } catch (e) {
      setErro(e.status >= 400 && e.status < 500 ? e.message : `${falha}: ${e.message}`)
      return null
    } finally {
      await recarregar()
      enviandoRef.current = false
      if (vivoRef.current) setEnviando(false)
    }
  }

  return {
    corridas,
    conectada,
    erro,
    erroLeitura,
    enviando,
    atualizadaEm,
    semResposta,
    recarregar,
    atualizarAgora,
    registrar: (corpo) => gravar(() => api.registrarCorrida(viagemId, corpo), 'A corrida não foi registrada'),
    corrigir: (corridaId, corpo) => gravar(() => api.corrigirCorrida(viagemId, corridaId, corpo), 'A corrida não foi corrigida'),
    cotar: (veiculo) => gravar(() => api.cotarCorrida(viagemId, veiculo), 'A Lalamove não cotou'),
    chamar: (corpo) => gravar(() => api.chamarCorrida(viagemId, corpo), 'A corrida não foi chamada'),
    cancelar: (corridaId) => gravar(() => api.cancelarCorrida(viagemId, corridaId), 'A corrida não foi cancelada'),
    gorjeta: (corridaId, centavos) => gravar(() => api.gorjetaCorrida(viagemId, corridaId, centavos), 'A gorjeta não entrou'),
    liberar: (corridaId) => gravar(() => api.liberarCorrida(viagemId, corridaId), 'A viagem não foi liberada'),
  }
}
