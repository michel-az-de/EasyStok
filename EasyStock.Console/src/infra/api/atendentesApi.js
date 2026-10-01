import { chamarApi } from './cliente'

// Atendentes da empresa e transferência de conversa (F09, S41). Destino que não atende
// conversas volta 422 (`DESTINO_NAO_ATENDE`) com a mensagem da API.
export const listarAtendentes = async () => (await chamarApi('/api/atendimento/atendentes')) ?? []

export const transferirConversa = (conversaId, paraUsuarioId) =>
  chamarApi(`/api/atendimento/conversas/${conversaId}/transferir`, { metodo: 'POST', corpo: { paraUsuarioId } })
