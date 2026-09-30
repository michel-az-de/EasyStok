// Histórico de pedidos por cliente. Vira consulta ao backend quando existir.
//
// Rodada 8 (US-007): todo cliente com `cliente.pedidos > 0` na massa tem aqui o
// mesmo número de pedidos, coerente com tag, nota e restrição de cada um. Quando
// a conversa tem `pedido` em aberto (massa-conversas.json), o pedido mais recente
// deste array é o espelho dele (mesmo número, itens e estado) — não conta em
// dobro, é o mesmo pedido visto pelas duas telas. `coerencia-da-massa.mjs` cobra
// os dois lados: a contagem batendo e o pedido aberto aparecendo aqui.
//
// Rodada 10 (achados P1.2, P2, "o que falta construir" 2, 4 e 5): três
// acréscimos, todos por gerador em vez de reescrever as 122 linhas à mão.
//   avaliacaoCliente  cada pedido concluído ganha a avaliação por
//                     `avaliacaoSemente.js` (`comAvaliacoes`), nunca no
//                     pedido mais recente (esse é ao vivo).
//   HISTORICO_GERADO  os pedidos das dezenas de cadastros novos de
//                     `massaClientesGerados.js` (achado P2: a massa de 31
//                     não sustenta o exemplo da dona, "50 clientes no filtro
//                     de lasanha").
//   c27 (Paulo)       ganhou 2 pedidos antigos (cancelado e agendado) para
//                     provar a variedade de estado pedida no achado 5, num
//                     cliente já citado nesta rodada.
import { comAvaliacoes } from './avaliacaoSemente'
import { HISTORICO_GERADO } from './massaClientesGerados'
import { comEntregadores } from './entregadoresSemente'

const HISTORICO = {
  c1: [
    { numero: '2026-0184', em: '2026-09-22', total: 113, estado: 'pago', itens: ['1× Lasanha clássica', '2× Parmesão ralado'], nota: 'Gratinada no forno' },
    { numero: '2026-0171', em: '2026-09-14', total: 99, estado: 'entregue', itens: ['1× Lasanha clássica', '1× Parmesão ralado'], nota: 'Massa mais al dente' },
    { numero: '2026-0152', em: '2026-08-31', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
    { numero: '2026-0140', em: '2026-08-17', total: 163, estado: 'entregue', itens: ['1× Lasanha clássica', '1× Pappardelle com ragu'], nota: null },
    { numero: '2026-0095', em: '2026-07-20', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
    { numero: '2026-0075', em: '2026-06-08', total: 99, estado: 'entregue', itens: ['1× Lasanha clássica', '1× Parmesão ralado'], nota: null },
    { numero: '2026-0057', em: '2026-04-18', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: 'Primeiro pedido, ainda sem pedir massa al dente' },
  ],
  c3: [
    { numero: '2026-0181', em: '2026-09-22', total: 124, estado: 'entrega', itens: ['2× Ravióli de abóbora'], nota: 'Molho à parte' },
    { numero: '2026-0168', em: '2026-09-12', total: 124, estado: 'entregue', itens: ['2× Ravióli de abóbora'], nota: 'Molho à parte' },
    { numero: '2026-0149', em: '2026-08-29', total: 62, estado: 'entregue', itens: ['1× Ravióli de abóbora'], nota: null },
    { numero: '2026-0112', em: '2026-08-14', total: 124, estado: 'entregue', itens: ['2× Ravióli de abóbora'], nota: 'Perguntou pela lasanha, não tinha. Levou ravióli de abóbora.' },
  ],
  c4: [
    { numero: '2026-0160', em: '2026-09-05', total: 68, estado: 'entregue', itens: ['1× Tortéi de costela'], nota: 'Sem queijo por causa da lactose' },
    { numero: '2026-0108', em: '2026-08-08', total: 78, estado: 'entregue', itens: ['1× Pappardelle com ragu'], nota: 'Sem queijo por causa da lactose, mesmo ajuste do Tortéi' },
  ],
  c5: [
    { numero: '2026-0177', em: '2026-09-21', total: 62, estado: 'entregue', itens: ['1× Ravióli de limão siciliano'], nota: 'Avaliou positivo, quer o limão siciliano de novo na sexta' },
    { numero: '2026-0159', em: '2026-09-04', total: 62, estado: 'entregue', itens: ['1× Ravióli de limão siciliano'], nota: null },
    { numero: '2026-0144', em: '2026-08-22', total: 62, estado: 'entregue', itens: ['1× Ravióli de limão siciliano'], nota: null },
    { numero: '2026-0106', em: '2026-08-07', total: 62, estado: 'entregue', itens: ['1× Ravióli de limão siciliano'], nota: null },
    { numero: '2026-0091', em: '2026-07-10', total: 62, estado: 'entregue', itens: ['1× Ravióli de limão siciliano'], nota: null },
  ],
  c6: [
    { numero: '2026-0155', em: '2026-09-01', total: 78, estado: 'cancelado', itens: ['1× Pappardelle com ragu'], nota: 'Cliente não confirmou a tempo' },
    { numero: '2026-0110', em: '2026-08-11', total: 78, estado: 'entregue', itens: ['1× Pappardelle com ragu'], nota: null },
    { numero: '2026-0083', em: '2026-06-25', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
  ],
  c10: [
    { numero: '2026-0121', em: '2026-08-25', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
    { numero: '2026-0096', em: '2026-07-20', total: 78, estado: 'entregue', itens: ['1× Pappardelle com ragu'], nota: null },
  ],
  c11: [
    { numero: '2026-0134', em: '2026-09-10', total: 62, estado: 'entregue', itens: ['1× Ravióli de abóbora'], nota: 'Perguntou se era vegano antes de pedir' },
  ],
  c12: [
    { numero: '2026-0191', em: '2026-09-22', total: 106, estado: 'aguardando', itens: ['1× Pappardelle com ragu', '2× Parmesão ralado'], nota: 'Ragu bem quente, potes separados' },
    { numero: '2026-0127', em: '2026-09-02', total: 106, estado: 'entregue', itens: ['1× Pappardelle com ragu', '2× Parmesão ralado'], nota: 'Pede parmesão à parte sempre, já veio incluso' },
    { numero: '2026-0105', em: '2026-08-05', total: 106, estado: 'entregue', itens: ['1× Pappardelle com ragu', '2× Parmesão ralado'], nota: null },
    { numero: '2026-0089', em: '2026-07-08', total: 106, estado: 'entregue', itens: ['1× Pappardelle com ragu', '2× Parmesão ralado'], nota: null },
    { numero: '2026-0076', em: '2026-06-10', total: 106, estado: 'entregue', itens: ['1× Pappardelle com ragu', '2× Parmesão ralado'], nota: null },
    { numero: '2026-0065', em: '2026-05-13', total: 106, estado: 'entregue', itens: ['1× Pappardelle com ragu', '2× Parmesão ralado'], nota: null },
    { numero: '2026-0056', em: '2026-04-15', total: 106, estado: 'entregue', itens: ['1× Pappardelle com ragu', '2× Parmesão ralado'], nota: null },
    { numero: '2026-0050', em: '2026-03-18', total: 106, estado: 'entregue', itens: ['1× Pappardelle com ragu', '2× Parmesão ralado'], nota: null },
    { numero: '2026-0046', em: '2026-02-20', total: 106, estado: 'entregue', itens: ['1× Pappardelle com ragu', '2× Parmesão ralado'], nota: null },
  ],
  c13: [
    { numero: '2026-0192', em: '2026-09-22', total: 85, estado: 'pago', itens: ['1× Lasanha clássica'], nota: null },
    { numero: '2026-0126', em: '2026-08-30', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: 'Almoça 11h45, atenção à janela cedo' },
    { numero: '2026-0094', em: '2026-07-19', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
    { numero: '2026-0074', em: '2026-06-07', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
    { numero: '2026-0061', em: '2026-04-26', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
    { numero: '2026-0049', em: '2026-03-15', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
  ],
  c14: [
    { numero: '2026-0138', em: '2026-09-18', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
    { numero: '2026-0131', em: '2026-09-07', total: 68, estado: 'entregue', itens: ['1× Tortéi de costela'], nota: null },
    { numero: '2026-0114', em: '2026-08-17', total: 62, estado: 'entregue', itens: ['1× Ravióli de abóbora'], nota: null },
    { numero: '2026-0101', em: '2026-07-27', total: 62, estado: 'entregue', itens: ['1× Ravióli de limão siciliano'], nota: null },
    { numero: '2026-0085', em: '2026-06-29', total: 78, estado: 'entregue', itens: ['1× Pappardelle com ragu'], nota: null },
    { numero: '2026-0072', em: '2026-06-01', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
    { numero: '2026-0063', em: '2026-05-04', total: 68, estado: 'entregue', itens: ['1× Tortéi de costela'], nota: null },
    { numero: '2026-0053', em: '2026-04-06', total: 62, estado: 'entregue', itens: ['1× Ravióli de abóbora'], nota: null },
    { numero: '2026-0048', em: '2026-03-09', total: 62, estado: 'entregue', itens: ['1× Ravióli de limão siciliano'], nota: null },
    { numero: '2026-0045', em: '2026-02-09', total: 78, estado: 'entregue', itens: ['1× Pappardelle com ragu'], nota: null },
    { numero: '2026-0042', em: '2026-01-12', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: 'Cliente desde a primeira fornada' },
  ],
  c15: [
    { numero: '2026-0186', em: '2026-09-21', total: 68, estado: 'aguardando', itens: ['1× Tortéi de costela'], nota: 'Gerou o pedido e ainda não pagou' },
  ],
  c16: [
    { numero: '2026-0187', em: '2026-09-22', total: 85, estado: 'preparo', itens: ['1× Lasanha clássica'], nota: 'Pergunta do pedido a cada dez minutos' },
    { numero: '2026-0124', em: '2026-08-29', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
    { numero: '2026-0099', em: '2026-07-25', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
    { numero: '2026-0081', em: '2026-06-20', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
  ],
  c17: [
    { numero: '2026-0182', em: '2026-09-22', total: 78, estado: 'entregue', itens: ['1× Pappardelle com ragu'], nota: 'Reclamou da massa cozida demais, reduzir dois minutos' },
    { numero: '2026-0122', em: '2026-08-28', total: 78, estado: 'entregue', itens: ['1× Pappardelle com ragu'], nota: null },
    { numero: '2026-0098', em: '2026-07-24', total: 78, estado: 'entregue', itens: ['1× Pappardelle com ragu'], nota: null },
    { numero: '2026-0080', em: '2026-06-19', total: 78, estado: 'entregue', itens: ['1× Pappardelle com ragu'], nota: null },
    { numero: '2026-0066', em: '2026-05-15', total: 78, estado: 'entregue', itens: ['1× Pappardelle com ragu'], nota: null },
  ],
  c18: [
    { numero: '2026-0183', em: '2026-09-22', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: 'Chegou virada na sacola, R$ 85 devolvidos' },
    { numero: '2026-0119', em: '2026-08-22', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
    { numero: '2026-0093', em: '2026-07-18', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
  ],
  c19: [
    { numero: '2026-0180', em: '2026-09-22', total: 124, estado: 'entregue', itens: ['2× Ravióli de limão siciliano'], nota: null },
    { numero: '2026-0137', em: '2026-09-12', total: 124, estado: 'entregue', itens: ['2× Ravióli de limão siciliano'], nota: null },
    { numero: '2026-0120', em: '2026-08-22', total: 124, estado: 'entregue', itens: ['2× Ravióli de limão siciliano'], nota: null },
    { numero: '2026-0103', em: '2026-08-01', total: 124, estado: 'entregue', itens: ['2× Ravióli de limão siciliano'], nota: null },
    { numero: '2026-0087', em: '2026-07-04', total: 124, estado: 'entregue', itens: ['2× Ravióli de limão siciliano'], nota: null },
    { numero: '2026-0073', em: '2026-06-06', total: 124, estado: 'entregue', itens: ['2× Ravióli de limão siciliano'], nota: null },
    { numero: '2026-0064', em: '2026-05-09', total: 124, estado: 'entregue', itens: ['2× Ravióli de limão siciliano'], nota: null },
    { numero: '2026-0055', em: '2026-04-11', total: 124, estado: 'entregue', itens: ['2× Ravióli de limão siciliano'], nota: 'Trouxe a primeira indicação' },
  ],
  c20: [
    { numero: '2026-0116', em: '2026-08-18', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: 'Contestou o pix dizendo que não chegou, segunda vez' },
    { numero: '2026-0088', em: '2026-07-04', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
  ],
  c21: [
    { numero: '2026-0109', em: '2026-08-10', total: 78, estado: 'entregue', itens: ['1× Pappardelle com ragu'], nota: null },
    { numero: '2026-0078', em: '2026-06-15', total: 62, estado: 'entregue', itens: ['1× Ravióli de limão siciliano'], nota: null },
    { numero: '2026-0059', em: '2026-04-20', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
  ],
  c22: [
    { numero: '2026-0139', em: '2026-09-18', total: 89, estado: 'entregue', itens: ['1× Lasanha verde'], nota: 'Primeiro da fila quando a lasanha verde entra na produção' },
    { numero: '2026-0136', em: '2026-09-11', total: 89, estado: 'entregue', itens: ['1× Lasanha verde'], nota: null },
    { numero: '2026-0128', em: '2026-09-04', total: 89, estado: 'entregue', itens: ['1× Lasanha verde'], nota: null },
    { numero: '2026-0123', em: '2026-08-28', total: 89, estado: 'entregue', itens: ['1× Lasanha verde'], nota: null },
  ],
  c23: [
    { numero: '2026-0130', em: '2026-09-05', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
    { numero: '2026-0113', em: '2026-08-15', total: 62, estado: 'entregue', itens: ['1× Ravióli de abóbora'], nota: null },
  ],
  c24: [
    { numero: '2026-0188', em: '2026-09-22', total: 62, estado: 'preparo', itens: ['1× Ravióli de limão siciliano'], nota: 'Detesta aviso automático, manter só o agradecimento' },
    { numero: '2026-0133', em: '2026-09-09', total: 62, estado: 'entregue', itens: ['1× Ravióli de abóbora'], nota: null },
    { numero: '2026-0111', em: '2026-08-12', total: 62, estado: 'entregue', itens: ['1× Ravióli de limão siciliano'], nota: null },
    { numero: '2026-0092', em: '2026-07-15', total: 62, estado: 'entregue', itens: ['1× Ravióli de limão siciliano'], nota: null },
    { numero: '2026-0079', em: '2026-06-17', total: 62, estado: 'entregue', itens: ['1× Ravióli de abóbora'], nota: null },
    { numero: '2026-0067', em: '2026-05-20', total: 62, estado: 'entregue', itens: ['1× Ravióli de limão siciliano'], nota: null },
  ],
  c25: [
    { numero: '2026-0194', em: '2026-09-22', total: 170, estado: 'aguardando', itens: ['2× Lasanha clássica'], nota: null },
    { numero: '2026-0129', em: '2026-09-04', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: 'Respondeu à campanha de lasanha rápido' },
    { numero: '2026-0107', em: '2026-08-07', total: 170, estado: 'entregue', itens: ['2× Lasanha clássica'], nota: null },
    { numero: '2026-0086', em: '2026-07-03', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
    { numero: '2026-0071', em: '2026-05-29', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
    { numero: '2026-0060', em: '2026-04-24', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
    { numero: '2026-0052', em: '2026-03-20', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
  ],
  c27: [
    { numero: '2026-0135', em: '2026-09-10', total: 68, estado: 'entregue', itens: ['1× Tortéi de costela'], nota: 'Não gosta que eu decida por ele, sempre perguntar' },
    { numero: '2026-0117', em: '2026-08-20', total: 68, estado: 'entregue', itens: ['1× Tortéi de costela'], nota: null },
    { numero: '2026-0102', em: '2026-07-30', total: 68, estado: 'entregue', itens: ['1× Tortéi de costela'], nota: null },
    { numero: '2026-0500', em: '2026-07-20', total: 68, estado: 'cancelado', itens: ['1× Tortéi de costela'], nota: 'Cancelou em cima da hora, imprevisto de trabalho' },
    { numero: '2026-0090', em: '2026-07-09', total: 68, estado: 'entregue', itens: ['1× Tortéi de costela'], nota: null },
    { numero: '2026-0077', em: '2026-06-11', total: 62, estado: 'entregue', itens: ['1× Ravióli de abóbora'], nota: null },
    { numero: '2026-0068', em: '2026-05-21', total: 62, estado: 'entregue', itens: ['1× Ravióli de limão siciliano'], nota: null },
    { numero: '2026-0501', em: '2026-05-10', total: 68, estado: 'agendado', itens: ['1× Tortéi de costela'], nota: 'Pediu pra deixar agendado pra semana seguinte, não estaria em casa' },
    { numero: '2026-0062', em: '2026-04-30', total: 78, estado: 'entregue', itens: ['1× Pappardelle com ragu'], nota: null },
    { numero: '2026-0054', em: '2026-04-09', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
    { numero: '2026-0051', em: '2026-03-19', total: 62, estado: 'entregue', itens: ['1× Ravióli de abóbora'], nota: null },
    { numero: '2026-0047', em: '2026-02-26', total: 62, estado: 'entregue', itens: ['1× Ravióli de limão siciliano'], nota: null },
    { numero: '2026-0044', em: '2026-02-05', total: 78, estado: 'entregue', itens: ['1× Pappardelle com ragu'], nota: null },
    { numero: '2026-0043', em: '2026-01-15', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
  ],
  c28: [
    { numero: '2026-0132', em: '2026-09-08', total: 62, estado: 'entregue', itens: ['1× Ravióli de abóbora'], nota: 'Combinou trocar o endereço a partir do próximo pedido' },
    { numero: '2026-0115', em: '2026-08-17', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
    { numero: '2026-0097', em: '2026-07-20', total: 78, estado: 'entregue', itens: ['1× Pappardelle com ragu'], nota: null },
    { numero: '2026-0082', em: '2026-06-22', total: 62, estado: 'entregue', itens: ['1× Ravióli de limão siciliano'], nota: null },
    { numero: '2026-0070', em: '2026-05-25', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
  ],
  c29: [
    { numero: '2026-0189', em: '2026-09-22', total: 62, estado: 'pago', itens: ['1× Ravióli de abóbora'], nota: null },
    { numero: '2026-0118', em: '2026-08-20', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: 'Mudou o pedido depois de fechado, aceito porque não tinha entrado no forno' },
  ],
  c30: [
    { numero: '2026-0185', em: '2026-09-22', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: 'Entrega saiu 40 min depois da janela, ocorrência aberta' },
    { numero: '2026-0125', em: '2026-08-29', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
    { numero: '2026-0100', em: '2026-07-25', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
    { numero: '2026-0084', em: '2026-06-27', total: 62, estado: 'entregue', itens: ['1× Ravióli de limão siciliano'], nota: null },
    { numero: '2026-0069', em: '2026-05-23', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
    { numero: '2026-0058', em: '2026-04-18', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
  ],
  c31: [
    { numero: '2026-0199', em: '2026-09-22', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
    { numero: '2026-0104', em: '2026-08-02', total: 85, estado: 'entregue', itens: ['1× Lasanha clássica'], nota: null },
  ],
}

// Pedidos escritos à mão ganham a avaliação por regra (`comAvaliacoes`); os
// pedidos gerados (`massaClientesGerados.js`) já chegam com ela pronta, não
// passam pela regra de novo.
const HISTORICO_ESCRITO = Object.fromEntries(
  Object.entries(HISTORICO).map(([id, lista]) => [id, comAvaliacoes(lista)]),
)
const HISTORICO_TOTAL = { ...HISTORICO_ESCRITO, ...HISTORICO_GERADO }

// Rodada 12 (issue #17): pedido entregue da semente diz quem levou, com placa
// e empresa, para o histórico do cliente separar entregadores de mesmo nome.
export const carregarHistorico = (conversaId) => comEntregadores(HISTORICO_TOTAL[conversaId] ?? [])

