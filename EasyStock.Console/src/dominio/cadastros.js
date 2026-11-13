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
export const mudarDadosCadastro = (tela, dados, campo, valor) => ({ ...dados, [campo]: valor,
  ...(tela === 'produtos' && campo === 'categoriaId' && valor !== dados.categoriaId ? { subcategoriaId: null } : {}) })
export const abrirEdicaoCadastro = (tela, item, novaOperacao = () => crypto.randomUUID()) =>
  tela === 'bancos' ? { ...item, requestId: novaOperacao() } : item
export const relacionamentoCadastro = (cliente) => !Number.isInteger(cliente.orderCount) || cliente.orderCount < 0 ? 'Histórico não informado' : cliente.orderCount === 0 ? 'Lead' : 'Cliente'

export function corpoCliente(dados, empresaId) {
  if (dados.id && (!Number.isInteger(dados.revisao) || dados.revisao < 0)) throw new Error('Reabra o cadastro para conferir a versão antes de salvar.')
  return { ...campos(dados, ['nome', 'apt', 'endereco', 'telefone', 'email', 'documento', 'observacoes', 'nomeFantasia', 'inscricaoEstadual']), telefone: telefoneParaApi(dados.telefone), empresaId,
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
  return { ...campos(dados, ['nome', 'documento', 'email', 'telefone', 'contato', 'categoria', 'tipo', 'siteUrl', 'pedidoMinimo', 'fretePadrao', 'observacoes']), telefone: telefoneParaApi(dados.telefone),
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

// #2457 (U2): a tela mostra "(11) 98225-4398" e a API continua recebendo o E.164 ("+5511982254398") que ela grava.
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
  if (/^\d{10,11}$/.test(digitos)) return `+55${digitos}`
  return BRASIL_COM_DDI.test(digitos) ? `+${digitos}` : bruto
}

// #2457 (U3): o limite de cada coluna vira erro no próprio campo, com o tamanho atual. `maxLength` cortava o
// texto colado sem avisar (direção visual, seção 2). Fornecedor: e-mail com 150, o tamanho da coluna (B2).
const LIMITES_CADASTRO = {
  clientes: { nome: 150, telefone: 32, email: 255, documento: 30, nomeFantasia: 150, inscricaoEstadual: 20, endereco: 255, apt: 32 },
  fornecedores: { nome: 150, contato: 150, telefone: 20, email: 150, documento: 30, categoria: 120, tipo: 60, siteUrl: 255, pedidoMinimo: 120, fretePadrao: 120 },
  produtos: { nome: 180 },
  bancos: { nome: 150 },
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

export const TITULOS_CADASTROS = { clientes: 'Leads e clientes', produtos: 'Produtos', fornecedores: 'Fornecedores', bancos: 'Contas bancárias', pagamentos: 'Formas de pagamento', modelos: 'Modelos' }
export const NOMES_CANAIS_MODELO = { Email: 'E-mail', Sms: 'SMS', WhatsApp: 'WhatsApp', 1: 'E-mail', 2: 'SMS', 3: 'WhatsApp' }
export const EVENTOS_MODELO = [
  ['PedidoPagoConfirmado', 'Pagamento do pedido confirmado'], ['PedidoEmPreparo', 'Pedido em preparo'], ['PedidoSaiuParaEntrega', 'Pedido saiu para entrega'],
  ['PedidoEntregue', 'Pedido entregue'], ['ReembolsoEfetuado', 'Reembolso efetuado'], ['CampanhaMarketing', 'Campanha'],
  ['ProdutoVencendo', 'Produto próximo do vencimento'], ['AlertaEstoqueCritico', 'Estoque crítico'], ['ContaPagarVencendo', 'Conta a pagar vencendo'],
  ['ContaReceberVencendo', 'Conta a receber vencendo'],
].map(([valor, rotulo]) => ({ valor, rotulo }))
