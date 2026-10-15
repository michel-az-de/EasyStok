import { chamarApi } from './cliente'

const BASE = '/api/acervo'
export const listarImagens = (pagina = 0, filtros = {}) => chamarApi(`${BASE}/imagens?${new URLSearchParams({ pagina, ...filtros })}`)
export const listarSolicitacoes = () => chamarApi(`${BASE}/solicitacoes?incluirConcluidas=true`)
export const listarCobertura = () => chamarApi(`${BASE}/cobertura`)
export const listarBriefings = () => chamarApi(`${BASE}/briefings`)
export const listarUsos = () => chamarApi(`${BASE}/usos`)
export const salvarBriefing = (dados) => chamarApi(`${BASE}/briefings`, { metodo: 'POST', corpo: dados })
export const enfileirarBriefing = (id, operacaoId) => chamarApi(`${BASE}/briefings/${id}/solicitacoes`, { metodo: 'POST', corpo: { operacaoId } })
export const confirmarUso = (id, dados) => chamarApi(`${BASE}/imagens/${id}/uso`, { metodo: 'PUT', corpo: dados })
export const classificarImagem = (id, natureza, naturezaEsperada) => chamarApi(`${BASE}/imagens/${id}/natureza`, { metodo: 'PUT', corpo: { natureza, naturezaEsperada } })
export const solicitarImagem = (dados) => chamarApi(`${BASE}/solicitacoes`, { metodo: 'POST', corpo: dados })
export const aprovarImagem = (id) => chamarApi(`${BASE}/imagens/${id}/aprovar`, { metodo: 'POST' })
export const vincularCanva = (id, url) => chamarApi(`${BASE}/imagens/${id}/canva`, { metodo: 'PUT', corpo: { url } })
export const publicarImagem = (id, dados) => chamarApi(`${BASE}/imagens/${id}/publicar`, { metodo: 'POST', corpo: dados })
// #1808: só propõem; a publicação continua em publicarImagem, depois da confirmação.
export const sugerirAssociacoes = () => chamarApi(`${BASE}/sugestoes`, { metodo: 'POST' })
export const dicaDoPrato = (itemId) => chamarApi(`${BASE}/itens/${itemId}/dica`, { metodo: 'POST' })
export const historicoImagem = (id) => chamarApi(`${BASE}/imagens/${id}/historico`)
export const arquivoImagem = (id) => chamarApi(`${BASE}/imagens/${id}/arquivo`, { arquivo: true })
export const miniaturaImagem = (id) => chamarApi(`${BASE}/imagens/${id}/miniatura`, { arquivo: true })
export function adicionarImagem(file, dados) {
  const formulario = new FormData()
  formulario.append('file', file)
  formulario.append('dados', JSON.stringify(dados))
  return chamarApi(`${BASE}/imagens`, { metodo: 'POST', formulario })
}
