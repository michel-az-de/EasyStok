import { useCallback, useEffect, useRef, useState } from 'react'
import { lerLembretesDaApi } from './api/lembretes'
import { SINCRONIZAR_LEMBRETES_API } from './acoes'

export function useLembretesApi({ ativo, despachar, estadoRef }) {
  const [situacao, setSituacao] = useState({ carregando: ativo, erro: null })
  const geracao = useRef(0)
  const vivo = useRef(false)
  const carregar = useCallback(async () => {
    if (!ativo) return
    const atual = ++geracao.current
    const revisao = estadoRef.current.revisaoLembretes ?? 0
    setSituacao((s) => ({ ...s, carregando: true }))
    try {
      const lembretes = await lerLembretesDaApi()
      if (!vivo.current || geracao.current !== atual) return
      despachar({ tipo: SINCRONIZAR_LEMBRETES_API, lembretes, revisao })
      setSituacao({ carregando: false, erro: null })
    } catch (erro) {
      if (vivo.current && geracao.current === atual) setSituacao({ carregando: false, erro: erro.message })
    }
  }, [ativo, despachar, estadoRef])

  useEffect(() => {
    if (!ativo) return undefined
    vivo.current = true
    carregar()
    const conferir = () => { if (document.visibilityState === 'visible') carregar() }
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
  return { ...situacao, carregar }
}