// Telas do modo demonstração (laboratório local, issue #2017): as que leem o estado do Balcão pelo espelho
// entre janelas e o painel de simulações. Carregado só por `app/demonstracao.js`, que o build de produção
// elimina. No modo API a Cozinha e as Entregas leem a API (`TelaCozinhaApi`, `TelaEntregasApi`).
export { TelaCardapioLink } from '../features/cardapio-link/TelaCardapioLink'
export { TelaCozinha } from '../features/cozinha/TelaCozinha'
export { GavetaEntregas } from '../features/entregas/GavetaEntregas'
export { TelaEntregas } from '../features/entregas/TelaEntregas'
export { AbaCaixa } from '../features/gestao/caixa/AbaCaixa'
export { AbaFidelidade } from '../features/gestao/fidelidade/AbaFidelidade'
export { AbaIntegracoes } from '../features/gestao/integracoes/AbaIntegracoes'
export { AbaJanelas } from '../features/gestao/janelas/AbaJanelas'
export { AbaProducao } from '../features/gestao/producao/AbaProducao'
export { PainelSimulacoes } from '../features/simulacoes/PainelSimulacoes'
export { useRoteiro } from '../features/simulacoes/useRoteiro'
