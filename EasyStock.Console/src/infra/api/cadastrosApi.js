import { chamarApi } from './cliente'
import { identidadeDaSessao, lerSessao } from './sessao'
import { gastoDaCarteiraDoMeta, notaDaApi, relacaoDoCliente, relacionamentoDoDossie } from './traducaoCliente'
import { chaveDeCriacaoCadastro, corpoCliente, corpoFornecedor, corpoProduto, parametrosDaOrdem, TAMANHO_PAGINA_CADASTRO, termoBuscaCadastro } from '../../dominio/cadastros'

const rotas = { clientes: '/api/clientes', produtos: '/api/produtos', fornecedores: '/api/fornecedores', bancos: '/api/cadastros/contas-bancarias', pagamentos: '/api/cadastros/metodos-pagamento', modelos: '/api/notificacoes/templates', etiquetas: '/api/etiquetas/templates' }
export const contextoCadastro = () => { const sessao = lerSessao(); return { empresaId: sessao?.empresa?.id, identidade: identidadeDaSessao(sessao) } }
const opcoes = (contexto, sinal) => ({ identidadeEsperada: contexto.identidade, sinal, prazoComCancelamento: true })
const query = (dados) => new URLSearchParams(Object.entries(dados).filter(([, v]) => v !== '' && v !== null && v !== undefined)).toString()

export async function listarCadastro(tela, { busca = '', pagina = 1, filtro = '', relacionamento = '', ordem = '' }, contexto, sinal) {
  const simples = ['bancos', 'pagamentos', 'etiquetas'].includes(tela)
  const pesquisaProduto = tela === 'produtos' && busca.trim()
  const parametros = simples ? '' : query({ empresaId: contexto.empresaId, page: pagina, pageSize: TAMANHO_PAGINA_CADASTRO,
    ...(pesquisaProduto ? { termo: busca, limite: 100 } : { search: termoBuscaCadastro(busca), ...parametrosDaOrdem(tela, ordem) }),
    ...(['clientes', 'fornecedores'].includes(tela) && filtro ? { ativo: filtro === 'ativos' } : {}),
    ...(tela === 'clientes' && relacionamento ? { relacionamento } : {}),
    ...(tela === 'produtos' && filtro ? { status: filtro === 'ativos' ? 'Ativo' : 'Inativo' } : {}),
    ...(tela === 'modelos' ? { apenasAtuais: true, ...(filtro ? { canal: filtro } : {}) } : {}),
  })
  const r = await chamarApi(`${rotas[tela]}${pesquisaProduto ? '/search' : ''}${parametros ? `?${parametros}` : ''}`, { ...opcoes(contexto, sinal), envelope: true })
  let itens = r?.data ?? []
  if (!Array.isArray(itens)) throw new Error('A lista não foi recebida. Tente carregar novamente.')
  if (simples && busca) itens = itens.filter(x => `${x.nome} ${x.instituicao ?? ''}`.toLocaleLowerCase('pt-BR').includes(busca.toLocaleLowerCase('pt-BR')))
  // #3254: na lista de clientes cada linha ganha `relacao` e a lista traz o gasto da carteira (denominador da barra).
  if (tela === 'clientes') itens = itens.map(x => ({ ...x, relacao: relacaoDoCliente(x) }))
  return { itens, gastoTotalDaCarteira: gastoDaCarteiraDoMeta(r?.meta), total: r?.meta?.totalCount ?? r?.meta?.total ?? itens.length, pagina, busca, pesquisaLimitada: Boolean(pesquisaProduto) }
}
// #2495 (S5): a API acha por "contém" em nome, telefone e documento; quem compara os dígitos é o domínio.
export async function buscarClientesParecidos(termo, contexto, sinal) {
  const r = await chamarApi(`${rotas.clientes}?${query({ empresaId: contexto.empresaId, search: termo, pageSize: 10 })}`, { ...opcoes(contexto, sinal), envelope: true })
  return Array.isArray(r?.data) ? r.data : []
}
// #2495 (S6): a ficha do cliente só adiciona e remove endereço e telefone extra; a API devolve só o id.
const subrecurso = (clienteId, sub, id) => `${rotas.clientes}/${encodeURIComponent(clienteId)}/${sub}${id ? `/${encodeURIComponent(id)}` : ''}`
export const adicionarEnderecoCliente = (clienteId, corpo, contexto) => chamarApi(subrecurso(clienteId, 'enderecos'), { ...opcoes(contexto), metodo: 'POST', corpo })
export const removerEnderecoCliente = (clienteId, id, contexto) => chamarApi(`${subrecurso(clienteId, 'enderecos', id)}?empresaId=${contexto.empresaId}`, { ...opcoes(contexto), metodo: 'DELETE' })
export const adicionarTelefoneCliente = (clienteId, corpo, contexto) => chamarApi(subrecurso(clienteId, 'telefones'), { ...opcoes(contexto), metodo: 'POST', corpo })
export const removerTelefoneCliente = (clienteId, id, contexto) => chamarApi(`${subrecurso(clienteId, 'telefones', id)}?empresaId=${contexto.empresaId}`, { ...opcoes(contexto), metodo: 'DELETE' })
// #2734: relacionamento do cliente no editor (dossiê S25, notas e tags), na mesma tradução da Ficha do Atendimento.
export async function obterRelacionamentoCliente(clienteId, contexto, sinal) {
  const lido = relacionamentoDoDossie(await chamarApi(`${subrecurso(clienteId, 'dossie')}?empresaId=${contexto.empresaId}`, opcoes(contexto, sinal)))
  if (!lido) throw new Error('O relacionamento deste cliente não foi recebido. Tente carregar novamente.')
  return lido
}
// A nota volta na tradução da Ficha e a tag como o servidor normalizou. Resposta sem corpo devolve nulo (a tela relê o dossiê).
export async function adicionarNotaCadastro(clienteId, texto, contexto) {
  const nota = await chamarApi(`${subrecurso(clienteId, 'notas')}?empresaId=${contexto.empresaId}`, { ...opcoes(contexto), metodo: 'POST', corpo: { texto } })
  return nota?.id ? notaDaApi(nota) : null
}
export async function adicionarTagCadastro(clienteId, tag, contexto) {
  const resposta = await chamarApi(`${subrecurso(clienteId, 'tags')}?empresaId=${contexto.empresaId}`, { ...opcoes(contexto), metodo: 'POST', corpo: { tag } })
  return resposta?.tag ?? null
}
export const removerTagCadastro = (clienteId, tag, contexto) => chamarApi(`${subrecurso(clienteId, 'tags', tag)}?empresaId=${contexto.empresaId}`, { ...opcoes(contexto), metodo: 'DELETE' })
export async function obterCadastro(tela, id, contexto, sinal) {
  const r = await chamarApi(`${rotas[tela]}/${encodeURIComponent(id)}?empresaId=${contexto.empresaId}`, opcoes(contexto, sinal))
  return tela === 'clientes' ? { ...r.cliente, detalhe: r } : r
}
// #2495 (S2): DELETE inativa (204) e POST /reativar volta; as duas são idempotentes na API.
export const alternarAtividadeCadastro = (tela, id, ativar, contexto) => chamarApi(
  `${rotas[tela]}/${encodeURIComponent(id)}${ativar ? '/reativar' : ''}?empresaId=${contexto.empresaId}`,
  { ...opcoes(contexto), metodo: ativar ? 'POST' : 'DELETE' })
export const listarCategoriasCadastro = (contexto, sinal) => chamarApi(`/api/categorias?empresaId=${contexto.empresaId}`, opcoes(contexto, sinal))
// #2495 (S4): o POST e o PUT levam `empresaId` no corpo; o DELETE leva na query.
export const criarCategoriaCadastro = (corpo, contexto) => chamarApi('/api/categorias', { ...opcoes(contexto), metodo: 'POST', corpo })
export const atualizarCategoriaCadastro = (corpo, contexto) => chamarApi(`/api/categorias/${encodeURIComponent(corpo.id)}`, { ...opcoes(contexto), metodo: 'PUT', corpo })
export const excluirCategoriaCadastro = (id, contexto) => chamarApi(`/api/categorias/${encodeURIComponent(id)}?empresaId=${contexto.empresaId}`, { ...opcoes(contexto), metodo: 'DELETE' })
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
// #2792: variáveis {{ x }} do tipo de evento. A rota é a do controller (api/notificacoes), não a do openapi antigo; só o admin lê.
export const variaveisDoModeloCadastro = (tipoEvento, contexto, sinal) => chamarApi(`/api/notificacoes/variaveis-catalogo?${query({ tipoEvento })}`, opcoes(contexto, sinal))
export const aprovarModeloCadastro = (id, contexto) => chamarApi(`${rotas.modelos}/${encodeURIComponent(id)}/aprovar`, { ...opcoes(contexto), metodo: 'POST' })
export const definirEtiquetaPadrao = (item, contexto) => chamarApi(`${rotas.etiquetas}/${item.origem}/${item.id}/set-default`, { ...opcoes(contexto), metodo: 'POST', corpo: { empresaId: contexto.empresaId } })
export const duplicarEtiquetaCadastro = (item, nome, contexto) => chamarApi(rotas.etiquetas, { ...opcoes(contexto), metodo: 'POST', corpo: { empresaId: contexto.empresaId, nome, layoutJson: item.layoutJson, baseSistemaId: item.origem === 'Sistema' ? item.id : item.baseSistemaId, definirComoPadrao: false } })
export const editarEtiquetaCadastro = (item, nome, contexto) => chamarApi(`${rotas.etiquetas}/${encodeURIComponent(item.id)}`, { ...opcoes(contexto), metodo: 'PUT', corpo: { empresaId: contexto.empresaId, id: item.id, nome, layoutJson: item.layoutJson } })
// #3299: o desempenho dos produtos (vendas de 90 dias, tendência, curva, parado e margem) e a faixa da tela Produtos.
export async function obterDesempenhoDosProdutos(contexto, sinal) {
  const r = await chamarApi(`${rotas.produtos}/desempenho?${query({ empresaId: contexto.empresaId })}`, opcoes(contexto, sinal))
  if (!r?.resumo || !Array.isArray(r?.produtos)) throw new Error('Os números dos produtos não carregaram.')
  return r
}
