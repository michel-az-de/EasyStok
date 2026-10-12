import { chamarApi } from './cliente'

export async function carregarNotificacoesDaSessao() {
  const [badge, recentes] = await Promise.all([
    chamarApi('/api/notificacoes/badge'), chamarApi('/api/notificacoes/recentes?limit=10'),
  ])
  if (!Number.isInteger(badge?.count) || badge.count < 0 || !Array.isArray(recentes)) {
    throw new Error('Os avisos não foram recebidos. Tente atualizar.')
  }
  return { total: badge.count, recentes }
}

export const lerNotificacaoDaSessao = (id) => chamarApi(`/api/notificacoes/${encodeURIComponent(id)}/lida`, { metodo: 'PATCH' })
