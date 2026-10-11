import { useCallback, useEffect, useRef, useState } from 'react'
import { carregarNotificacoesDaSessao, lerNotificacaoDaSessao } from '../infra/api/notificacoesDaSessao'
import { aoComecarConsulta, aoConcluirConsulta, aoFalharConsulta } from './situacaoDeConsulta'

export function useNotificacoesDaSessao() {
  const [estado, setEstado] = useState({ total: null, recentes: [], erro: null, carregando: true, lendo: null })
  const ativo = useRef(false)
  const geracao = useRef(0)
  const carregar = useCallback(async ({ silenciosa = false } = {}) => {
    const atual = ++geracao.current
    setEstado((s) => aoComecarConsulta(s, { silenciosa }))
    try {
      const dados = await carregarNotificacoesDaSessao()
      if (ativo.current && atual === geracao.current) setEstado((s) => aoConcluirConsulta(s, dados))
    } catch (erro) {
      if (ativo.current && atual === geracao.current) setEstado((s) => aoFalharConsulta(s, erro.message))
    }
  }, [])
  useEffect(() => {
    ativo.current = true
    carregar({ silenciosa: true })
    const conferir = () => { if (document.visibilityState === 'visible') carregar({ silenciosa: true }) }
    const intervalo = setInterval(conferir, 30000)
    document.addEventListener('visibilitychange', conferir)
    window.addEventListener('online', conferir)
    return () => {
      ativo.current = false
      geracao.current++
      clearInterval(intervalo)
      document.removeEventListener('visibilitychange', conferir)
      window.removeEventListener('online', conferir)
    }
  }, [carregar])
  const marcarLida = async (id) => {
    ++geracao.current // descarta uma consulta iniciada antes da confirmação
    setEstado((s) => ({ ...s, lendo: id }))
    try {
      await lerNotificacaoDaSessao(id)
      if (ativo.current) await carregar()
    } catch (erro) {
      if (ativo.current) setEstado((s) => ({ ...s, erro: erro.message }))
    } finally {
      if (ativo.current) setEstado((s) => ({ ...s, lendo: null }))
    }
  }
  return { ...estado, carregar, marcarLida }
}
