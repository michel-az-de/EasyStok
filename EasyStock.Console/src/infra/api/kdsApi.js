import { API_BASE } from '../fonteDados'
import { ErroApi, chamarApi } from './cliente'
import { lerSessao } from './sessao'

// KDS do console (S19, `api/kds`) e canhoto (S20, `api/pedidos/{id}/canhoto`).
// Mudança de status passa pela máquina de estados da API; transição inválida
// volta 400 com a mensagem pronta para a tela.
export const listarPedidosKds = () => chamarApi('/api/kds/pedidos')

export const mudarStatusKds = (id, status) =>
  chamarApi(`/api/kds/pedidos/${id}/status`, { metodo: 'PATCH', corpo: { status } })

export const reimprimirCanhoto = (id) => chamarApi(`/api/pedidos/${id}/reimprimir`, { metodo: 'POST' })

// O canhoto HTML (80 mm, com @media print) não vem no envelope `{ data }`: é a página
// pronta. Busca com o JWT e devolve o texto para a tela abrir numa janela de impressão.
export async function obterCanhotoHtml(id) {
  const sessao = lerSessao()
  let resposta
  try {
    resposta = await fetch(`${API_BASE}/api/pedidos/${id}/canhoto?formato=html`, {
      headers: sessao ? { Authorization: `Bearer ${sessao.token}` } : {},
    })
  } catch {
    throw new ErroApi(0, 'SEM_CONEXAO', 'Sem conexão com o EasyStok.')
  }
  if (!resposta.ok) throw new ErroApi(resposta.status, `HTTP_${resposta.status}`, 'O canhoto não abriu. Tente de novo.')
  return resposta.text()
}
