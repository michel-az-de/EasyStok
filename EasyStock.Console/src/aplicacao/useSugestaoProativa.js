import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { contextoDaSugestao } from '../dominio/contextoDaSugestao'
import { EVENTO_SESSAO_ALTERADA } from '../infra/api/sessao'
import { criarConsultaProativa, identidadeDaSugestao } from './api/agente'
import { useComplementoLigado } from './preferenciaDoComplemento'

const editando = () => Boolean(document.activeElement?.matches('input, textarea, [contenteditable="true"]'))

export function useSugestaoProativa({ conversa, ativo, campoEmUso = false }) {
  const [ligado] = useComplementoLigado()
  const [identidade, setIdentidade] = useState(identidadeDaSugestao)
  const sessaoAtual = useRef(identidade)
  const [visivel, setVisivel] = useState(() => document.visibilityState === 'visible')
  const [escrevendo, setEscrevendo] = useState(editando)
  const [resposta, setResposta] = useState({ estado: 'ocioso', sugestao: null })
  const [consulta] = useState(() => criarConsultaProativa({ aoMudar: setResposta }))
  const contexto = contextoDaSugestao(conversa, identidade)
  const chave = contexto?.chave ?? null
  const atual = useRef(contexto)
  useLayoutEffect(() => { atual.current = contexto }, [contexto])
  const habilitada = Boolean(ativo && ligado && visivel && !campoEmUso && !escrevendo)

  useEffect(() => {
    const sessaoMudou = () => {
      const nova = identidadeDaSugestao()
      if (nova === sessaoAtual.current) return
      sessaoAtual.current = nova
      consulta.atualizar(null, false)
      setIdentidade(nova)
    }
    const visibilidadeMudou = () => {
      if (document.visibilityState !== 'visible') consulta.atualizar(null, false)
      setVisivel(document.visibilityState === 'visible')
    }
    const focoMudou = () => {
      if (editando()) consulta.atualizar(null, false)
      setEscrevendo(editando())
    }
    window.addEventListener(EVENTO_SESSAO_ALTERADA, sessaoMudou)
    window.addEventListener('storage', sessaoMudou)
    document.addEventListener('visibilitychange', visibilidadeMudou)
    document.addEventListener('focusin', focoMudou)
    document.addEventListener('focusout', focoMudou)
    return () => {
      consulta.atualizar(null, false)
      window.removeEventListener(EVENTO_SESSAO_ALTERADA, sessaoMudou)
      window.removeEventListener('storage', sessaoMudou)
      document.removeEventListener('visibilitychange', visibilidadeMudou)
      document.removeEventListener('focusin', focoMudou)
      document.removeEventListener('focusout', focoMudou)
    }
  }, [consulta])

  useEffect(() => {
    consulta.atualizar(atual.current, habilitada)
    return () => consulta.atualizar(null, false)
  }, [chave, habilitada, identidade, consulta])

  // Revalidar também no clique: troca de contexto/sessão pode preceder o próximo efeito.
  const obterSugestaoAtual = () => habilitada && atual.current?.chave === chave
    && document.visibilityState === 'visible' && !editando()
    ? consulta.obterSugestaoAtual(atual.current) : null
  return {
    estado: habilitada && resposta.chave === chave ? resposta.estado : 'ocioso',
    sugestao: habilitada && resposta.chave === chave ? resposta.sugestao : null,
    obterSugestaoAtual,
    dispensar: () => { if (atual.current?.chave === chave) consulta.dispensar(atual.current) },
  }
}
