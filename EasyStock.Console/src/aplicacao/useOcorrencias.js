import { useCallback, useEffect, useRef, useState } from 'react'
import { listarOcorrencias, resolverOcorrencia } from '../infra/api/ocorrenciasApi'
import { useAcoes } from './contextos'

// O componente é montado por pedido. A limpeza invalida respostas de outra ficha.
export function useOcorrencias(pedidoId, conversaId) {
  const acoes = useAcoes()
  const [dados, setDados] = useState(null)
  const [erro, setErro] = useState(null)
  const [ocupado, setOcupado] = useState(false)
  const emVoo = useRef(false)
  const geracao = useRef(0)

  const carregar = useCallback(async (versao) => {
    const resposta = await listarOcorrencias(pedidoId)
    if (geracao.current === versao) setDados(resposta)
  }, [pedidoId])

  const atualizar = useCallback(async () => {
    if (emVoo.current) return
    emVoo.current = true
    const versao = geracao.current
    setOcupado(true)
    try { await carregar(versao); if (geracao.current === versao) setErro(null) }
    catch (e) { if (geracao.current === versao) { setErro(e.message); setDados(null) } }
    finally { emVoo.current = false; if (geracao.current === versao) setOcupado(false) }
  }, [carregar])

  useEffect(() => {
    // Em StrictMode o primeiro efeito pode terminar depois da segunda montagem.
    const versao = ++geracao.current
    carregar(versao).catch((e) => { if (geracao.current === versao) setErro(e.message) })
    window.addEventListener('focus', atualizar)
    return () => { ++geracao.current; window.removeEventListener('focus', atualizar) }
  }, [atualizar, carregar])

  async function alterar(operacao) {
    if (emVoo.current) return false
    emVoo.current = true
    const versao = geracao.current
    setOcupado(true)
    setErro(null)
    let confirmado = false
    try {
      await operacao()
      confirmado = true
      await carregar(versao)
      return true
    } catch (e) {
      // Inclusive após resposta perdida: a próxima leitura decide o estado exibido.
      try { await carregar(versao) }
      catch { if (geracao.current === versao) setDados(null) }
      if (geracao.current === versao) setErro(confirmado ? 'A ação foi registrada. Atualize para conferir o histórico.' : e.message)
      return confirmado
    } finally { emVoo.current = false; if (geracao.current === versao) setOcupado(false) }
  }

  return {
    dados, erro, ocupado, atualizar,
    apurar: (id) => alterar(() => acoes.apurarOcorrencia(conversaId, id)),
    encerrar: (id, resolucao) => alterar(() => acoes.encerrarOcorrenciaSemEstorno(conversaId, resolucao, id)),
    retomar: (o) => alterar(() => resolverOcorrencia(o.id, { resolucao: o.resolucao, reembolsar: true, valor: o.reembolsoValor })),
  }
}
