import { chamarApi } from './cliente'
import { identidadeDaSessao, lerSessao } from './sessao'
import { chaveDeCriacaoCadastro, corpoCliente, corpoFornecedor, corpoProduto, termoBuscaCadastro } from '../../dominio/cadastros'

const rotas = { clientes: '/api/clientes', produtos: '/api/produtos', fornecedores: '/api/fornecedores', bancos: '/api/cadastros/contas-bancarias', pagamentos: '/api/cadastros/metodos-pagamento', modelos: '/api/notificacoes/templates', etiquetas: '/api/etiquetas/templates' }
export const contextoCadastro = () => { const sessao = lerSessao(); return { empresaId: sessao?.empresa?.id, identidade: identidadeDaSessao(sessao) } }
const opcoes = (contexto, sinal) => ({ identidadeEsperada: contexto.identidade, sinal, prazoComCancelamento: true })
const query = (dados) => new URLSearchParams(Object.entries(dados).filter(([, v]) => v !== '' && v !== null && v !== undefined)).toString()

export async function listarCadastro(tela, { busca = '', pagina = 1, filtro = '', relacionamento = '' }, contexto, sinal) {
  const simples = ['bancos', 'pagamentos', 'etiquetas'].includes(tela)
  const pesquisaProduto = tela === 'produtos' && busca.trim()
  const parametros = simples ? '' : query({ empresaId: contexto.empresaId, page: pagina, pageSize: 20,
    ...(pesquisaProduto ? { termo: busca, limite: 100 } : { search: termoBuscaCadastro(busca) }),
    ...(['clientes', 'fornecedores'].includes(tela) && filtro ? { ativo: filtro === 'ativos' } : {}),
    ...(tela === 'clientes' && relacionamento ? { relacionamento } : {}),
    ...(tela === 'produtos' && filtro ? { status: filtro === 'ativos' ? 'Ativo' : 'Inativo' } : {}),
    ...(tela === 'modelos' ? { apenasAtuais: true, ...(filtro ? { canal: filtro } : {}) } : {}),
  })
  const r = await chamarApi(`${rotas[tela]}${pesquisaProduto ? '/search' : ''}${parametros ? `?${parametros}` : ''}`, { ...opcoes(contexto, sinal), envelope: true })
  let itens = r?.data ?? []
  if (!Array.isArray(itens)) throw new Error('A lista não foi recebida. Tente carregar novamente.')
  if (simples && busca) itens = itens.filter(x => `${x.nome} ${x.instituicao ?? ''}`.toLocaleLowerCase('pt-BR').includes(busca.toLocaleLowerCase('pt-BR')))
  return { itens, total: r?.meta?.totalCount ?? r?.meta?.total ?? itens.length, pagina, busca, pesquisaLimitada: Boolean(pesquisaProduto) }
}
export async function obterCadastro(tela, id, contexto, sinal) {
  const r = await chamarApi(`${rotas[tela]}/${encodeURIComponent(id)}?empresaId=${contexto.empresaId}`, opcoes(contexto, sinal))
  return tela === 'clientes' ? { ...r.cliente, detalhe: r } : r
}
export const listarCategoriasCadastro = (contexto, sinal) => chamarApi(`/api/categorias?empresaId=${contexto.empresaId}`, opcoes(contexto, sinal))
export async function salvarCadastro(tela, dados, contexto) {
  const id = dados.id ?? dados.produtoId
  let corpo, metodo = id ? 'PATCH' : 'POST'
  if (tela === 'clientes') corpo = corpoCliente(dados, contexto.empresaId)
  if (tela === 'fornecedores') corpo = corpoFornecedor(dados, contexto.empresaId)
  if (tela === 'produtos') corpo = corpoProduto(dados, contexto.empresaId)
  if (tela === 'bancos') corpo = { ...dados, empresaId: contexto.empresaId }
  if (tela === 'modelos') {
    metodo = id ? 'PUT' : 'POST'
    corpo = id ? { novoAssunto: dados.assuntoTemplate, novoCorpo: dados.corpoTemplate }
      : { codigo: dados.codigo, nome: dados.nome, canal: dados.canal, tipoEvento: dados.tipoEvento, assuntoTemplate: dados.assuntoTemplate ?? '', corpoTemplate: dados.corpoTemplate, idioma: 'pt-BR' }
  }
  const chave = chaveDeCriacaoCadastro(tela, dados)
  return chamarApi(`${rotas[tela]}${id ? `/${encodeURIComponent(id)}` : ''}`, { ...opcoes(contexto), metodo, corpo, ...(chave ? { chave } : {}) })
}
export const ativarMetodoCadastro = (metodo, ativo, requestId, contexto) => chamarApi(`${rotas.pagamentos}/${encodeURIComponent(metodo.codigo)}`, {
  ...opcoes(contexto), metodo: 'PUT', corpo: { ativo, revisao: metodo.revisao, requestId },
})
export const previaModeloCadastro = (dados, contexto, sinal) => chamarApi('/api/notificacoes/templates/preview-draft', {
  ...opcoes(contexto, sinal), metodo: 'POST', corpo: { assuntoTemplate: dados.assuntoTemplate ?? '', corpoTemplate: dados.corpoTemplate, variaveis: {} },
})
export const aprovarModeloCadastro = (id, contexto) => chamarApi(`${rotas.modelos}/${encodeURIComponent(id)}/aprovar`, { ...opcoes(contexto), metodo: 'POST' })
export const definirEtiquetaPadrao = (item, contexto) => chamarApi(`${rotas.etiquetas}/${item.origem}/${item.id}/set-default`, { ...opcoes(contexto), metodo: 'POST', corpo: { empresaId: contexto.empresaId } })
export const duplicarEtiquetaCadastro = (item, nome, contexto) => chamarApi(rotas.etiquetas, { ...opcoes(contexto), metodo: 'POST', corpo: { empresaId: contexto.empresaId, nome, layoutJson: item.layoutJson, baseSistemaId: item.origem === 'Sistema' ? item.id : item.baseSistemaId, definirComoPadrao: false } })
export const editarEtiquetaCadastro = (item, nome, contexto) => chamarApi(`${rotas.etiquetas}/${encodeURIComponent(item.id)}`, { ...opcoes(contexto), metodo: 'PUT', corpo: { empresaId: contexto.empresaId, id: item.id, nome, layoutJson: item.layoutJson } })
