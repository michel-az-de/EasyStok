import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { lerLembretesDaApi } from './api/lembretes'
import { SINCRONIZAR_LEMBRETES_API } from './acoes'
import { aoComecarConsulta, aoConcluirConsulta, aoFalharConsulta } from './situacaoDeConsulta'

export function useLembretesApi({ ativo, despachar, estadoRef }) {
  const [situacao, setSituacao] = useState({ carregando: ativo, erro: null })
  const geracao = useRef(0)
  const vivo = useRef(false)
  const carregar = useCallback(async ({ silenciosa = false } = {}) => {
    if (!ativo) return
    const atual = ++geracao.current
    const revisao = estadoRef.current.revisaoLembretes ?? 0
    setSituacao((s) => aoComecarConsulta(s, { silenciosa }))
    try {
      const lembretes = await lerLembretesDaApi()
      if (!vivo.current || geracao.current !== atual) return
      despachar({ tipo: SINCRONIZAR_LEMBRETES_API, lembretes, revisao })
      setSituacao((s) => aoConcluirConsulta(s))
    } catch (erro) {
      if (vivo.current && geracao.current === atual) setSituacao((s) => aoFalharConsulta(s, erro.message))
    }
  }, [ativo, despachar, estadoRef])

  useEffect(() => {
    if (!ativo) return undefined
    vivo.current = true
    carregar({ silenciosa: true })
    const conferir = () => { if (document.visibilityState === 'visible') carregar({ silenciosa: true }) }
    const intervalo = setInterval(conferir, 15000)
    document.addEventListener('visibilitychange', conferir)
    window.addEventListener('online', conferir)
    return () => {
      vivo.current = false
      geracao.current++
      clearInterval(intervalo)
      document.removeEventListener('visibilitychange', conferir)
      window.removeEventListener('online', conferir)
    }
  }, [ativo, carregar])
  // #1565: o Provider memoiza o contexto com este objeto; novo a cada render, o memo nunca valia.
  return useMemo(() => ({ ...situacao, carregar }), [situacao, carregar])
}