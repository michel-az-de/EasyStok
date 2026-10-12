import { useCallback, useEffect, useRef, useState } from 'react'
export * from '../infra/api/financeiroApi'

export function useConsultaFinanceira(carregar) {
  const [carga, setCarga] = useState({ dados: null, erro: null, carregando: true })
  const versao = useRef(0)
  const recarregar = useCallback(async () => {
    const atual = ++versao.current
    setCarga((antes) => ({ ...antes, carregando: true, erro: null }))
    try {
      const dados = await carregar()
      if (versao.current === atual) setCarga({ dados, erro: null, carregando: false })
    } catch (erro) {
      if (versao.current === atual) setCarga({ dados: null, erro: erro.message, carregando: false })
    }
  }, [carregar])
  useEffect(() => { recarregar(); return () => { versao.current += 1 } }, [recarregar])
  return { ...carga, recarregar }
}

export function useOperacaoFinanceira(aoGravar) {
  const [estado, setEstado] = useState({ ocupado: false, erro: null })
  const emCurso = useRef(false)
  const executar = async (acao) => {
    if (emCurso.current) return false
    emCurso.current = true
    setEstado({ ocupado: true, erro: null })
    try {
      await acao()
      await aoGravar?.()
      setEstado({ ocupado: false, erro: null })
      return true
    } catch (erro) {
      setEstado({ ocupado: false, erro: erro.message })
      return false
    } finally { emCurso.current = false }
  }
  return { ...estado, executar }
}
