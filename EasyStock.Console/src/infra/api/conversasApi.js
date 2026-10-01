import { chamarApi } from './cliente'

// Inbox do console (S07): `api/atendimento/conversas`. Devolve o formato da API;
// a tradução para o formato do console mora em traducaoConversas.js.
const BASE = '/api/atendimento/conversas'

// A API pagina por `pagina` e corta `limite` em 100 (ListarConversasAtendimentoUseCase).
const POR_PAGINA = 100
const PAGINAS_NO_MAXIMO = 10

export const listarConversas = ({ limite = POR_PAGINA, pagina = 1 } = {}) =>
  chamarApi(`${BASE}?limite=${limite}&pagina=${pagina}`)

// Inbox inteira (F07, item 7): segue as páginas até uma vir incompleta. Conversa que
// subiu de página entre uma chamada e outra aparece uma vez só.
export async function listarTodasConversas() {
  const vistas = new Map()
  for (let pagina = 1; pagina <= PAGINAS_NO_MAXIMO; pagina += 1) {
    const itens = (await listarConversas({ pagina })) ?? []
    for (const item of itens) if (!vistas.has(item.id)) vistas.set(item.id, item)
    if (itens.length < POR_PAGINA) break
  }
  return [...vistas.values()]
}

export const listarMensagens = (id, { limite = 100 } = {}) =>
  chamarApi(`${BASE}/${id}/mensagens?limite=${limite}`)

export const enviarTexto = (id, texto) =>
  chamarApi(`${BASE}/${id}/mensagens`, { metodo: 'POST', corpo: { texto } })

export const assumir = (id) => chamarApi(`${BASE}/${id}/assumir`, { metodo: 'POST' })
export const liberarAutomatico = (id) => chamarApi(`${BASE}/${id}/liberar-automatico`, { metodo: 'POST' })
export const encerrar = (id) => chamarApi(`${BASE}/${id}/encerrar`, { metodo: 'POST' })
export const marcarLida = (id) => chamarApi(`${BASE}/${id}/marcar-lida`, { metodo: 'POST' })
