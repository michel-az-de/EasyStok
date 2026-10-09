import { chamarApi } from './cliente'

// Produção pelo console (M2, #1490). Estoque do dia em porções, por prato do cardápio.
const PRODUCAO = '/api/atendimento/producao'

export const obterEstoqueDoDia = () => chamarApi(`${PRODUCAO}/estoque-do-dia`)

const loteDaApi = (l) => ({
  codigo: l.codigo ?? null,
  quantidade: l.quantidade,
  validade: l.validadeEm ? l.validadeEm.slice(0, 10) : null,
  diasParaVencer: l.diasParaVencer ?? null,
  vencendo: l.vencendo === true,
  vencido: l.vencido === true,
})

export const estoqueDoDiaDaApi = (r) => ({
  pratos: (r?.pratos ?? []).map((p) => ({
    sku: p.cardapioItemId,
    nome: p.nome,
    porcao: p.porcao ?? '',
    saldo: p.saldo,
    descoberto: p.descoberto,
    lotes: (p.lotes ?? []).map(loteDaApi),
  })),
  alertas: (r?.alertas ?? []).map((a) => ({
    produtoId: a.produtoId,
    sku: a.cardapioItemId ?? null,
    nome: a.nome,
    texto: a.texto,
    descoberto: a.quantidadeDescoberta,
  })),
})

// M2.2 (#1491): produção do dia por prato. A chave evita lote em dobro se ela clicar de novo.
export const registrarProducao = (pratos, chave) => chamarApi(PRODUCAO, {
  metodo: 'POST',
  chave,
  corpo: {
    pratos: pratos.map((p) => ({
      cardapioItemId: p.sku,
      porcoes: p.porcoes,
      pesoPorPorcaoG: p.pesoPorPorcaoG ?? null,
      pesoRealG: p.pesoRealG ?? null,
      validadeDias: p.validadeDias,
    })),
  },
})

// Etiquetas do lote (payload de impressão que o EasyStok já monta, LotesController).
export const obterEtiquetasDoLote = (loteId) => chamarApi(`/api/lotes/${loteId}/etiquetas/render`)

export const etiquetasDaApi = (r) => (r?.etiquetas ?? []).map((e) => ({
  id: e.id,
  sequencial: e.sequencial,
  codigo: e.codigo,
  nome: e.produto?.nome ?? '',
  pesoG: e.produto?.pesoG ?? null,
  alergenos: e.produto?.fichaAlergenos ?? [],
  lote: e.loteCodigo ?? null,
  produzidoEm: e.loteCriadoEm ?? null,
  validadeEm: e.loteValidadeEm ?? null,
}))

// M2.3 (#1496): insumos da produção (intermediário e embalagem). Cadastro e ajuste são do Gerente.
export const listarInsumos = () => chamarApi(`${PRODUCAO}/insumos`)
export const criarInsumo = (dados) => chamarApi(`${PRODUCAO}/insumos`, { metodo: 'POST', corpo: dados })
export const atualizarInsumo = (id, dados) => chamarApi(`${PRODUCAO}/insumos/${id}`, { metodo: 'PUT', corpo: dados })

export const insumoDaApi = (i) => ({
  id: i.produtoId,
  nome: i.nome,
  unidade: i.unidade,
  saldo: i.saldo,
  minimo: i.minimo ?? null,
  custo: i.custo ?? null,
  receitas: i.receitas,
  comprar: i.abaixoDoMinimo === true,
})

// M2.4a (#1498): receitas dos pratos. Ler é do Operador com estoque; gravar é o PUT da composição
// que já existe (Gerente desde a #1462), que guarda o diff na auditoria.
export const listarReceitas = () => chamarApi(`${PRODUCAO}/receitas`)
export const obterReceita = (produtoId) => chamarApi(`${PRODUCAO}/receitas/${produtoId}`)
export const salvarReceita = (produtoId, { rendimentoBase, rendimentoUnidade, unidadeMedidaBase, linhas }) =>
  chamarApi(`/api/produtos/${produtoId}/composicao`, {
    metodo: 'PUT',
    corpo: {
      rendimentoBase,
      rendimentoUnidade,
      unidadeMedidaBaseProdutoFinal: unidadeMedidaBase,
      linhas: linhas.map((l, i) => ({ insumoId: l.insumoId, quantidade: l.quantidade, unidade: l.unidade, observacao: null, ordemExibicao: i })),
      observacao: 'Receita editada pelo console',
    },
  })

export const receitaDaApi = (r) => ({
  sku: r.cardapioItemId,
  produtoId: r.produtoId,
  nome: r.nome,
  rendimento: r.rendimentoBase,
  unidadeRendimento: r.rendimentoUnidade,
  linhas: r.linhas,
  custoTotal: r.custoTotal ?? null,
  custoPorRendimento: r.custoPorRendimento ?? null,
})

export const detalheDaReceitaDaApi = (d) => ({
  produtoId: d.produtoId,
  nome: d.nome,
  rendimento: d.rendimentoBase,
  unidadeRendimento: d.rendimentoUnidade,
  unidadeMedidaBase: d.unidadeMedidaBase,
  linhas: (d.linhas ?? []).map((l) => ({ insumoId: l.insumoId, insumo: l.insumo, quantidade: l.quantidade, unidade: l.unidade, custo: l.custo ?? null })),
  custoTotal: d.custoTotal ?? null,
  custoPorRendimento: d.custoPorRendimento ?? null,
})
