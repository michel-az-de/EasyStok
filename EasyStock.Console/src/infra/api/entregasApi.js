import { chamarApi } from './cliente'

// Entregas no modo API (F04). Pedidos pelo KDS (S19, com endereço e aprovação),
// entregadores, viagens e chamados (S44, `api/atendimento`), aprovação manual
// (S12, `api/storefront/pedidos`) e o cadastro da loja (S45, `api/minha-vitrine/entrega`).
// Toda rota usa a empresa do token; a API confere cada id contra ela.
export const listarPedidosEntrega = (status) =>
  chamarApi(`/api/kds/pedidos?status=${encodeURIComponent(status)}`)

// Entregadores (policy Operador).
export const listarEntregadores = () => chamarApi('/api/atendimento/entregadores')
export const criarEntregador = (corpo) => chamarApi('/api/atendimento/entregadores', { metodo: 'POST', corpo })
export const desativarEntregador = (id) => chamarApi(`/api/atendimento/entregadores/${id}`, { metodo: 'DELETE' })

// Viagens: cada ação devolve a viagem montada de novo (com o link de rota).
const V = '/api/atendimento/viagens'
export const listarViagens = () => chamarApi(V)
export const criarViagem = (entregadorId) => chamarApi(V, { metodo: 'POST', corpo: { entregadorId: entregadorId ?? null } })
export const definirEntregadorViagem = (id, entregadorId) =>
  chamarApi(`${V}/${id}/entregador`, { metodo: 'PUT', corpo: { entregadorId: entregadorId || null } })
export const incluirParada = (id, pedidoId) => chamarApi(`${V}/${id}/paradas`, { metodo: 'POST', corpo: { pedidoId } })
export const retirarParada = (id, pedidoId) => chamarApi(`${V}/${id}/paradas/${pedidoId}`, { metodo: 'DELETE' })
export const reordenarParada = (id, pedidoId, ordem) =>
  chamarApi(`${V}/${id}/paradas/${pedidoId}/ordem`, { metodo: 'PUT', corpo: { ordem } })
export const sairParaEntrega = (id) => chamarApi(`${V}/${id}/sair`, { metodo: 'POST' })
export const marcarParadaEntregue = (id, pedidoId) => chamarApi(`${V}/${id}/paradas/${pedidoId}/entregue`, { metodo: 'POST' })
export const desfazerViagem = (id) => chamarApi(`${V}/${id}/desfazer`, { metodo: 'POST' })

// Chamados de entregador (texto livre).
const C = '/api/atendimento/chamados-entregador'
export const listarChamados = () => chamarApi(C)
export const abrirChamado = (texto, viagemId) => chamarApi(C, { metodo: 'POST', corpo: { texto, viagemId: viagemId ?? null } })
export const atenderChamado = (id) => chamarApi(`${C}/${id}/atender`, { metodo: 'POST' })
export const cancelarChamado = (id) => chamarApi(`${C}/${id}/cancelar`, { metodo: 'POST' })

// S12: a exceção (fora de área) que a dona aprova ou recusa.
export const aprovarPedido = (id) => chamarApi(`/api/storefront/pedidos/${id}/aprovar`, { metodo: 'POST', corpo: {} })
export const recusarPedido = (id, motivo) =>
  chamarApi(`/api/storefront/pedidos/${id}/recusar`, { metodo: 'POST', corpo: { motivo } })

// S45 (policy Admin): janelas, zonas de frete e bloqueios.
const E = '/api/minha-vitrine/entrega'
export const listarJanelas = () => chamarApi(`${E}/janelas`)
export const criarJanela = (corpo) => chamarApi(`${E}/janelas`, { metodo: 'POST', corpo })
export const definirJanelaAtiva = (id, ativa) => chamarApi(`${E}/janelas/${id}/ativa`, { metodo: 'POST', corpo: { ativa } })
export const listarZonas = () => chamarApi(`${E}/zonas`)
export const criarZona = (corpo) => chamarApi(`${E}/zonas`, { metodo: 'POST', corpo })
export const definirZonaAtiva = (id, ativa) => chamarApi(`${E}/zonas/${id}/ativa`, { metodo: 'POST', corpo: { ativa } })
export const listarBloqueios = (de, ate) => chamarApi(`${E}/bloqueios?de=${de}&ate=${ate}`)
export const criarBloqueio = (corpo) => chamarApi(`${E}/bloqueios`, { metodo: 'POST', corpo })
export const removerBloqueio = (id) => chamarApi(`${E}/bloqueios/${id}`, { metodo: 'DELETE' })
