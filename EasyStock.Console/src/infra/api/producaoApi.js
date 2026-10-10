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
    produtoId: p.produtoId ?? null,
    nome: p.nome,
    porcao: p.porcao ?? '',
    saldo: p.saldo,
    descoberto: p.descoberto,
    lotes: (p.lotes ?? []).map(loteDaApi),
    // M1.4c (#1537): saldo de cada porção; variacaoId nulo é o que ficou sem porção.
    porcoes: (p.porcoes ?? []).map((s) => ({ variacaoId: s.variacaoId ?? null, rotulo: s.rotulo, saldo: s.saldo })),
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
      // M1.4c (#1537): a porção produzida, quando o prato tem porções.
      ...(p.variacaoId ? { variacaoId: p.variacaoId } : {}),
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
  // M2.7 (#1523, D-M2-03): embalagem desce em toda produção pela receita.
  embalagem: i.embalagem === true,
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

// D-M2-01 (#1499): produzir o prato baixa os insumos da receita (Gerente liga e desliga).
export const marcarBaixaAutomatica = (produtoId, ligada) =>
  chamarApi(`${PRODUCAO}/receitas/${produtoId}/baixa-automatica`, { metodo: 'PUT', corpo: { ligada } })

export const receitaDaApi = (r) => ({
  sku: r.cardapioItemId,
  produtoId: r.produtoId,
  nome: r.nome,
  rendimento: r.rendimentoBase,
  unidadeRendimento: r.rendimentoUnidade,
  linhas: r.linhas,
  custoTotal: r.custoTotal ?? null,
  custoPorRendimento: r.custoPorRendimento ?? null,
  baixaAutomatica: r.baixaAutomatica === true,
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

// M2.5 (#1502): sugestão de produção (mínimo + agendados + descoberto − saldo) e planejamento dos
// insumos pela mesma cesta da calculadora mobile. Ler exige a permissão de estoque.
export const obterSugestao = (ate) => chamarApi(`${PRODUCAO}/sugestao${ate ? `?ate=${ate}` : ''}`)
export const planejarProducao = (pratos) => chamarApi(`${PRODUCAO}/planejamento`, { metodo: 'POST', corpo: { pratos } })

export const sugestaoDaApi = (s) => ({
  sku: s.cardapioItemId,
  produtoId: s.produtoId,
  nome: s.nome,
  minimo: s.minimo,
  saldo: s.saldo,
  agendados: s.agendados,
  descoberto: s.descoberto,
  sugestao: s.sugestao,
})

export const planejamentoDaApi = (r) => ({
  insumos: (r.consolidado ?? []).map((c) => ({
    insumoId: c.insumoId,
    nome: c.insumoNome,
    precisa: c.precisa,
    unidade: c.unidadeReceita,
    saldo: c.saldo,
    unidadeSaldo: c.unidadeSaldo,
    falta: c.falta ?? null,
    custo: c.custoEstimado ?? null,
    aviso: c.aviso ?? null,
  })),
  pendentes: (r.itens ?? []).filter((i) => i.status !== 'Ok').map((i) => ({ produtoId: i.produtoFinalId, status: i.status, erro: i.erro ?? null })),
  custoTotal: r.custoEstimadoTotal ?? null,
})

// Lista de compras (já existe na API, ListasComprasController). O console grava com origem "console".
export const gerarListaDeCompras = ({ nome, itens }) => chamarApi('/api/listas-compras/gerar', {
  metodo: 'POST',
  corpo: { nome, itens, origem: 'console' },
})

// M2.6 (#1511): perdas com motivo (D-M2-02). Lançar acima de R$ 50 é do Gerente (D-M2-06);
// desfazer é o estorno de saída que já existe (Gerente).
const query = (pares) => {
  const q = Object.entries(pares).filter(([, v]) => v).map(([k, v]) => `${k}=${encodeURIComponent(v)}`).join('&')
  return q ? `?${q}` : ''
}
export const obterPerdas = (de, ate) => chamarApi(`${PRODUCAO}/perdas${query({ de, ate })}`)
export const obterVencidos = () => chamarApi(`${PRODUCAO}/perdas/vencidos`)
export const lancarPerda = ({ produtoId, itemEstoqueId = null, quantidade, motivo, texto = null }) =>
  chamarApi(`${PRODUCAO}/perdas`, { metodo: 'POST', corpo: { produtoId, itemEstoqueId, quantidade, motivo, texto } })
export const estornarSaida = (movimentacaoId, motivo) =>
  chamarApi(`/api/estoque/estorno/${movimentacaoId}`, { metodo: 'POST', corpo: { motivo } })

export const resumoDePerdasDaApi = (r) => ({
  de: r?.de ?? null,
  ate: r?.ate ?? null,
  valor: r?.valor ?? 0,
  porMotivo: (r?.porMotivo ?? []).map((m) => ({ motivo: m.motivo, rotulo: m.rotulo, quantidade: m.quantidade, valor: m.valor })),
  porProduto: (r?.porProduto ?? []).map((p) => ({ produtoId: p.produtoId, nome: p.produto, quantidade: p.quantidade, valor: p.valor })),
  lancamentos: (r?.lancamentos ?? []).map((l) => ({
    id: l.movimentacaoId, data: l.data, produtoId: l.produtoId, nome: l.produto, lote: l.lote ?? null,
    quantidade: l.quantidade, valor: l.valor, motivo: l.motivo, descricao: l.descricao ?? null, desfeita: l.desfeita === true,
  })),
})

export const vencidoDaApi = (v) => ({
  itemEstoqueId: v.itemEstoqueId, produtoId: v.produtoId, nome: v.produto, lote: v.lote ?? null,
  quantidade: v.quantidade, validade: v.validadeEm ? v.validadeEm.slice(0, 10) : null, diasVencido: v.diasVencido, valor: v.valor,
})
