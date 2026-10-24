import { demonstracao } from './demonstracao'

// Porta de entrada das conversas. A aplicação depende desta função, nunca do
// array. Trocar por uma chamada HTTP mais tarde não toca em componente nenhum.
//
// Modo API (F01): as conversas chegam pelo polling, nunca da massa; a massa de 30 conversas
// (dados/massa-conversas.json) e a semente curta (`?massa=0`) são da demonstração e moram em
// `src/demonstracao/conversas.js`.
export const carregarConversas = () => (demonstracao ? demonstracao.carregarConversas() : [])

// A numeração local fica em `numeracao.js`; quem já importava daqui continua importando.
export { proximoId, proximoNumeroPedido } from './numeracao'
