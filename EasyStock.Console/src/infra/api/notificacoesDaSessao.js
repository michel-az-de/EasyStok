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

// #3394: aba Todas do painel, lidas e não lidas, mais novas primeiro (NotificacaoController.GetAll). O total e as
// páginas vêm no `meta` do envelope.
export async function carregarTodasAsNotificacoes(pagina = 1, porPagina = 20) {
  const resposta = await chamarApi(`/api/notificacoes?page=${pagina}&pageSize=${porPagina}`, { envelope: true })
  if (!Array.isArray(resposta?.data) || !Number.isInteger(resposta?.meta?.total)) {
    throw new Error('A lista de avisos não foi recebida. Tente atualizar.')
  }
  return { itens: resposta.data, total: resposta.meta.total, paginas: resposta.meta.pages ?? 1, pagina }
}

export const lerNotificacaoDaSessao = (id) => chamarApi(`/api/notificacoes/${encodeURIComponent(id)}/lida`, { metodo: 'PATCH' })

// #3394: marca todas as não lidas desta pessoa, inclusive os avisos gerais, que valem para a equipe toda.
export const lerTodasAsNotificacoes = () => chamarApi('/api/notificacoes/marcar-todas', { metodo: 'POST' })
