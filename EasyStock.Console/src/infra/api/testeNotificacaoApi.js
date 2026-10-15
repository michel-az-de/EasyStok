import { chamarApi } from './cliente'

const BASE = '/api/admin/notificacoes/disparo-teste'
export const solicitarTesteEmail = motivo => chamarApi(BASE, {
  metodo: 'POST', corpo: { tipo: 'ContatoAlterado', motivo }, sinal: AbortSignal.timeout(15000),
})
export const consultarTesteEmail = eventoId => chamarApi(`${BASE}/${encodeURIComponent(eventoId)}`, { sinal: AbortSignal.timeout(15000) })
