import { useCallback, useEffect, useRef, useState } from 'react'
import * as api from '../infra/api/entregasApi'
import { INTERVALO_CORRIDA_MS, corridaEmCurso } from '../dominio/entregasApi'

// Corridas externas de uma viagem: a anotada à mão com o custo (I9.2, #1691) e a chamada pela API da
// Lalamove (#2162). Lê ao montar e depois de cada gravação; um envio por vez. Com corrida em curso,
// reconsulta a cada 15 s: o GET da corrida faz o servidor reler a Lalamove e aplicar o status.
export function useCorridasDaViagem(viagemId) {
  const [corridas, setCorridas] = useState(null)
  const [conectada, setConectada] = useState(false)
  const [erro, setErro] = useState(null)
  const [enviando, setEnviando] = useState(false)
  const enviandoRef = useRef(false)

  const recarregar = useCallback(
    () => api.listarCorridas(viagemId)
      .then((lista) => { setCorridas(lista ?? []); setErro(null) })
      .catch((e) => setErro(`As corridas não carregaram: ${e.message}`)),
    [viagemId],
  )

  useEffect(() => { recarregar() }, [recarregar])

  // Sem conexão o botão some; falha da consulta vale "sem conexão" (a tela não oferece o que não existe).
  useEffect(() => {
    let vivo = true
    api.conexaoDespacho().then((r) => { if (vivo) setConectada(r?.lalamoveConectada === true) }, () => {})
    return () => { vivo = false }
  }, [])

  const emCurso = corridaEmCurso(corridas)
  useEffect(() => {
    if (!emCurso) return undefined
    const relogio = setInterval(() => {
      const ativas = (corridas ?? []).filter((c) => c.origem === 'Api' && c.ativa)
      Promise.all(ativas.map((c) => api.atualizarCorrida(viagemId, c.id).catch(() => null))).then(() => recarregar())
    }, INTERVALO_CORRIDA_MS)
    return () => clearInterval(relogio)
  }, [emCurso, corridas, viagemId, recarregar])

  // Devolve o resultado (ou true) se deu certo e null se falhou: o formulário só fecha quando a API aceitou.
  const gravar = async (chamada, falha) => {
    if (enviandoRef.current) return null
    enviandoRef.current = true
    setEnviando(true)
    try {
      const resultado = await chamada()
      await recarregar()
      return resultado ?? true
    } catch (e) {
      setErro(`${falha}: ${e.message}`)
      return null
    } finally {
      enviandoRef.current = false
      setEnviando(false)
    }
  }

  return {
    corridas,
    conectada,
    erro,
    enviando,
    registrar: (corpo) => gravar(() => api.registrarCorrida(viagemId, corpo), 'A corrida não foi registrada'),
    corrigir: (corridaId, corpo) => gravar(() => api.corrigirCorrida(viagemId, corridaId, corpo), 'A corrida não foi corrigida'),
    cotar: (veiculo) => gravar(() => api.cotarCorrida(viagemId, veiculo), 'A Lalamove não cotou'),
    chamar: (corpo) => gravar(() => api.chamarCorrida(viagemId, corpo), 'A corrida não foi chamada'),
    cancelar: (corridaId) => gravar(() => api.cancelarCorrida(viagemId, corridaId), 'A corrida não foi cancelada'),
    gorjeta: (corridaId, centavos) => gravar(() => api.gorjetaCorrida(viagemId, corridaId, centavos), 'A gorjeta não entrou'),
  }
}
