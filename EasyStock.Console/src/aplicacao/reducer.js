import { demonstracao } from '../infra/demonstracao'
import { casosApi } from './casos/api'
import { casosAtendimento } from './casos/atendimento'
import { casosComanda } from './casos/comanda'
import { casosConexao } from './casos/conexao'
import { casosFuncionamento } from './casos/funcionamento'
import { casosGestoDaLoja } from './casos/gestoDaLoja'
import { casosLembretes } from './casos/lembretes'
import { casosMeioDePagamento } from './casos/meioDePagamento'
import { casosRascunhoDaComanda } from './casos/rascunhoDaComanda'
import { casosTags } from './casos/tags'

// Fachada do reducer: quem importa daqui (`ATENDENTE`, `estadoInicial`, `reducer`) não muda.
// O estado inicial, a mescla do servidor e cada grupo de casos moram em módulos próprios.
export { ATENDENTE, estadoInicial } from './reducer/estadoInicial'

// Os casos que o modo API despacha: o que o EasyStok devolve (`casos/api.js`) e as ações locais que a
// tela de verdade também usa (rascunho da comanda, filtro, rascunho de texto, lembretes, tags, loja).
export const CASOS_DO_MODO_API = {
  ...casosApi,
  ...casosAtendimento,
  ...casosRascunhoDaComanda,
  ...casosLembretes,
  ...casosTags,
  ...casosMeioDePagamento,
  ...casosGestoDaLoja,
  ...casosConexao,
  ...casosFuncionamento,
  ...casosComanda,
}

// A demonstração soma os dela (esteira, Pix, entregas, simulações...), que só existem fora do build de
// produção: `demonstracao` é `null` com `VITE_FONTE_DADOS=api` (ver `infra/demonstracao.js`).
export const CASOS_COMPOSTOS = demonstracao ? { ...CASOS_DO_MODO_API, ...demonstracao.casos } : CASOS_DO_MODO_API

export function reducer(estado, despacho) {
  const caso = CASOS_COMPOSTOS[despacho.tipo]
  return caso ? caso(estado, despacho) : estado
}
