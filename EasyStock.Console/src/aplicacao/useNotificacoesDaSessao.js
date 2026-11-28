import { useCallback, useEffect, useRef, useState } from 'react'
import {
  carregarNotificacoesDaSessao, carregarTodasAsNotificacoes, lerNotificacaoDaSessao, lerTodasAsNotificacoes,
} from '../infra/api/notificacoesDaSessao'
import { aoComecarConsulta, aoConcluirConsulta, aoFalharConsulta } from './situacaoDeConsulta'

const TODAS_VAZIA = { itens: [], total: 0, paginas: 0, pagina: 0, carregando: false, erro: null }
// Comparação de texto pura (ordem dos caracteres), como a API ordena: localeCompare segue o idioma e pode trocar
// a ordem de duas datas no mesmo segundo.
const maisNovoPrimeiro = (a, b) => {
  const x = String(a.createdAt ?? ''), y = String(b.createdAt ?? '')
  return x > y ? -1 : x < y ? 1 : 0
}
const marcarNaLista = (itens, lido, ids) => itens.map((aviso) => (ids === null || ids.has(aviso.id) ? { ...aviso, lida: lido } : aviso))

// #3394: a leitura é otimista, como nas redes: o aviso sai da lista e o contador cai na hora; se a API recusa,
// o aviso volta e `falha` diz o que houve. Resposta que cruzou com uma leitura não pinta a tela (nem a dos
// recentes nem a da aba Todas), senão o aviso lido reapareceria; a tela se acerta quando a leitura termina.
export function useNotificacoesDaSessao() {
  const [estado, setEstado] = useState({
    total: null, recentes: [], erro: null, carregando: true, falha: null, chegaram: [], todas: TODAS_VAZIA,
  })
  const ativo = useRef(false)
  const geracao = useRef(0)
  const geracaoTodas = useRef(0)
  const pendentes = useRef(0)
  const leituras = useRef(0) // leituras começadas: a resposta da aba Todas que cruzou com uma é pedida de novo
  const todasDepois = useRef(null) // pedido da aba Todas guardado até a leitura pendente terminar
  // Data do aviso mais novo já visto: só o que é mais novo que ele "chegou". Um aviso antigo que sobe para os
  // recentes porque outro foi lido não chegou agora.
  const maisNovoVisto = useRef(null)
  // Os handlers leem o retrato mais recente; atualizado depois de cada render, não durante.
  const atual = useRef(estado)
  useEffect(() => { atual.current = estado })

  const carregar = useCallback(async ({ silenciosa = false } = {}) => {
    const minha = ++geracao.current
    setEstado((s) => aoComecarConsulta(s, { silenciosa }))
    try {
      const dados = await carregarNotificacoesDaSessao()
      if (!ativo.current || minha !== geracao.current || pendentes.current > 0) return
      // A primeira leitura não realça nada; depois, realça o que é mais novo que tudo o que já se viu.
      const datas = dados.recentes.map((aviso) => String(aviso.createdAt ?? ''))
      const visto = maisNovoVisto.current
      const chegaram = visto === null ? [] : dados.recentes.filter((_, i) => datas[i] > visto).map((aviso) => aviso.id)
      maisNovoVisto.current = datas.reduce((maior, data) => (data > maior ? data : maior), visto ?? '')
      setEstado((s) => aoConcluirConsulta(s, { ...dados, chegaram: chegaram.length > 0 ? chegaram : s.chegaram }))
    } catch (erro) {
      if (ativo.current && minha === geracao.current) setEstado((s) => aoFalharConsulta(s, erro.message))
    }
  }, [])

  const carregarTodas = useCallback(async ({ mais = false } = {}) => {
    const minha = ++geracaoTodas.current
    setEstado((s) => ({ ...s, todas: { ...s.todas, carregando: true, erro: null } }))
    // Só pinta a resposta que não cruzou com leitura: a que cruzou pode ter saído antes da gravação. Ela é pedida
    // de novo agora (a leitura já terminou) ou quando a leitura pendente terminar (`todasDepois`). Pedido mais
    // novo vence o mais velho, e é ele quem tira o "carregando".
    for (;;) {
      const pagina = mais ? atual.current.todas.pagina + 1 : 1
      const leiturasNoPedido = leituras.current
      const semLeituraPendente = pendentes.current === 0
      let dados
      try {
        dados = await carregarTodasAsNotificacoes(pagina)
      } catch (erro) {
        if (ativo.current && minha === geracaoTodas.current) setEstado((s) => ({ ...s, todas: { ...s.todas, carregando: false, erro: erro.message } }))
        return
      }
      if (!ativo.current || minha !== geracaoTodas.current) return
      if (pendentes.current > 0) { todasDepois.current = { mais }; return }
      if (!semLeituraPendente || leituras.current !== leiturasNoPedido) continue
      setEstado((s) => ({ ...s, todas: {
        ...dados, carregando: false, erro: null,
        itens: mais ? [...s.todas.itens, ...dados.itens.filter((novo) => !s.todas.itens.some((velho) => velho.id === novo.id))] : dados.itens,
      } }))
      return
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

  // Tira da tela agora, grava na API e, se der errado, devolve o que tirou. "Voltam" são os não lidos das duas
  // listas (os recentes trazem só 10; a aba Todas pode ter mais), e é por eles que o contador cai e sobe.
  const lerComVolta = useCallback(async ({ ids, gravar, mensagem }) => {
    const antes = atual.current
    const escolhido = (aviso) => ids === null || ids.has(aviso.id)
    const tirados = antes.recentes.filter(escolhido)
    const voltam = new Set([...tirados, ...antes.todas.itens.filter((aviso) => !aviso.lida && escolhido(aviso))].map((aviso) => aviso.id))
    pendentes.current++
    leituras.current++
    ++geracao.current
    setEstado((s) => ({
      ...s, falha: null, carregando: false,
      recentes: s.recentes.filter((aviso) => !escolhido(aviso)),
      total: ids === null ? 0 : Math.max(0, (s.total ?? 0) - voltam.size),
      todas: { ...s.todas, itens: marcarNaLista(s.todas.itens, true, ids) },
    }))
    try {
      await gravar()
    } catch {
      if (ativo.current) {
        setEstado((s) => ({
          ...s, falha: mensagem,
          recentes: [...s.recentes, ...tirados.filter((aviso) => !s.recentes.some((r) => r.id === aviso.id))].sort(maisNovoPrimeiro),
          total: ids === null ? antes.total : (s.total ?? 0) + voltam.size,
          todas: { ...s.todas, itens: marcarNaLista(s.todas.itens, false, voltam) },
        }))
      }
    } finally {
      pendentes.current--
      // Deu certo ou não, a tela volta a bater com o servidor quando não resta leitura pendente.
      if (ativo.current && pendentes.current === 0) {
        carregar({ silenciosa: true })
        const pedido = todasDepois.current
        todasDepois.current = null
        if (pedido) carregarTodas(pedido)
      }
    }
  }, [carregar, carregarTodas])

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
