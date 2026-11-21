import { useCallback, useEffect, useRef, useState } from 'react'
import { ModalConfirmar } from '../../componentes/ModalConfirmar'
import { usePerguntaDeOutra } from '../../aplicacao/usePerguntaDeOutra'

const QUADROS_DE_ESPERA = 20

// O controle que pediu a ação fica desabilitado enquanto ela voa e perde o foco; devolve quando voltar a aceitar.
function devolverOFoco(controle, tentativa = 0) {
  if (!controle?.isConnected) return
  if (controle.disabled && tentativa < QUADROS_DE_ESPERA) { requestAnimationFrame(() => devolverOFoco(controle, tentativa + 1)); return }
  controle.focus({ preventScroll: true })
}

// #3037 (fecha o padrão da #3011): a pergunta "Conversa de outra atendente. Confirme" da camada de API abre aqui,
// no ModalConfirmar, em vez do window.confirm. Esc, X, fundo e Cancelar recusam; o foco volta ao controle de
// origem. O ModalConfirmar chama aoFechar e depois aoConfirmar: a recusa espera um microtask para o "sim" chegar primeiro.
export function AnfitriaoDeConfirmacao() {
  const [pergunta, setPergunta] = useState(null)
  const resolverRef = useRef(null)
  const ultimoFocoRef = useRef(null)
  const origemRef = useRef(null)

  useEffect(() => {
    const guardar = (e) => { if (!e.target.closest?.('dialog')) ultimoFocoRef.current = e.target }
    document.addEventListener('focusin', guardar)
    return () => {
      document.removeEventListener('focusin', guardar)
      // Desmontado com pergunta pendente: recusa, senão a ação fica sem erro e sem fim (a recusa rejeita com CONFIRMACAO_RECUSADA).
      const resolver = resolverRef.current
      resolverRef.current = null
      resolver?.(false)
    }
  }, [])

  const perguntar = useCallback((mensagem) => new Promise((resolver) => {
    resolverRef.current?.(false)
    resolverRef.current = resolver
    origemRef.current = ultimoFocoRef.current
    setPergunta(mensagem)
  }), [])
  usePerguntaDeOutra(perguntar)

  const responder = (resposta) => {
    const resolver = resolverRef.current
    resolverRef.current = null
    resolver?.(resposta)
  }
  const fechar = () => {
    setPergunta(null)
    queueMicrotask(() => responder(false))
    requestAnimationFrame(() => devolverOFoco(origemRef.current))
  }

  if (pergunta === null) return null
  return (
    <ModalConfirmar
      titulo="Conversa de outra atendente"
      descricao={pergunta}
      rotuloConfirmar="Confirmar"
      aoConfirmar={() => responder(true)}
      aoFechar={fechar}
    />
  )
}
