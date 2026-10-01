import { chamarApi } from './cliente'

// KDS do console (S19, `api/kds`) e canhoto (S20, `api/pedidos/{id}/canhoto`).
// Mudança de status passa pela máquina de estados da API; transição inválida
// volta 400 com a mensagem pronta para a tela.
export const listarPedidosKds = () => chamarApi('/api/kds/pedidos')

export const mudarStatusKds = (id, status) =>
  chamarApi(`/api/kds/pedidos/${id}/status`, { metodo: 'PATCH', corpo: { status } })

export const reimprimirCanhoto = (id) => chamarApi(`/api/pedidos/${id}/reimprimir`, { metodo: 'POST' })

// O canhoto HTML (80 mm, com @media print) não vem no envelope `{ data }`: é a página
// pronta. Passa pelo `chamarApi` (F07, item 10): 401 aqui também devolve para o login.
export const obterCanhotoHtml = (id) => chamarApi(`/api/pedidos/${id}/canhoto?formato=html`, { texto: true })
