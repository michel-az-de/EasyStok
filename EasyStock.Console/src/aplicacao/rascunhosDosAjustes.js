import { createContext, useCallback, useContext, useRef, useState } from 'react'

// A memória pertence à visita autenticada, nunca ao navegador nem a outra conta.
export const ContextoRascunhosDosAjustes = createContext(null)

export function useGravacoesDosAjustes() {
  const emAndamento = useRef(new Map())
  const [gravando, setGravando] = useState({})
  const estaGravando = useCallback((chave) => emAndamento.current.has(chave), [])
  const iniciarGravacao = useCallback((chave) => {
    if (emAndamento.current.has(chave)) return null
    const gravacao = {}
    emAndamento.current.set(chave, gravacao)
    setGravando((atuais) => ({ ...atuais, [chave]: true }))
    return () => {
      if (emAndamento.current.get(chave) !== gravacao) return
      emAndamento.current.delete(chave)
      setGravando((atuais) => { const proximos = { ...atuais }; delete proximos[chave]; return proximos })
    }
  }, [])
  return { gravando, iniciarGravacao, estaGravando }
}

export function useGravacaoDoAjuste(chave) {
  const contexto = useContext(ContextoRascunhosDosAjustes)
  const local = useGravacoesDosAjustes()
  const { gravando, iniciarGravacao, estaGravando } = contexto ?? local
  const iniciar = useCallback(() => iniciarGravacao(chave), [chave, iniciarGravacao])
  const consultar = useCallback(() => estaGravando(chave), [chave, estaGravando])
  return [gravando[chave] === true, iniciar, consultar]
}

export function useRascunhoDoAjuste(chave) {
  const contexto = useContext(ContextoRascunhosDosAjustes)
  const [local, definirLocal] = useState(null)
  const guardar = contexto?.definir
  const definir = useCallback((valor) => {
    if (guardar) guardar(chave, valor)
    else definirLocal(valor)
  }, [chave, guardar])
  return [contexto ? contexto.rascunhos[chave] ?? null : local, definir]
}
