// Regras do baba-producao: consultas e a unica escrita (perda) pela API de producao do atendimento. Sem E/S de protocolo.
import { qs, data, uuid, campos } from './caixa.mjs';

const BASE = '/api/atendimento/producao';
export const MOTIVOS = ['Vencido', 'PerdaNoPreparo', 'Doacao', 'Degustacao', 'Outro'];

function quantidade(v) {
  if (!Number.isFinite(v) || v <= 0 || v > 100000 || Math.round(v * 1000) / 1000 !== v) throw new Error('quantidade deve ser maior que zero, ate 100000 e ate 3 casas decimais.');
  return v;
}

export const consultas = {
  estoque_do_dia: () => `${BASE}/estoque-do-dia`,
  sugestao: ({ ate }) => `${BASE}/sugestao` + qs({ ate: data(ate, 'ate') }),
  receitas: () => `${BASE}/receitas`,
  receita: ({ produto_id }) => {
    if (!produto_id) throw new Error('produto_id e obrigatorio.');
    return `${BASE}/receitas/${uuid(produto_id, 'produto_id')}`;
  },
  insumos: () => `${BASE}/insumos`,
  perdas: ({ de, ate }) => `${BASE}/perdas` + qs({ de: data(de, 'de'), ate: data(ate, 'ate') }),
  vencidos: () => `${BASE}/perdas/vencidos`,
};

/** Corpo da unica escrita. Empresa e identidade vem do token; o resumo mostra TODOS os campos enviados. */
export const escritas = {
  perda: a => {
    const { produto_id, item_estoque_id, quantidade: q, motivo, texto, produto_variacao_id } = a;
    if (!produto_id) throw new Error('produto_id e obrigatorio.');
    if (!MOTIVOS.includes(motivo)) throw new Error(`motivo deve ser um de: ${MOTIVOS.join(', ')}.`);
    return {
      resumo: `LANCAR PERDA: ${campos({ produto_id: uuid(produto_id, 'produto_id'), quantidade: quantidade(q), motivo, item_estoque_id: uuid(item_estoque_id, 'item_estoque_id'), produto_variacao_id: uuid(produto_variacao_id, 'produto_variacao_id'), texto })}. Baixa o estoque; desfazer so pelo estorno na API.`,
      caminho: `${BASE}/perdas`,
      corpo: { produtoId: produto_id, itemEstoqueId: item_estoque_id, quantidade: q, motivo, texto, produtoVariacaoId: produto_variacao_id },
    };
  },
};
