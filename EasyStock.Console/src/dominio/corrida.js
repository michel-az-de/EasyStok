// Domínio da Frente Entregas e integrações (rodada 13, issue #46, registro
// 108). Puro, sem React: o vocabulário de status de uma CORRIDA chamada por
// um provedor integrado (Lalamove, 99 Entregas), diferente do chamado manual
// da R12 (`dominio/despacho.js`, `viagem.chamado.status`
// procurando/achado/chegou, que continua existindo do jeito que está para
// "Entregador próprio").
//
// A corrida mora no MESMO campo `pedido.viagem.chamado` (nunca uma coleção
// nova no estado, mesma decisão de `dominio/viagem.js`), só que com
// `chamado.provedor` apontando para Lalamove/99 em vez de `undefined`
// (próprio/manual). `casos/integracoes.js` é quem decide qual vocabulário
// usar, com base em `ehProvedorIntegrado`.
import { numeroParaEntregador } from './despacho'

export const ehProvedorIntegrado = (provedor) => provedor === 'lalamove' || provedor === '99entrega'

// Áudios 06 e da homologação (Q3, UC-10): "saber quando entregou" pelo status
// do provedor, não por adivinhação (RN-32, D8). Quatro passos da corrida mais
// a falha, que devolve o despacho para escolher outro provedor.
export const PASSOS_CORRIDA = {
  procurando: { rotulo: 'Procurando entregador', icone: 'moto', tom: 'atencao' },
  'a-caminho-coleta': { rotulo: 'A caminho da coleta', icone: 'moto', tom: 'atencao' },
  coletado: { rotulo: 'Coletado, saiu para entrega', icone: 'package', tom: 'ok' },
  entregue: { rotulo: 'Entregue', icone: 'house', tom: 'ok' },
  falha: { rotulo: 'Não foi possível chamar', icone: 'circle-x', tom: 'parado' },
  cancelado: { rotulo: 'Corrida cancelada', icone: 'circle-x', tom: 'parado' },
}

export const rotuloDoStatusCorrida = (status) => PASSOS_CORRIDA[status]?.rotulo ?? status

// Sequência feliz, sem falha: usada pela tela para saber qual é o PRÓXIMO
// passo simulado quando o tique da vez não sorteou falha (ver
// `infra/provedoresDeEntrega.js: consultarStatus`, que decide COM falha).
const SEQUENCIA = ['procurando', 'a-caminho-coleta', 'coletado', 'entregue']

export const proximoStatusCorrida = (status) => SEQUENCIA[SEQUENCIA.indexOf(status) + 1] ?? null

export const corridaEhFinal = (status) => status === 'entregue' || status === 'falha' || status === 'cancelado'

// "Coletado" é o marco que move a esteira sozinho, sem ela clicar (RN-32,
// D8): pedido embalado -> em entrega, com o aviso automático de sempre. É a
// mesma transição que "Sair agora" faz no modo próprio, só que disparada pelo
// status do provedor em vez do toque dela.
export const statusMoveParaEntrega = (status) => status === 'coletado'

// "Entregue" fecha sozinho, com agradecimento (UC-10, E1: sem confirmação da
// dona, marca na mão só quando ela precisar corrigir depois).
export const statusMoveParaEntregue = (status) => status === 'entregue'

// Texto curto da corrida para o cartão/painel: código do provedor (o mesmo
// formato que o entregador confere, `numeroParaEntregador`) e o rótulo do
// provedor, para "chamei a Lalamove do pedido #4821" ficar visível de relance.
export function resumoDaCorrida(chamado, pedido, rotuloProvedor) {
  if (!chamado?.codigo) return `${rotuloProvedor} · pedido ${numeroParaEntregador(pedido)}`
  return `${rotuloProvedor} ${chamado.codigo} · pedido ${numeroParaEntregador(pedido)}`
}
