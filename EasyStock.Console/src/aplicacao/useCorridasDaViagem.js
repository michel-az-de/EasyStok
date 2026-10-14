import { useCallback, useEffect, useRef, useState } from 'react'
import * as api from '../infra/api/entregasApi'

// Corridas externas de uma viagem (I9.2, #1691): a 99, a Lalamove ou a Uber pedida pelo app,
// anotada à mão com o custo. Lê ao montar e depois de cada gravação; um envio por vez.
export function useCorridasDaViagem(viagemId) {
  const [corridas, setCorridas] = useState(null)
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

  // Devolve se deu certo: o formulário só fecha quando a API aceitou.
  const gravar = async (chamada, falha) => {
    if (enviandoRef.current) return false
    enviandoRef.current = true
    setEnviando(true)
    try {
      await chamada()
      await recarregar()
      return true
    } catch (e) {
      setErro(`${falha}: ${e.message}`)
      return false
    } finally {
      enviandoRef.current = false
      setEnviando(false)
    }
  }

  return {
    corridas,
    erro,
    enviando,
    registrar: (corpo) => gravar(() => api.registrarCorrida(viagemId, corpo), 'A corrida não foi registrada'),
    corrigir: (corridaId, corpo) => gravar(() => api.corrigirCorrida(viagemId, corridaId, corpo), 'A corrida não foi corrigida'),
  }
}
