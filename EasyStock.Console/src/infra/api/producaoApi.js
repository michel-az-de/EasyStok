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
