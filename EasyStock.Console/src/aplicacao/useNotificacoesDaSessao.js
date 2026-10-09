import { useCallback, useEffect, useRef, useState } from 'react'
import { carregarNotificacoesDaSessao, lerNotificacaoDaSessao } from '../infra/api/notificacoesDaSessao'

export function useNotificacoesDaSessao() {
  const [estado, setEstado] = useState({ total: null, recentes: [], erro: null, carregando: true, lendo: null })
  const ativo = useRef(false)
  const geracao = useRef(0)
  const carregar = useCallback(async () => {
    const atual = ++geracao.current
    setEstado((s) => ({ ...s, carregando: true }))
    try {
      const dados = await carregarNotificacoesDaSessao()
      if (ativo.current && atual === geracao.current) setEstado((s) => ({ ...s, ...dados, erro: null, carregando: false }))
    } catch (erro) {
      if (ativo.current && atual === geracao.current) setEstado((s) => ({ ...s, erro: erro.message, carregando: false }))
    }
  }, [])
  useEffect(() => {
    ativo.current = true
    carregar()
    const conferir = () => { if (document.visibilityState === 'visible') carregar() }
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
