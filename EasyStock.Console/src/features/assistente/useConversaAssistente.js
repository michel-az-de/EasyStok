import { useLayoutEffect, useRef, useState } from 'react'
import { respostaDoAssistente } from '../../dominio/acoesDoAssistente'

function mensagemDaFalha(erro) {
  if (erro?.codigo === 'ASSISTENTE_INDISPONIVEL') {
    return 'O assistente está indisponível. Peça ao responsável pelo sistema para verificar a conexão com a IA.'
  }
  if (erro?.codigo === 'SEM_CONEXAO') {
    return 'Sem conexão com o EasyStok. Sua pergunta continua acima; tente novamente quando a conexão voltar.'
  }
  return erro?.message || 'O assistente não respondeu. Tente novamente.'
}

// Cada consulta pertence à conversa em que nasceu. A chamada anterior pode terminar,
// mas não ocupa o campo nem substitui a resposta de outro cliente.
export function useConversaAssistente({ conversa, historico, perguntar, executarAcao }) {
  const conversaId = conversa?.id ?? null
  const [contexto, setContexto] = useState(conversaId)
  const [pergunta, setPergunta] = useState('')
  const [trocas, setTrocas] = useState([])
  const [perguntando, setPerguntando] = useState(false)
  const geracaoRef = useRef(0)
  const consultandoRef = useRef(false)
  const acoesRef = useRef(new Set())
  const proximoIdRef = useRef(1)

  if (contexto !== conversaId) {
    setContexto(conversaId)
    setPergunta('')
    setTrocas([])
    setPerguntando(false)
  }

  useLayoutEffect(() => {
    consultandoRef.current = false
    acoesRef.current = new Set()
    return () => { geracaoRef.current += 1 }
  }, [conversaId])

  async function consultar(texto, trocaId = null) {
    texto = texto?.trim()
    if (!texto || !conversa || consultandoRef.current) return false
    consultandoRef.current = true
    const geracao = geracaoRef.current
    const id = trocaId ?? proximoIdRef.current++
    setPerguntando(true)
    if (trocaId === null) {
      setPergunta('')
      setTrocas(atual => [...atual, { id, pergunta: texto, resposta: null, acoes: [], erro: null }])
    } else {
      setTrocas(atual => atual.map(t => t.id === id ? { ...t, resposta: null, acoes: [], erro: null } : t))
    }
    try {
      const resposta = respostaDoAssistente(await perguntar(texto, conversa, historico))
      if (geracao !== geracaoRef.current) return false
      if (!resposta.texto && !resposta.acoes.length) throw new Error('O assistente não trouxe uma resposta. Tente novamente.')
      setTrocas(atual => atual.map(t => t.id === id ? { ...t, resposta: resposta.texto, acoes: resposta.acoes } : t))
      return true
    } catch (erro) {
      if (geracao === geracaoRef.current) {
        setTrocas(atual => atual.map(t => t.id === id ? { ...t, erro: mensagemDaFalha(erro) } : t))
      }
      return false
    } finally {
      if (geracao === geracaoRef.current) {
        consultandoRef.current = false
        setPerguntando(false)
      }
    }
  }

  const marcarAcao = (id, chave, mudanca) => setTrocas(atual => atual.map(t => t.id === id ? {
    ...t, acoes: t.acoes.map(a => a.chave === chave ? { ...a, ...mudanca } : a),
  } : t))

  async function aoExecutarAcao(id, proposta) {
    const chave = `${id}:${proposta.chave}`
    const acoes = acoesRef.current
    if (!conversa || acoes.has(chave)) return false
    acoes.add(chave)
    const geracao = geracaoRef.current
    const contextoAtual = () => geracao === geracaoRef.current
    marcarAcao(id, proposta.chave, { estado: 'executando', erro: null })
    try {
      const resultado = await executarAcao(proposta, conversa, contextoAtual)
      if (!contextoAtual()) return false
      if (resultado === false) throw new Error('A ação não foi confirmada. Confira a conversa antes de tentar novamente.')
      marcarAcao(id, proposta.chave, { estado: 'feita' })
      return true
    } catch (erro) {
      acoes.delete(chave)
      if (contextoAtual()) marcarAcao(id, proposta.chave, { estado: 'falhou', erro: mensagemDaFalha(erro) })
      return false
    }
  }

  return {
    pergunta, setPergunta, trocas, perguntando,
    aoPerguntar: () => consultar(pergunta),
    aoRepetir: troca => consultar(troca.pergunta, troca.id),
    aoExecutarAcao,
  }
}
