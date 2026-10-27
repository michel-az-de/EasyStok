import { useCallback, useEffect, useRef, useState } from 'react'
import * as api from '../infra/api/wppConnectApi'

export function useWppConnectApi() {
  const [sessao, setSessao] = useState(null)
  const [erro, setErro] = useState(null)
  const [ocupado, setOcupado] = useState(false)
  const ativo = useRef(false)
  const emVoo = useRef(false)

  const atualizar = useCallback(async () => {
    if (emVoo.current) return
    emVoo.current = true
    try {
      const estado = await api.obterWppConnect()
      if (ativo.current) { setSessao(estado); setErro(null) }
    } catch (e) {
      if (ativo.current) setErro(e.message)
    } finally { emVoo.current = false }
  }, [])

  useEffect(() => {
    ativo.current = true
    atualizar()
    return () => { ativo.current = false }
  }, [atualizar])

  useEffect(() => {
    if (!sessao?.vinculado) return
    const timer = setInterval(atualizar, sessao.conectado ? 15000 : 3000)
    return () => clearInterval(timer)
  }, [sessao?.vinculado, sessao?.conectado, atualizar])

  const executar = useCallback(async (acao) => {
    setOcupado(true)
    setErro(null)
    try {
      const estado = await acao()
      if (ativo.current) setSessao(estado)
    } catch (e) {
      if (ativo.current) setErro(e.message)
    } finally { if (ativo.current) setOcupado(false) }
  }, [])

  return { sessao, erro, ocupado, atualizar,
    conectar: () => executar(api.conectarWppConnect),
    reconectar: () => executar(api.reconectarWppConnect),
    desconectar: () => executar(api.desconectarWppConnect) }
}
