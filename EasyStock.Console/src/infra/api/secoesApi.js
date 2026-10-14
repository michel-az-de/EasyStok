import { chamarApi } from './cliente'

// Categorias do cardápio (seções, M1.3 #1483). Rotas do Gerente; a ordem é renumerada no servidor.
const SECOES = '/api/atendimento/comanda/cardapio/secoes'
const SECAO = (id) => `${SECOES}/${id}`
const DIRECAO_DA_API = { subir: 'Subir', descer: 'Descer' }

export const listarSecoes = () => chamarApi(SECOES)
export const criarSecao = (nome) => chamarApi(SECOES, { metodo: 'POST', corpo: { nome } })
export const renomearSecao = (id, nome) => chamarApi(SECAO(id), { metodo: 'PUT', corpo: { nome } })
export const definirSecaoVisivel = (id, visivel) => chamarApi(`${SECAO(id)}/visivel`, { metodo: 'POST', corpo: { visivel } })
export const moverSecao = (id, direcao) =>
  chamarApi(`${SECAO(id)}/mover`, { metodo: 'POST', corpo: { direcao: DIRECAO_DA_API[direcao] } })
export const excluirSecao = (id) => chamarApi(SECAO(id), { metodo: 'DELETE' })
export const migrarCategorias = () => chamarApi(`${SECOES}/migrar-categorias`, { metodo: 'POST' })
// #1803: o que a conversão faria agora (só lê). Mesmos textos e contagens que a conversão usa.
export const previaMigracaoCategorias = () => chamarApi(`${SECOES}/migrar-categorias/previa`)

export const secaoDaApi = (s) => ({ id: s.secaoId, nome: s.nome, ordem: s.ordem, visivel: s.visivel, itens: s.itens })
export const previaDaApi = (p) => ({
  pratos: p?.pratos ?? 0,
  categorias: (p?.categorias ?? []).map((c) => ({ nome: c.nome, pratos: c.pratos, existe: c.existe === true })),
})
