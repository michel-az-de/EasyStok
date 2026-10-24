// Pacote de dados e lógica do modo demonstração (laboratório local, issue #2017). Carregado só por
// `infra/demonstracao.js`, que o build de produção (`VITE_FONTE_DADOS=api`) elimina junto com tudo que só este
// arquivo importa. As telas da demonstração ficam em `telas.js`, que tem JSX e só o App carrega.
//
// Regra: nenhum módulo daqui para baixo pode importar `infra/demonstracao.js` (nem módulo que o importe), senão
// o `await` da porta espera por si mesmo. Ao somar algo à demonstração, ponha aqui e leia pela porta.
export { casosDaDemonstracao as casos } from './casos'
export { criarAcoesDaDemonstracao as criarAcoes } from '../aplicacao/acoes/demonstracao'
export { carregarConversas } from './conversas'
export { carregarHistorico } from '../infra/historicoPedidos'
export { conectarPrincipal } from '../infra/canalEntreJanelas'
export { useDigitacaoAutomatica } from '../aplicacao/useDigitacaoAutomatica'
export { useReacaoClienteSimulado } from '../aplicacao/useReacaoClienteSimulado'
