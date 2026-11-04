import { chamarApi } from './cliente'
import { lerSessao } from './sessao'
import { instanteFinanceiro, extratoFinanceiro, STATUS_EM_ABERTO } from '../../dominio/financeiro'

const baseConta = (lado) => {
  if (!['pagar', 'receber'].includes(lado)) throw new Error('Tipo de conta inválido.')
  return `/api/contas-a-${lado}`
}
const corpoDaEmpresa = (corpo) => {
  const empresaId = lerSessao()?.empresa?.id
  if (!empresaId) throw new Error('Entre novamente na empresa para continuar.')
  return { ...corpo, empresaId }
}
const escrever = (url, corpo, metodo = 'POST') => chamarApi(url, { metodo, corpo: corpoDaEmpresa(corpo) })
const query = (filtros) => new URLSearchParams(Object.entries(filtros).filter(([, valor]) => valor !== '' && valor != null)).toString()

async function pagina(url, filtros) {
  const resposta = await chamarApi(`${url}?${query(filtros)}`, { envelope: true })
  if (!Array.isArray(resposta?.data) || !Number.isFinite(resposta?.meta?.total)) throw new Error('A lista financeira não foi recebida por completo.')
  return { itens: resposta.data, total: resposta.meta.total }
}
async function todas(url, filtros = {}) {
  const itens = []
  const ids = new Set()
  let numero = 1, total
  do {
    const atual = await pagina(url, { ...filtros, page: numero, pageSize: 100 })
    if (total !== undefined && total !== atual.total) throw new Error('A lista financeira mudou. Atualize para obter o extrato completo.')
    total = atual.total
    if (!atual.itens.length && itens.length < total) throw new Error('A lista financeira mudou. Atualize para obter o extrato completo.')
    for (const item of atual.itens) {
      if (ids.has(item.id)) throw new Error('A lista financeira mudou. Atualize para obter o extrato completo.')
      ids.add(item.id)
      itens.push(item)
    }
    numero += 1
  } while (itens.length < total)
  return itens
}

export const listarContasFinanceiras = (lado, filtros) => pagina(baseConta(lado), { pageSize: 20, ...filtros })
// #2095: lista de um filtro para o recorte e a ordem feitos no console. Não depende da ordem do servidor
// entre páginas: junta por id e, se uma conta mudar de página no meio, a próxima recarga acerta.
async function semRepetir(url, filtros) {
  const porId = new Map()
  for (let numero = 1; numero <= 50; numero += 1) {
    const atual = await pagina(url, { ...filtros, page: numero, pageSize: 100 })
    for (const item of atual.itens) porId.set(item.id, item)
    if (atual.itens.length < 100 || numero * 100 >= atual.total) break
  }
  return [...porId.values()]
}
// Só as em aberto, uma chamada por status em paralelo (as pagas ficam no servidor).
export async function listarContasEmAberto(lado, filtros = {}) {
  const listas = await Promise.all(STATUS_EM_ABERTO.map((status) => semRepetir(baseConta(lado), { ...filtros, status })))
  return [...new Map(listas.flat().map((conta) => [conta.id, conta])).values()]
}
export const obterContaFinanceira = (lado, id) => chamarApi(`${baseConta(lado)}/${id}`)
export const salvarContaFinanceira = (lado, corpo, id = null) => escrever(`${baseConta(lado)}${id ? '/' + id : ''}`, id ? { ...corpo, id } : corpo, id ? 'PATCH' : 'POST')
export const emitirContaFinanceira = (lado, id) => chamarApi(`${baseConta(lado)}/${id}/emitir`, { metodo: 'POST' })
export const cancelarContaFinanceira = (lado, id, motivo) => escrever(`${baseConta(lado)}/${id}/cancelar`, { motivo })
export const registrarParcelaFinanceira = (lado, contaId, parcelaId, corpo) => escrever(`${baseConta(lado)}/${contaId}/parcelas/${parcelaId}/pagamentos`, corpo)
export const estornarParcelaFinanceira = (lado, parcelaId, pagamentoId, motivo) => escrever(`${baseConta(lado)}/parcelas/${parcelaId}/pagamentos/${pagamentoId}/estornar`, { motivo })
export const adicionarParcelaFinanceira = (lado, id, corpo) => escrever(`${baseConta(lado)}/${id}/parcelas`, { ...corpo, [lado === 'pagar' ? 'contaPagarId' : 'contaReceberId']: id })
export const removerParcelaFinanceira = (lado, id, parcelaId) => chamarApi(`${baseConta(lado)}/${id}/parcelas/${parcelaId}`, { metodo: 'DELETE' })
export const obterResumoFinanceiro = () => chamarApi('/api/financeiro/dashboard')
export const obterRelatorioFinanceiro = ({ inicio, fim, periodicidade, categoriaId, centroCustoId }) => chamarApi(`/api/financeiro/fluxo-caixa?${query({ inicio: instanteFinanceiro(inicio), fim: instanteFinanceiro(fim, true), periodicidade, categoriaId, centroCustoId })}`)
export const listarCategoriasFinanceiras = () => chamarApi('/api/financeiro/categorias')
export const listarCentrosFinanceiros = () => chamarApi('/api/financeiro/centros-custo')
export const salvarCadastroFinanceiro = (tipo, corpo, id) => escrever(`/api/financeiro/${tipo}${id ? '/' + id : ''}`, id ? { ...corpo, id } : corpo, id ? 'PATCH' : 'POST')
export const inativarCadastroFinanceiro = (tipo, id) => chamarApi(`/api/financeiro/${tipo}/${id}/inativar`, { metodo: 'POST' })
export const reativarCadastroFinanceiro = (tipo, id) => chamarApi(`/api/financeiro/${tipo}/${id}/reativar`, { metodo: 'POST' })

export async function obterExtratoFinanceiro({ inicio, fim }, acesso) {
  const [pagar, receber, movimentos] = await Promise.all([
    acesso.visualizarContasPagar ? todas(baseConta('pagar')) : [],
    acesso.visualizarContasReceber ? todas(baseConta('receber')) : [],
    todas('/api/caixa/movimentos', { desde: instanteFinanceiro(inicio), ate: instanteFinanceiro(fim, true), incluirEstornados: true }),
  ])
  return extratoFinanceiro([...pagar.map((conta) => ({ lado: 'pagar', conta })), ...receber.map((conta) => ({ lado: 'receber', conta }))], movimentos, inicio, fim)
}
