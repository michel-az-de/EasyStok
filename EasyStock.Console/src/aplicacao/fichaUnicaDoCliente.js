import { createContext, useContext } from 'react'

// #2849: a ficha única do cliente mora em Cadastros. Feature não importa feature, então o App entrega aqui
// a função que desenha essa ficha; o Atendimento abre por ela, do mesmo jeito que o Buscar na Casa.
// Sem a função (cenas de teste, modo demonstração) quem chama segue com a ficha da conversa.
export const ContextoFichaUnicaDoCliente = createContext(null)
export const useFichaUnicaDoCliente = () => useContext(ContextoFichaUnicaDoCliente)
