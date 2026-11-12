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
export const relacionamentoCadastro = (cliente) => !Number.isInteger(cliente.orderCount) || cliente.orderCount < 0 ? 'Histórico não informado' : cliente.orderCount === 0 ? 'Lead' : 'Cliente'

export function corpoCliente(dados, empresaId) {
  return { ...campos(dados, ['nome', 'apt', 'endereco', 'telefone', 'email', 'documento', 'observacoes', 'nomeFantasia', 'inscricaoEstadual']), empresaId,
    ...(dados.id ? { id: dados.id } : {}), pessoaJuridica: dados.tipoPessoa === 'juridica', origem: 'console' }
}
export function corpoFornecedor(dados, empresaId) {
  return { ...campos(dados, ['nome', 'documento', 'email', 'telefone', 'contato', 'categoria', 'tipo', 'siteUrl', 'pedidoMinimo', 'fretePadrao', 'observacoes']),
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

export const TITULOS_CADASTROS = { clientes: 'Leads e clientes', produtos: 'Produtos', fornecedores: 'Fornecedores', bancos: 'Contas bancárias', pagamentos: 'Formas de pagamento', modelos: 'Modelos' }
export const NOMES_CANAIS_MODELO = { Email: 'E-mail', Sms: 'SMS', WhatsApp: 'WhatsApp', 1: 'E-mail', 2: 'SMS', 3: 'WhatsApp' }
export const EVENTOS_MODELO = [
  ['PedidoPagoConfirmado', 'Pagamento do pedido confirmado'], ['PedidoEmPreparo', 'Pedido em preparo'], ['PedidoSaiuParaEntrega', 'Pedido saiu para entrega'],
  ['PedidoEntregue', 'Pedido entregue'], ['ReembolsoEfetuado', 'Reembolso efetuado'], ['CampanhaMarketing', 'Campanha'],
  ['ProdutoVencendo', 'Produto próximo do vencimento'], ['AlertaEstoqueCritico', 'Estoque crítico'], ['ContaPagarVencendo', 'Conta a pagar vencendo'],
  ['ContaReceberVencendo', 'Conta a receber vencendo'],
].map(([valor, rotulo]) => ({ valor, rotulo }))
