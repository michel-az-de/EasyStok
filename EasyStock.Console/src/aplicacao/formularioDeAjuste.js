import { useCallback, useEffect, useRef, useState } from 'react'

// Contrato dos formulários de ajuste (#2070): `{ aoMudarPendencia, aoSalvo, compacto }`. Quem monta
// (Configurações ou Primeiros passos) só fica sabendo se há campo editado e não salvo e quando salvou.

// Avisa `true` ao editar e `false` ao salvar, desfazer ou sair da tela (a edição local se perde).
// O retorno guarda a última função recebida: quem monta pode passar uma função nova a cada render.
export function usePendenciaAvisada(pendente, aoMudarPendencia) {
  const avisar = useRef(aoMudarPendencia)
  useEffect(() => { avisar.current = aoMudarPendencia })
  useEffect(() => {
    if (!pendente) return undefined
    avisar.current?.(true)
    return () => avisar.current?.(false)
  }, [pendente])
}

// Leitura da API com "tentar de novo". Uma resposta atrasada de leitura anterior não substitui a atual.
export function useLeitura(ler) {
  const [carga, setCarga] = useState({ estado: 'carregando', dados: null, mensagem: null })
  const geracao = useRef(0)
  const buscar = useCallback(() => {
    const atual = ++geracao.current
    return ler().then(
      (dados) => { if (atual === geracao.current) setCarga({ estado: 'ok', dados, mensagem: null }) },
      (erro) => { if (atual === geracao.current) setCarga({ estado: 'erro', dados: null, mensagem: erro.message }) },
    )
  }, [ler])
  useEffect(() => {
    buscar()
    const leituras = geracao
    return () => { leituras.current++ }
  }, [buscar])
  const recarregar = useCallback(() => {
    setCarga((anterior) => ({ ...anterior, estado: anterior.dados === null ? 'carregando' : anterior.estado }))
    return buscar()
  }, [buscar])
  // A resposta de uma gravação já é o estado novo: troca os dados sem ler de novo.
  const substituir = useCallback((dados) => {
    geracao.current++
    setCarga({ estado: 'ok', dados, mensagem: null })
  }, [])
  return [carga, recarregar, substituir]
}

// Um "Salvar" por vez. O erro fica à vista até a próxima edição; nunca some calado.
export function useEnvio() {
  const [situacao, setSituacao] = useState({ estado: 'ocioso', mensagem: null })
  const ocupado = useRef(false)
  const enviar = useCallback(async (chamada) => {
    if (ocupado.current) return { ok: false }
    ocupado.current = true
    setSituacao({ estado: 'salvando', mensagem: null })
    try {
      const resultado = await chamada()
      setSituacao({ estado: 'salvo', mensagem: null })
      return { ok: true, resultado }
    } catch (erro) {
      setSituacao({ estado: 'erro', mensagem: erro.message })
      return { ok: false }
    } finally {
      ocupado.current = false
    }
  }, [])
  const limpar = useCallback(() => setSituacao({ estado: 'ocioso', mensagem: null }), [])
  return [situacao, enviar, limpar]
}
