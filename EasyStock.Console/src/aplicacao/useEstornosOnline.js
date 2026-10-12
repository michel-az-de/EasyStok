import { useCallback, useEffect, useRef, useState } from 'react'
import {
  listarEstornosOnline, solicitarEstornoOnline, retomarEstornoOnline, descartarEstornoOnline,
  lerEstornoSemResposta, guardarEstornoSemResposta, limparEstornoSemResposta,
} from '../infra/api/estornosOnlineApi'

export function useEstornosOnline(pedidoId) {
  const [dados, setDados] = useState(null)
  const [erro, setErro] = useState(null)
  const [aviso, setAviso] = useState(null)
  const [semResposta, setSemResposta] = useState(null)
  const [ocupado, setOcupado] = useState(false)
  const emVoo = useRef(false)
  const ativo = useRef(true)

  const carregar = useCallback(async () => {
    const salvo = lerEstornoSemResposta(pedidoId)
    const resposta = await listarEstornosOnline(pedidoId)
    const encontrado = salvo && resposta.estornos.some((e) => e.id === salvo.operacaoId)
    if (encontrado) limparEstornoSemResposta(pedidoId, salvo.operacaoId)
    if (ativo.current) { setDados(resposta); setSemResposta(encontrado ? null : salvo) }
  }, [pedidoId])

  const atualizar = useCallback(async () => {
    if (emVoo.current) return
    emVoo.current = true; setOcupado(true); setErro(null)
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

  async function enviar(campos, operacaoId) {
    if (emVoo.current) return false
    emVoo.current = true; setOcupado(true); setErro(null); setAviso(null)
    let corpo
    let recebido = false
    try {
      if (!operacaoId) {
        corpo = lerEstornoSemResposta(pedidoId) ?? { ...campos, operacaoId: crypto.randomUUID() }
        guardarEstornoSemResposta(pedidoId, corpo)
        setSemResposta(corpo)
      }
      const resposta = operacaoId ? await retomarEstornoOnline(pedidoId, operacaoId) : await solicitarEstornoOnline(pedidoId, corpo)
      recebido = true
      if (corpo) limparEstornoSemResposta(pedidoId, corpo.operacaoId)
      if (ativo.current) { setSemResposta(null); setAviso(resposta.detalhe); setDados(null) }
      await carregar()
      return true
    } catch (e) {
      if (corpo && [400, 403, 404, 409].includes(e.status)) {
        limparEstornoSemResposta(pedidoId, corpo.operacaoId)
        if (ativo.current) setSemResposta(null)
      }
      if (ativo.current) setErro(recebido ? 'O servidor respondeu ao estorno. Atualize para conferir a situação e o saldo.' : e.message)
      return recebido
    } finally { emVoo.current = false; if (ativo.current) setOcupado(false) }
  }

  // #1563: "retomar" envia ao Mercado Pago; descartar encerra a solicitação que nunca foi enviada.
  // No 409 (já enviada, já encerrada) a lista é relida: o botão some e a situação real aparece.
  async function descartar(operacaoId) {
    if (emVoo.current) return false
    emVoo.current = true; setOcupado(true); setErro(null); setAviso(null)
    let recebido = false
    try {
      const resposta = await descartarEstornoOnline(pedidoId, operacaoId)
      recebido = true
      if (ativo.current) setAviso(resposta.detalhe)
      await carregar()
      return true
    } catch (e) {
      if (!recebido && e.status === 409) await carregar().catch(() => {})
      if (ativo.current) setErro(recebido ? 'A solicitação foi descartada. Atualize para conferir a situação e o saldo.' : e.message)
      return recebido
    } finally { emVoo.current = false; if (ativo.current) setOcupado(false) }
  }

  return { dados, erro, aviso, semResposta, ocupado, atualizar, solicitar: (campos) => enviar(campos), retomar: (id) => enviar(null, id), descartar }
}
