import { chamarApi } from './cliente'

// Mensagens padrão locais (#3204/#3206): modelos no formato da Meta guardados no EasyStok.
const MODELOS = '/api/atendimento/modelos-locais'

export const listarMensagensPadrao = () => chamarApi(`${MODELOS}?arquivados=true`)
export const criarMensagemPadrao = (corpo) => chamarApi(MODELOS, { metodo: 'POST', corpo })
export const editarMensagemPadrao = (id, corpo) => chamarApi(`${MODELOS}/${id}`, { metodo: 'PUT', corpo })
export const arquivarMensagemPadrao = (id, arquivado) => chamarApi(`${MODELOS}/${id}/arquivar?arquivado=${arquivado}`, { metodo: 'POST' })
