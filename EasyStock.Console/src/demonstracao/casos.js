import { casosAnexos } from '../aplicacao/casos/anexos'
import { casosAreaEntrega } from '../aplicacao/casos/areaEntrega'
import { casosAtendimentoLocal } from '../aplicacao/casos/atendimentoLocal'
import { casosCaixa } from '../aplicacao/casos/caixa'
import { casosCardapio } from '../aplicacao/casos/cardapio'
import { casosCardapioLink } from '../aplicacao/casos/cardapioLink'
import { casosCliente } from '../aplicacao/casos/cliente'
import { casosCobranca } from '../aplicacao/casos/cobranca'
import { casosEncerramento } from '../aplicacao/casos/encerramento'
import { casosEntregas } from '../aplicacao/casos/entregas'
import { casosEsteira } from '../aplicacao/casos/esteira'
import { casosFidelidade } from '../aplicacao/casos/fidelidade'
import { casosIntegracoes } from '../aplicacao/casos/integracoes'
import { casosJanelas } from '../aplicacao/casos/janelas'
import { casosLote } from '../aplicacao/casos/lote'
import { casosPagamentoPix } from '../aplicacao/casos/pagamentoPix'
import { casosProducao } from '../aplicacao/casos/producao'
import { casosRespostas } from '../aplicacao/casos/respostas'
import { casosSimulacao } from '../aplicacao/casos/simulacao'

// Os casos do reducer que só a demonstração despacha: no modo API o EasyStok faz esse trabalho (cobra, anda a
// esteira, cadastra, entrega) e a tela só mostra o que ele devolve. Os que os dois modos usam ficam em
// `aplicacao/reducer.js` (`CASOS_DO_MODO_API`).
export const casosDaDemonstracao = {
  ...casosAnexos,
  ...casosAreaEntrega,
  ...casosAtendimentoLocal,
  ...casosCaixa,
  ...casosCardapio,
  ...casosCardapioLink,
  ...casosCliente,
  ...casosCobranca,
  ...casosEncerramento,
  ...casosEntregas,
  ...casosEsteira,
  ...casosFidelidade,
  ...casosIntegracoes,
  ...casosJanelas,
  ...casosLote,
  ...casosPagamentoPix,
  ...casosProducao,
  ...casosRespostas,
  ...casosSimulacao,
}
