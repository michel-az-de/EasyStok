import { useLayoutEffect, useRef, useState } from 'react'
import { respostaDoAssistente } from '../../dominio/acoesDoAssistente'
import { identidadeDaAssistencia, useContextoAssistencia } from '../../aplicacao/contextoAssistencia'

function mensagemDaFalha(erro) {
  if (erro?.codigo === 'ASSISTENTE_INDISPONIVEL') {
    return 'O Assistente está indisponível. Peça ao responsável pelo sistema para verificar a conexão.'
  }
  if (erro?.codigo === 'SEM_CONEXAO') {
    return 'Sem conexão com o EasyStok. Sua pergunta continua acima; tente novamente quando a conexão voltar.'
  }
  return erro?.message || 'O assistente não respondeu. Tente novamente.'
}

// Cada consulta pertence à conversa em que nasceu. A chamada anterior pode terminar,
// mas não ocupa o campo nem substitui a resposta de outro cliente.
export function useConversaAssistente({ conversa, historico, perguntar, executarAcao }) {
  const { identidade, chave: chaveContexto } = useContextoAssistencia(conversa)
  const [contexto, setContexto] = useState(chaveContexto)
  const [pergunta, setPergunta] = useState('')
  const [trocas, setTrocas] = useState([])
  const [perguntando, setPerguntando] = useState(false)
  const geracaoRef = useRef(0)
  const consultandoRef = useRef(false)
  const acoesRef = useRef(new Set())
  const proximoIdRef = useRef(1)
  const consultaRef = useRef(null)
  const contextoRef = useRef(chaveContexto)
  useLayoutEffect(() => { contextoRef.current = chaveContexto }, [chaveContexto])
  const contextoAtual = () => contextoRef.current === chaveContexto && identidadeDaAssistencia() === identidade

  // #2446 (M9): a revisão do contexto muda a cada mensagem nova. Ela invalida respostas e propostas, mas o texto
  // que a dona ainda está digitando só some quando muda a conversa ou a sessão.
  const [escopo, setEscopo] = useState(() => JSON.stringify([identidade, conversa?.id ?? null]))
  const escopoAtual = JSON.stringify([identidade, conversa?.id ?? null])
  if (contexto !== chaveContexto) {
    setContexto(chaveContexto)
    if (escopo !== escopoAtual) { setEscopo(escopoAtual); setPergunta('') }
    setTrocas([])
    setPerguntando(false)
  }

  useLayoutEffect(() => {
    consultandoRef.current = false
    acoesRef.current = new Set()
    return () => { geracaoRef.current += 1; consultaRef.current?.abort(); consultaRef.current = null }
  }, [chaveContexto])

  async function consultar(texto, trocaId = null) {
    texto = texto?.trim()
    if (!texto || !conversa || !contextoAtual() || consultandoRef.current) return false
    consultandoRef.current = true
    const controle = new AbortController()
    consultaRef.current = controle
    const geracao = geracaoRef.current
    const aindaAtual = () => !controle.signal.aborted && geracao === geracaoRef.current && contextoAtual()
    const id = trocaId ?? proximoIdRef.current++
    setPerguntando(true)
    if (trocaId === null) {
      setPergunta('')
      setTrocas(atual => [...atual, { id, pergunta: texto, resposta: null, acoes: [], erro: null }])
    } else {
      setTrocas(atual => atual.map(t => t.id === id ? { ...t, resposta: null, acoes: [], erro: null } : t))
    }
    try {
      const resposta = respostaDoAssistente(await perguntar(texto, conversa, historico, { sinal: controle.signal, identidadeEsperada: identidade }))
      if (!aindaAtual()) return false
      if (!resposta.texto && !resposta.acoes.length) throw new Error('O assistente não trouxe uma resposta. Tente novamente.')
      setTrocas(atual => atual.map(t => t.id === id ? { ...t, resposta: resposta.texto, acoes: resposta.acoes } : t))
      return true
    } catch (erro) {
      if (aindaAtual()) {
        setTrocas(atual => atual.map(t => t.id === id ? { ...t, erro: mensagemDaFalha(erro) } : t))
      }
      return false
    } finally {
      if (aindaAtual()) {
        consultaRef.current = null
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
    if (!conversa || !contextoAtual() || acoes.has(chave)
      || !trocas.some(t => t.id === id && t.acoes.some(a => a.chave === proposta.chave))) return false
    acoes.add(chave)
    const geracao = geracaoRef.current
    const aindaAtual = () => geracao === geracaoRef.current && contextoAtual()
    marcarAcao(id, proposta.chave, { estado: 'executando', erro: null })
    try {
      const resultado = await executarAcao(proposta, conversa, aindaAtual)
      if (!aindaAtual()) return false
      if (resultado === false) throw new Error('A ação não foi confirmada. Confira a conversa antes de tentar novamente.')
      marcarAcao(id, proposta.chave, { estado: 'feita' })
      return true
    } catch (erro) {
      acoes.delete(chave)
      if (aindaAtual()) marcarAcao(id, proposta.chave, { estado: 'falhou', erro: mensagemDaFalha(erro) })
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
