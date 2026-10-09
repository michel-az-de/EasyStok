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
