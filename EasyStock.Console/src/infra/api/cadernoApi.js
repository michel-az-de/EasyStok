import { chamarApi } from './cliente'

// Caderno da loja (S54): `api/atendimento/caderno`. Não há exclusão: remover é arquivar.
const BASE = '/api/atendimento/caderno'

export const listarTrechosDoCaderno = () => chamarApi(BASE)
export const criarTrechoDoCaderno = (corpo) => chamarApi(BASE, { metodo: 'POST', corpo })
export const editarTrechoDoCaderno = (id, corpo) => chamarApi(`${BASE}/${encodeURIComponent(id)}`, { metodo: 'PUT', corpo })
export const arquivarTrechoDoCaderno = (id) =>
  chamarApi(`${BASE}/${encodeURIComponent(id)}/arquivar?arquivado=true`, { metodo: 'POST' })
