import { chamarApi } from './cliente'

const BASE = '/api/acervo'
export const listarImagens = (pagina = 0) => chamarApi(`${BASE}/imagens?pagina=${pagina}`)
export const listarSolicitacoes = () => chamarApi(`${BASE}/solicitacoes`)
export const solicitarImagem = (dados) => chamarApi(`${BASE}/solicitacoes`, { metodo: 'POST', corpo: dados })
export const aprovarImagem = (id) => chamarApi(`${BASE}/imagens/${id}/aprovar`, { metodo: 'POST' })
export const vincularCanva = (id, url) => chamarApi(`${BASE}/imagens/${id}/canva`, { metodo: 'PUT', corpo: { url } })
export const publicarImagem = (id, dados) => chamarApi(`${BASE}/imagens/${id}/publicar`, { metodo: 'POST', corpo: dados })
export const historicoImagem = (id) => chamarApi(`${BASE}/imagens/${id}/historico`)
export const arquivoImagem = (id) => chamarApi(`${BASE}/imagens/${id}/arquivo`, { arquivo: true })
export const miniaturaImagem = (id) => chamarApi(`${BASE}/imagens/${id}/miniatura`, { arquivo: true })
export function adicionarImagem(file, dados) {
  const formulario = new FormData()
  formulario.append('file', file)
  formulario.append('dados', JSON.stringify(dados))
  return chamarApi(`${BASE}/imagens`, { metodo: 'POST', formulario })
}
