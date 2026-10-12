import { mesmoConteudo } from './igualdade'

// Situação de uma consulta que se repete sozinha (#1565): lembretes a cada 15 s, notificações a cada
// 30 s. Quando a recarga não traz novidade a situação fica a MESMA, senão cada tick re-renderiza quem lê.
const semTrocar = (situacao, proxima) => (mesmoConteudo(situacao, proxima) ? situacao : proxima)

// A recarga do relógio é silenciosa: "carregando" é para a primeira carga e para o pedido de quem está na tela.
export const aoComecarConsulta = (situacao, { silenciosa = false } = {}) =>
  (silenciosa ? situacao : semTrocar(situacao, { ...situacao, carregando: true }))

export const aoConcluirConsulta = (situacao, dados = {}) =>
  semTrocar(situacao, { ...situacao, ...dados, carregando: false, erro: null })

export const aoFalharConsulta = (situacao, mensagem) =>
  semTrocar(situacao, { ...situacao, carregando: false, erro: mensagem })
