import { chamarApi } from './cliente'

// Inbox do console (S07): `api/atendimento/conversas`. Devolve o formato da API;
// a tradução para o formato do console mora em traducaoConversas.js.
const BASE = '/api/atendimento/conversas'

export const listarConversas = ({ limite = 50 } = {}) => chamarApi(`${BASE}?limite=${limite}`)

export const listarMensagens = (id, { limite = 100 } = {}) =>
  chamarApi(`${BASE}/${id}/mensagens?limite=${limite}`)

export const enviarTexto = (id, texto) =>
  chamarApi(`${BASE}/${id}/mensagens`, { metodo: 'POST', corpo: { texto } })

export const assumir = (id) => chamarApi(`${BASE}/${id}/assumir`, { metodo: 'POST' })
export const liberarAutomatico = (id) => chamarApi(`${BASE}/${id}/liberar-automatico`, { metodo: 'POST' })
export const encerrar = (id) => chamarApi(`${BASE}/${id}/encerrar`, { metodo: 'POST' })
export const marcarLida = (id) => chamarApi(`${BASE}/${id}/marcar-lida`, { metodo: 'POST' })
