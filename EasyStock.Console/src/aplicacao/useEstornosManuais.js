import { useCallback, useEffect, useRef, useState } from 'react'
import {
  guardarDevolucaoPendente, lerDevolucaoPendente, limparDevolucaoPendente,
  listarEstornosManuais, registrarEstornoManual,
} from '../infra/api/estornosManuaisApi'

export function useEstornosManuais(pedidoId) {
  const [dados, setDados] = useState(null)
  const [erro, setErro] = useState(null)
  const [aviso, setAviso] = useState(null)
  const [pendente, setPendente] = useState(null)
  const [ocupado, setOcupado] = useState(false)
  const emVoo = useRef(false)
  const ativo = useRef(true)

  const carregar = useCallback(async () => {
    const salvo = lerDevolucaoPendente(pedidoId)
    const resposta = await listarEstornosManuais(pedidoId)
    const confirmado = salvo && resposta.estornos.some((e) => e.id === salvo.operacaoId)
    if (confirmado) limparDevolucaoPendente(pedidoId, salvo.operacaoId)
    if (!ativo.current) return
    setPendente(confirmado ? null : salvo)
    setDados(resposta)
    if (confirmado) setAviso('Devolução confirmada e registrada no Caixa.')
  }, [pedidoId])

  const atualizar = useCallback(async () => {
    if (emVoo.current) return
    emVoo.current = true
    setOcupado(true)
    setErro(null)
    try { await carregar() }
    catch (e) { if (ativo.current) setErro(e.message) }
    finally { emVoo.current = false; if (ativo.current) setOcupado(false) }
  }, [carregar])

  useEffect(() => {
    ativo.current = true
    atualizar()
    window.addEventListener('focus', atualizar)
    return () => { ativo.current = false; window.removeEventListener('focus', atualizar) }
  }, [atualizar])

  async function registrar(campos) {
    if (emVoo.current) return false
    emVoo.current = true
    setOcupado(true)
    setErro(null)
    setAviso(null)
    let corpo
    let confirmado = false
    try {
      corpo = lerDevolucaoPendente(pedidoId) ?? { ...campos, operacaoId: crypto.randomUUID() }
      guardarDevolucaoPendente(pedidoId, corpo)
      setPendente(corpo)
      await registrarEstornoManual(pedidoId, corpo)
      confirmado = true
      limparDevolucaoPendente(pedidoId, corpo.operacaoId)
      if (ativo.current) { setPendente(null); setDados(null); setAviso('Devolução confirmada e registrada no Caixa.') }
      await carregar()
      return true
    } catch (e) {
      if (corpo && [400, 403, 404, 409].includes(e.status)) {
        limparDevolucaoPendente(pedidoId, corpo.operacaoId)
        if (ativo.current) setPendente(null)
      }
      if (ativo.current) setErro(confirmado
        ? 'A devolução foi registrada. Atualize para conferir o saldo e o histórico.' : e.message)
      return confirmado
    } finally { emVoo.current = false; if (ativo.current) setOcupado(false) }
  }

  return { dados, erro, aviso, pendente, ocupado, atualizar, registrar }
}
