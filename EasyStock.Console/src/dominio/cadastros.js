import { mascaraTelefone } from './formato.js'
const texto = (v) => typeof v === 'string' ? v.trim() || null : v ?? null
const campos = (dados, nomes) => Object.fromEntries(nomes.map(nome => [nome, texto(dados[nome])]))
export const normalizarBuscaCadastro = (valor) => String(valor ?? '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase('pt-BR')
export const termoBuscaCadastro = (valor) => /^[+\d\s().-]+$/.test(String(valor).trim()) ? String(valor).replace(/\D/g, '') : String(valor).trim()
export const chaveContextoCadastro = (tela, conversaId) => `cadastros:${tela}:${conversaId ?? 'geral'}`
export const telefoneCadastro = (c) => c.telefone || c.contatoInformado?.telefone || c.cliente?.telefoneCanal || (['WhatsApp', 'SMS'].includes(c.canal) ? c.contato : null)
export const emailCadastro = (c) => c.email || c.contatoInformado?.email || (c.canal === 'E-mail' ? c.contato : null)
export const filtrarSemCadastro = (conversas, busca) => {
  const termo = normalizarBuscaCadastro(termoBuscaCadastro(busca))
  return conversas.filter(c => !c.clienteId && (normalizarBuscaCadastro([c.nome, telefoneCadastro(c), emailCadastro(c), c.canal].join(' ')).includes(termo)
    || String(telefoneCadastro(c) ?? '').replace(/\D/g, '').includes(termo)))
}
// #2446 (M4): o `requestId` nasce ao abrir o editor e vale para a abertura inteira. Trocá-lo a cada tecla fazia a
// conta bancária duplicar quando a dona editava e salvava de novo depois de um timeout que tinha gravado.
// #2495 (S3): "Sem subcategoria" chega como '' do seletor e grava null.
export const mudarDadosCadastro = (tela, dados, campo, valor) => ({ ...dados, [campo]: campo === 'subcategoriaId' && valor === '' ? null : valor,
  ...(tela === 'produtos' && campo === 'categoriaId' && valor !== dados.categoriaId ? { subcategoriaId: null } : {}) })
export const abrirEdicaoCadastro = (tela, item, novaOperacao = () => crypto.randomUUID()) =>
  tela === 'bancos' ? { ...item, requestId: novaOperacao() } : item
// #2462 (M5): criar cadastro manda o `requestId` da abertura como Idempotency-Key. Repetir depois de um timeout
// devolve o cadastro já gravado; se os dados mudaram no meio (422), a dona confere a lista antes de repetir.
const CRIACAO_IDEMPOTENTE = ['clientes', 'fornecedores', 'produtos', 'modelos']
export const chaveDeCriacaoCadastro = (tela, dados) =>
  CRIACAO_IDEMPOTENTE.includes(tela) && !(dados.id ?? dados.produtoId) ? dados.requestId ?? null : null
export const bloqueiaRepeticaoDoCadastro = (tela, dados, erro) => {
  if (dados.id ?? dados.produtoId) return false
  if (erro?.codigo === 'IDEMPOTENCY_PAYLOAD_MISMATCH') return true
  const semResposta = erro?.status === 0 || erro?.status >= 500
  return semResposta && tela !== 'bancos' && !chaveDeCriacaoCadastro(tela, dados)
}
export const MENSAGEM_CADASTRO_REPETIDO = 'Este cadastro já foi enviado com outros dados. Confira a lista antes de salvar de novo.'
export const novoModeloCadastro = (novaOperacao = () => crypto.randomUUID()) =>
  ({ nome: '', codigo: '', canal: 'Email', tipoEvento: 'PedidoEntregue', assuntoTemplate: '', corpoTemplate: '', requestId: novaOperacao() })
export const relacionamentoCadastro = (cliente) => !Number.isInteger(cliente.orderCount) || cliente.orderCount < 0 ? 'Histórico não informado' : cliente.orderCount === 0 ? 'Lead' : 'Cliente'

export function corpoCliente(dados, empresaId) {
  if (dados.id && (!Number.isInteger(dados.revisao) || dados.revisao < 0)) throw new Error('Reabra o cadastro para conferir a versão antes de salvar.')
  return { ...campos(dados, ['nome', 'apt', 'endereco', 'telefone', 'email', 'documento', 'observacoes', 'nomeFantasia', 'inscricaoEstadual']), telefone: telefoneParaApi(dados.telefone), documento: documentoParaApi(dados.documento), empresaId,
    ...(dados.id ? { id: dados.id, revisao: dados.revisao } : {}), pessoaJuridica: dados.tipoPessoa === 'juridica', origem: 'console' }
}
export const CAMPOS_CLIENTE = { nome: 'Nome', tipoPessoa: 'Tipo de pessoa', telefone: 'Telefone', email: 'E-mail', documento: 'Documento', nomeFantasia: 'Nome fantasia', inscricaoEstadual: 'Inscrição estadual', endereco: 'Endereço', apt: 'Apartamento ou referência', observacoes: 'Observações' }
export function reconferirCadastroCliente(editor, atual) {
  if (!editor.dados.id || atual?.id !== editor.dados.id || !Number.isInteger(atual.revisao) || atual.revisao < 0)
    throw new Error('Não foi possível conferir a versão deste cadastro. Tente carregar novamente.')
  const alterados = Object.keys(CAMPOS_CLIENTE).filter(campo => texto(editor.dados[campo]) !== texto(editor.inicial[campo]))
  return { ...editor, inicial: atual, dados: { ...atual, ...Object.fromEntries(alterados.map(campo => [campo, editor.dados[campo]])) }, conflito: false, reconferido: true }
}
export function corpoFornecedor(dados, empresaId) {
  return { ...campos(dados, ['nome', 'documento', 'email', 'telefone', 'contato', 'categoria', 'tipo', 'siteUrl', 'pedidoMinimo', 'fretePadrao', 'observacoes']), telefone: telefoneParaApi(dados.telefone), documento: documentoParaApi(dados.documento),
    empresaId, ...(dados.id ? { fornecedorId: dados.id } : {}), leadTimeEstimadoDias: dados.leadTimeEstimadoDias === '' ? null : dados.leadTimeEstimadoDias ?? null, origem: 'console' }
}
export function corpoProduto(dados, empresaId) {
  return { ...campos(dados, ['nome', 'descricaoBase', 'marca', 'skuBase', 'codigoBarras', 'atributosJson', 'observacaoInterna']), empresaId,
    produtoId: dados.produtoId ?? dados.id, categoriaId: dados.categoriaId, subcategoriaId: dados.subcategoriaId ?? null,
    tipo: dados.tipo ?? 'Alimento', status: dados.status ?? 'Inativo', controlaValidade: dados.controlaValidade === true,
    dimensoes: dados.dimensoes ?? null, custoReferencia: dados.custoReferencia ?? null, precoReferencia: dados.precoReferencia ?? null,
    margemEstimada: dados.margemEstimada ?? null, tipoEmbalagem: dados.tipoEmbalagem ?? 'Avulso',
    // O contrato interpreta coleções não nulas como substituição integral. Cadastro não recria IDs com estoque.
    caracteristicas: null, embalagens: null, variacoes: null }
}

// #2457 (U2): a tela mostra "(11) 98225-4398"; a API recebe só os dígitos, como recebia antes da máscara. O DDI
// nunca é inventado: um telefone gravado sem "+" volta sem "+" (revisão da #2460). Com "+", segue E.164.
// Número de outro país (DDI que não é 55) fica como foi digitado; o que não parece telefone vai cru e a API diz o motivo.
const BRASIL_COM_DDI = /^55\d{10,11}$/
export function telefoneNaTela(valor) {
  const bruto = String(valor ?? '').trim()
  const digitos = bruto.replace(/\D/g, '')
  if (!digitos) return bruto.startsWith('+') ? bruto : ''
  if (bruto.startsWith('+') && !digitos.startsWith('55')) return bruto
  const nacional = bruto.startsWith('+') || BRASIL_COM_DDI.test(digitos) ? digitos.slice(2) : digitos
  if (!nacional) return bruto
  return nacional.length > 11 ? bruto : mascaraTelefone(nacional)
}
export function telefoneParaApi(valor) {
  const bruto = String(valor ?? '').trim()
  if (!bruto) return null
  const digitos = bruto.replace(/\D/g, '')
  if (bruto.startsWith('+')) return `+${digitos}`
  return digitos && /^[\d\s().-]+$/.test(bruto) ? digitos : bruto
}

// #2476 (U2): CPF "123.456.789-00" e CNPJ "12.345.678/0001-99" na tela. Só com 11 ou 14 dígitos, os que a API
// já normaliza (`Cnpj.TryFrom`): RG ou passaporte gravado (com letra, ou numérico de outro tamanho) fica como foi
// escrito e volta à API igual, para salvar outro campo não regravar o documento.
const SO_NUMERO_DE_DOCUMENTO = /^[\d\s./-]+$/
const juntarPartes = (partes, separadores) => partes.filter(Boolean).map((parte, i) => (i ? separadores[i - 1] : '') + parte).join('')
export function documentoNaTela(valor) {
  const bruto = String(valor ?? '').trim()
  const d = bruto.replace(/\D/g, '')
  if (!SO_NUMERO_DE_DOCUMENTO.test(bruto) || (d.length !== 11 && d.length !== 14)) return bruto
  if (d.length === 11) return juntarPartes([d.slice(0, 3), d.slice(3, 6), d.slice(6, 9), d.slice(9)], ['.', '.', '-'])
  return juntarPartes([d.slice(0, 2), d.slice(2, 5), d.slice(5, 8), d.slice(8, 12), d.slice(12)], ['.', '.', '/', '-'])
}
export function documentoParaApi(valor) {
  const bruto = String(valor ?? '').trim()
  const d = bruto.replace(/\D/g, '')
  if (!bruto || (!d && SO_NUMERO_DE_DOCUMENTO.test(bruto))) return null
  return SO_NUMERO_DE_DOCUMENTO.test(bruto) && (d.length === 11 || d.length === 14) ? d : bruto
}

// #2476 (U6): a pílula é só do estado que pede ação (Pilula.module.css): o contato sem cadastro e o Lead, que
// ainda não comprou. Ativo, Inativo e Cliente são texto.
export const emAtividadeCadastro = (item) => item.ativo !== false && item.status !== 'Inativo' && item.status !== 1
// #2495 (S2, I1): inativar e reativar pelo editor. Só cliente e fornecedor usam DELETE e /reativar; produto tem
// a Situação no formulário e conta bancária a caixa Conta ativa.
const SINGULAR_ATIVIDADE = { clientes: 'cliente', fornecedores: 'fornecedor' }
export const temAtividadeNoEditor = (tela, dados) => Boolean(SINGULAR_ATIVIDADE[tela] && dados?.id)
export function confirmacaoDeAtividade(tela, item) {
  const ativar = !emAtividadeCadastro(item)
  const nome = item.nome || `este ${SINGULAR_ATIVIDADE[tela]}`
  const verbo = ativar ? 'Reativar' : 'Inativar'
  return {
    ativar,
    titulo: `${verbo} ${nome}?`,
    texto: ativar ? `${nome} volta para a lista de ativos.`
      : `${nome} deixa de aparecer entre os ativos. O histórico e os pedidos ficam guardados, e dá para reativar depois.`,
    botao: `${verbo} ${SINGULAR_ATIVIDADE[tela]}`,
    sucesso: `${item.nome ? `O cadastro de ${item.nome}` : 'O cadastro'} foi ${ativar ? 'reativado' : 'inativado'}.`,
  }
}
// A API responde sem acento e o 403 chega sem corpo: a frase diz o que fazer.
export function mensagemDeAtividade(tela, ativar, erro) {
  if (erro?.status === 403) return `Seu acesso não permite ${ativar ? 'reativar' : 'inativar'} ${SINGULAR_ATIVIDADE[tela]}. Peça a quem administra a loja.`
  if (tela === 'fornecedores' && !ativar && /pedido aberto/i.test(erro?.message ?? '')) return 'Este fornecedor tem pedido de compra aberto ou em trânsito. Conclua ou cancele o pedido antes de inativar.'
  return erro?.message || 'Não deu para concluir agora. Tente de novo.'
}
export function situacaoDaLinha(tela, item, soLeads) {
  if (soLeads) return { texto: 'Sem cadastro', tom: 'aviso' }
  if (!emAtividadeCadastro(item)) return { texto: 'Inativo', tom: null }
  if (tela !== 'clientes') return { texto: 'Ativo', tom: null }
  const relacao = relacionamentoCadastro(item)
  return { texto: relacao, tom: relacao === 'Lead' ? 'marca' : null }
}

// #2476 (A4): um tamanho de página para o pedido à API e para o "Próxima" das listas.
export const TAMANHO_PAGINA_CADASTRO = 20
export const temProximaPagina = (pagina, total) => pagina * TAMANHO_PAGINA_CADASTRO < total

// #2495 (S1, I6): ordens que a API aceita em cada lista (Cliente/Fornecedor/ProdutoRepository). A primeira, de
// valor vazio, é a padrão da rota e não manda `sort`: a lista abre como sempre abriu. "Último pedido" só no
// crescente, porque o Postgres põe NULL primeiro no DESC e a lista abriria com quem nunca comprou.
const NOME_AZ = { valor: 'nome-az', rotulo: 'Nome (A a Z)', sort: 'nome', order: 'asc' }
const NOME_ZA = { valor: 'nome-za', rotulo: 'Nome (Z a A)', sort: 'nome', order: 'desc' }
const MAIS_ANTIGOS = { valor: 'antigos', rotulo: 'Cadastrados primeiro', sort: 'criadoem', order: 'asc' }
export const ORDENS_CADASTRO = {
  clientes: [
    { valor: '', rotulo: 'Nome (A a Z)', sort: 'nome', order: 'asc' }, NOME_ZA,
    { valor: 'recentes', rotulo: 'Cadastrados por último', sort: 'criadoem', order: 'desc' }, MAIS_ANTIGOS,
    { valor: 'mais-pedidos', rotulo: 'Mais pedidos', sort: 'ordercount', order: 'desc' },
    { valor: 'sem-comprar', rotulo: 'Sem comprar há mais tempo', sort: 'lastorderat', order: 'asc' },
  ],
  fornecedores: [{ valor: '', rotulo: 'Cadastrados por último', sort: 'criadoem', order: 'desc' }, MAIS_ANTIGOS, NOME_AZ, NOME_ZA],
  produtos: [
    { valor: '', rotulo: 'Cadastrados por último', sort: 'criadoem', order: 'desc' }, MAIS_ANTIGOS,
    { valor: 'alterados', rotulo: 'Alterados por último', sort: 'alteradoem', order: 'desc' }, NOME_AZ, NOME_ZA,
  ],
}
const ordemEscolhida = (tela, valor) => ORDENS_CADASTRO[tela]?.find((o) => o.valor === valor) ?? ORDENS_CADASTRO[tela]?.[0] ?? null
export function parametrosDaOrdem(tela, valor) {
  const ordem = ordemEscolhida(tela, valor)
  return ordem?.valor ? { sort: ordem.sort, order: ordem.order } : {}
}
// Cabeçalho Nome: o estado para `aria-sort` e a ordem do próximo clique (A a Z, depois Z a A).
export function sortDoNome(tela, valor) {
  const ordem = ordemEscolhida(tela, valor)
  return ordem?.sort !== 'nome' ? 'none' : ordem.order === 'desc' ? 'descending' : 'ascending'
}
export function ordemDoCabecalhoNome(tela, valor) {
  const az = ORDENS_CADASTRO[tela]?.find((o) => o.sort === 'nome' && o.order === 'asc')?.valor
  const za = ORDENS_CADASTRO[tela]?.find((o) => o.sort === 'nome' && o.order === 'desc')?.valor
  return sortDoNome(tela, valor) === 'ascending' ? za : az
}

// #2457 (U3): o limite de cada coluna vira erro no próprio campo, com o tamanho atual. `maxLength` cortava o
// texto colado sem avisar (direção visual, seção 2). Fornecedor: e-mail com 150, o tamanho da coluna (B2).
const LIMITES_CADASTRO = {
  clientes: { nome: 150, telefone: 32, email: 255, documento: 30, nomeFantasia: 150, inscricaoEstadual: 20, endereco: 255, apt: 32 },
  fornecedores: { nome: 150, contato: 150, telefone: 20, email: 150, documento: 30, categoria: 120, tipo: 60, siteUrl: 255, pedidoMinimo: 120, fretePadrao: 120 },
  produtos: { nome: 180 },
  bancos: { nome: 150 },
}
// #2495 (S3, I2): GET /api/categorias devolve as raízes com as filhas de um nível. O produto oferece só as filhas
// da categoria escolhida; sem filhas, o seletor some.
export const categoriasRaiz = (categorias) => (categorias ?? []).filter(c => !c.categoriaPaiId).map(c => ({ valor: c.id, rotulo: c.nome }))
export function subcategoriasDe(categorias, categoriaId) {
  const filhas = (categorias ?? []).find(c => c.id === categoriaId)?.subCategorias ?? []
  return filhas.length ? [{ valor: '', rotulo: 'Sem subcategoria' }, ...filhas.map(s => ({ valor: s.id, rotulo: s.nome }))] : []
}
export const OBRIGATORIOS_CADASTRO = { clientes: ['nome'], fornecedores: ['nome'], produtos: ['nome', 'categoriaId'], bancos: ['nome'] }
export function errosDoFormularioCadastro(tela, dados) {
  const erros = {}
  for (const [campo, limite] of Object.entries(LIMITES_CADASTRO[tela] ?? {})) {
    const tamanho = String(dados[campo] ?? '').trim().length
    if (tamanho > limite) erros[campo] = `Use até ${limite} caracteres. Agora tem ${tamanho}.`
  }
  if (!String(dados.nome ?? '').trim()) erros.nome = 'Informe o nome.'
  if (tela === 'produtos' && !dados.categoriaId) erros.categoriaId = 'Escolha uma categoria.'
  if (tela === 'produtos' && dados.id && dados.status === 'Ativo' && !(dados.precoReferencia > 0)) erros.precoReferencia = 'Informe o preço para ativar o produto.'
  return erros
}

// #2457 (U5): textos de cada lista. Conta bancária não tem telefone nem documento para buscar.
export const TEXTOS_CADASTRO = {
  clientes: { busca: 'Nome, telefone ou documento', novo: 'Novo cliente', vazio: 'Cadastre a primeira pessoa ou traga um contato do Atendimento.' },
  produtos: { busca: 'Nome, marca, código ou SKU', novo: 'Novo produto', vazio: 'Cadastre o primeiro produto para usar no cardápio e no estoque.' },
  fornecedores: { busca: 'Nome, telefone ou documento', novo: 'Novo fornecedor', vazio: 'Cadastre o primeiro fornecedor para registrar compras.' },
  bancos: { busca: 'Nome ou instituição', novo: 'Nova conta', vazio: 'Cadastre a conta que recebe os pagamentos da loja.' },
}

export const TITULOS_CADASTROS = { clientes: 'Leads e clientes', produtos: 'Produtos', categorias: 'Categorias de produto', fornecedores: 'Fornecedores', bancos: 'Contas bancárias', pagamentos: 'Formas de pagamento', modelos: 'Modelos' }
export const NOMES_CANAIS_MODELO = { Email: 'E-mail', Sms: 'SMS', WhatsApp: 'WhatsApp', 1: 'E-mail', 2: 'SMS', 3: 'WhatsApp' }
export const EVENTOS_MODELO = [
  ['PedidoPagoConfirmado', 'Pagamento do pedido confirmado'], ['PedidoEmPreparo', 'Pedido em preparo'], ['PedidoSaiuParaEntrega', 'Pedido saiu para entrega'],
  ['PedidoEntregue', 'Pedido entregue'], ['ReembolsoEfetuado', 'Reembolso efetuado'], ['CampanhaMarketing', 'Campanha'],
  ['ProdutoVencendo', 'Produto próximo do vencimento'], ['AlertaEstoqueCritico', 'Estoque crítico'], ['ContaPagarVencendo', 'Conta a pagar vencendo'],
  ['ContaReceberVencendo', 'Conta a receber vencendo'],
].map(([valor, rotulo]) => ({ valor, rotulo }))
