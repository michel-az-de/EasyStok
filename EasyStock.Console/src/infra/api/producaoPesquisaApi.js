import { chamarApi } from './cliente'

const BASE = '/api/atendimento/producao'
export const pesquisarInsumosWeb = (texto, sinal) => chamarApi(`${BASE}/pesquisa?texto=${encodeURIComponent(texto)}`, { sinal })
export const pesquisarReceitaIa = (nome, sinal) => chamarApi(`${BASE}/sugerir-receita`, { metodo: 'POST', corpo: { nome }, sinal })
export const calcularReceita = (produtoId, quantidade, unidade) => chamarApi(`${BASE}/calcular`, { metodo: 'POST', corpo: { produtoId, quantidade, unidade }, sinal: AbortSignal.timeout(20000) })
