import { createContext, useContext } from 'react'

// #2855: o painel do Assistente (uso e cota, fluxo, histórico) mora em Gestão › Atendimento. Feature não
// importa feature, então o App entrega aqui a função que desenha o painel e o modal da conversa abre por ela,
// do mesmo jeito que a ficha única do cliente (#2849). Sem a função (cenas de teste) o modal mostra só a conversa.
export const ContextoPainelDoAssistente = createContext(null)
export const usePainelDoAssistenteNaConversa = () => useContext(ContextoPainelDoAssistente)
