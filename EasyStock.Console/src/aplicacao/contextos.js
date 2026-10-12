import { createContext, useContext } from 'react'

// Três contextos em vez de um. Quem só dispara ação não volta a renderizar
// quando o estado muda, quem só lê o catálogo não acorda a cada tique do
// relógio, e ninguém precisa conhecer o que não usa.
export const ContextoEstado = createContext(null)
export const ContextoAcoes = createContext(null)
export const ContextoCatalogo = createContext(null)

function useContexto(contexto, nome) {
  const valor = useContext(contexto)
  if (valor === null) throw new Error(nome + ' exige AtendimentoProvider acima na árvore.')
  return valor
}

export const useAtendimento = () => useContexto(ContextoEstado, 'useAtendimento')
export const useAcoes = () => useContexto(ContextoAcoes, 'useAcoes')
export const useCatalogo = () => useContexto(ContextoCatalogo, 'useCatalogo')
