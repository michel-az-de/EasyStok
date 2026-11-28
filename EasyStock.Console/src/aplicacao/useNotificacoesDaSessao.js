import { useCallback, useEffect, useRef, useState } from 'react'
import {
  carregarNotificacoesDaSessao, carregarTodasAsNotificacoes, lerNotificacaoDaSessao, lerTodasAsNotificacoes,
} from '../infra/api/notificacoesDaSessao'
import { aoComecarConsulta, aoConcluirConsulta, aoFalharConsulta } from './situacaoDeConsulta'

const TODAS_VAZIA = { itens: [], total: 0, paginas: 0, pagina: 0, carregando: false, erro: null }
const maisNovoPrimeiro = (a, b) => String(b.createdAt ?? '').localeCompare(String(a.createdAt ?? ''))
const marcarNaLista = (itens, lido, ids) => itens.map((aviso) => (ids === null || ids.has(aviso.id) ? { ...aviso, lida: lido } : aviso))

// #3394: a leitura é otimista, como nas redes: o aviso sai da lista e o contador cai na hora; se a API recusa,
// o aviso volta e `falha` diz o que houve. Enquanto há leitura pendente, a consulta de 30 s não pinta a tela,
// senão uma resposta que saiu antes do PATCH faria o aviso lido reaparecer.
export function useNotificacoesDaSessao() {
  const [estado, setEstado] = useState({
    total: null, recentes: [], erro: null, carregando: true, falha: null, chegaram: [], todas: TODAS_VAZIA,
  })
  const ativo = useRef(false)
  const geracao = useRef(0)
  const pendentes = useRef(0)
  const conhecidos = useRef(null)
  // Os handlers leem o retrato mais recente; atualizado depois de cada render, não durante.
  const atual = useRef(estado)
  useEffect(() => { atual.current = estado })

  const carregar = useCallback(async ({ silenciosa = false } = {}) => {
    const minha = ++geracao.current
    setEstado((s) => aoComecarConsulta(s, { silenciosa }))
    try {
      const dados = await carregarNotificacoesDaSessao()
      if (!ativo.current || minha !== geracao.current || pendentes.current > 0) return
      // O que não estava na última leitura chegou agora: o painel realça. A primeira leitura não realça nada.
      const ids = dados.recentes.map((aviso) => aviso.id)
      const chegaram = conhecidos.current === null ? [] : ids.filter((id) => !conhecidos.current.has(id))
      conhecidos.current = new Set(ids)
      setEstado((s) => aoConcluirConsulta(s, { ...dados, chegaram: chegaram.length > 0 ? chegaram : s.chegaram }))
    } catch (erro) {
      if (ativo.current && minha === geracao.current) setEstado((s) => aoFalharConsulta(s, erro.message))
    }
  }, [])

  const carregarTodas = useCallback(async ({ mais = false } = {}) => {
    const pagina = mais ? atual.current.todas.pagina + 1 : 1
    setEstado((s) => ({ ...s, todas: { ...s.todas, carregando: true, erro: null } }))
    try {
      const dados = await carregarTodasAsNotificacoes(pagina)
      if (!ativo.current) return
      setEstado((s) => ({ ...s, todas: {
        ...dados, carregando: false, erro: null,
        itens: mais ? [...s.todas.itens, ...dados.itens.filter((novo) => !s.todas.itens.some((velho) => velho.id === novo.id))] : dados.itens,
      } }))
    } catch (erro) {
      if (ativo.current) setEstado((s) => ({ ...s, todas: { ...s.todas, carregando: false, erro: erro.message } }))
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

  // Tira da tela agora, grava na API e, se der errado, devolve o que tirou.
  const lerComVolta = useCallback(async ({ ids, gravar, mensagem }) => {
    const antes = atual.current
    const tirados = antes.recentes.filter((aviso) => ids === null || ids.has(aviso.id))
    pendentes.current++
    ++geracao.current
    setEstado((s) => ({
      ...s, falha: null,
      recentes: ids === null ? [] : s.recentes.filter((aviso) => !ids.has(aviso.id)),
      total: ids === null ? 0 : Math.max(0, (s.total ?? 0) - tirados.length),
      todas: { ...s.todas, itens: marcarNaLista(s.todas.itens, true, ids) },
    }))
    try {
      await gravar()
      pendentes.current--
      if (ativo.current) carregar({ silenciosa: true })
    } catch {
      pendentes.current--
      if (!ativo.current) return
      setEstado((s) => ({
        ...s, falha: mensagem,
        recentes: [...s.recentes, ...tirados.filter((aviso) => !s.recentes.some((r) => r.id === aviso.id))].sort(maisNovoPrimeiro),
        total: ids === null ? antes.total : (s.total ?? 0) + tirados.length,
        todas: { ...s.todas, itens: marcarNaLista(s.todas.itens, false, ids === null ? new Set(tirados.map((a) => a.id)) : ids) },
      }))
    }
  }, [carregar])

  const marcarLida = useCallback((id) => {
    const aviso = atual.current.recentes.find((a) => a.id === id) ?? atual.current.todas.itens.find((a) => a.id === id)
    return lerComVolta({ ids: new Set([id]), gravar: () => lerNotificacaoDaSessao(id),
      mensagem: `Não deu para marcar "${aviso?.titulo || 'Aviso'}" como lido. Ele voltou para a lista; tente de novo.` })
  }, [lerComVolta])

  const marcarTodas = useCallback(() => lerComVolta({ ids: null, gravar: lerTodasAsNotificacoes,
    mensagem: 'Não deu para marcar todos como lidos. Eles voltaram para a lista; tente de novo.' }), [lerComVolta])

  // Painel fechado: a falha já foi vista e o realce do que chegou não se repete na próxima abertura.
  const aoFecharPainel = useCallback(() => setEstado((s) => (s.falha || s.chegaram.length > 0 ? { ...s, falha: null, chegaram: [] } : s)), [])

  return { ...estado, carregar, carregarTodas, marcarLida, marcarTodas, aoFecharPainel }
}
