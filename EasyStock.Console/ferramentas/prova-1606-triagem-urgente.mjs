/* eslint-disable no-console */
// Prova da issue #1606: estorno, restrição alimentar, insistência sobre o
// pedido e "paguei" com cobrança aberta sobem para "Precisa de você" na hora,
// com o motivo escrito, mesmo com o automático ligado e sem "?" no fim.
// Importa as MESMAS funções que o Balcão usa (dominio/automatico.js):
//
//   node ferramentas/prova-1606-triagem-urgente.mjs
//
// Mesmo gancho de resolução de `prova-1427-sla-resposta.mjs`.

process.env.TZ = 'America/Sao_Paulo'

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    try {
      return proximo(especificador, contexto)
    } catch (erro) {
      if (especificador.startsWith('.') && !/\.m?jsx?$/.test(especificador)) {
        return proximo(especificador + '.js', contexto)
      }
      throw erro
    }
  },
})

const { motivoDePrecisar, precisaDeVoce } = await import('../src/dominio/automatico.js')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

const AGORA = new Date('2026-10-11T11:05:00-03:00').getTime()
const ha = (minutos) => new Date(AGORA - minutos * 60000).toISOString()
const cliente = (texto, minutos) => ({ dir: 'in', texto, em: ha(minutos) })
const casa = (texto, minutos, extra = {}) => ({ dir: 'out', texto, em: ha(minutos), status: 'entregue', ...extra })
const conversa = (mensagens, extra = {}) => ({
  id: 'c1', nome: 'Cliente', estado: 'Aberto', cliente: { tags: [] }, mensagens, ...extra,
})
const cobrancaAberta = { valor: 106, criadaEm: AGORA - 10 * 60000, expiraEm: AGORA + 20 * 60000 }
const cobrancaPaga = { ...cobrancaAberta, pagaEm: AGORA - 5 * 60000, valorPago: 106 }
// Automático LIGADO (pausado = false) é o caso que escapava: nada aqui passou do SLA de 5 min.
const motivo = (c) => motivoDePrecisar(c, AGORA, false)

confere('estorno pedido sem "?" sobe na hora com motivo próprio', () => {
  const m = motivo(conversa(
    [cliente('Obrigado pela atenção. Pode devolver o valor. A gente pede de novo depois, sem drama', 1)],
    { pedido: { numero: '2026-0183', estado: 'entregue', cobranca: cobrancaPaga } },
  ))
  assert.equal(m?.chave, 'devolucao')
  assert.equal(m.tom, 'perigo')
})

confere('restrição alimentar sem "?" sobe na hora', () => {
  const m = motivo(conversa([
    casa('Oi Marcelo! Em que posso ajudar?', 9, { automatica: true }),
    cliente('É pra minha esposa, ela não pode lactose de jeito nenhum.', 1),
  ]))
  assert.equal(m?.chave, 'restricao')
})

confere('"sem querer ser chato" não vira restrição: pedido de desligar avisos sobe como Precisa de você', () => {
  const m = motivo(conversa([
    cliente('Thatiane, sem querer ser chato, dá pra desligar essas mensagens automáticas?', 2),
    cliente('Eu compro de você justamente por não ser igual a aplicativo', 1),
  ], { pedido: { numero: '2026-0188', estado: 'em-preparo', cobranca: cobrancaPaga } }))
  assert.equal(m?.chave, 'pede-a-dona')
})

confere('cliente que insiste sobre pedido em andamento sobe na hora', () => {
  const m = motivo(conversa([
    casa('Seu pedido entrou em preparo.', 90, { automatica: true, regra: 'esteira-preparo' }),
    cliente('E aí, saiu?', 4),
    cliente('Cadê meu pedido? Já tá quase na hora do almoço', 1),
  ], { pedido: { numero: '2026-0187', estado: 'em-preparo', cobranca: cobrancaPaga } }))
  assert.equal(m?.chave, 'insistencia')
  assert.match(m.texto, /2026-0187/)
})

confere('uma pergunta só sobre o pedido ainda é do automático (espera o SLA)', () => {
  const c = conversa([cliente('Quanto tempo falta?', 1)],
    { pedido: { numero: '2026-0181', estado: 'em-entrega', cobranca: cobrancaPaga } })
  assert.equal(precisaDeVoce(c, AGORA, false), false)
})

confere('"paguei" com cobrança ainda aberta sobe na hora', () => {
  const m = motivo(conversa([cliente('Paguei pelo pix, confere aí?', 1)],
    { pedido: { numero: '2026-0191', estado: 'aguardando-pagamento', cobranca: cobrancaAberta } }))
  assert.equal(m?.chave, 'diz-que-pagou')
})

confere('"paguei" com pagamento já reconhecido não sobe', () => {
  const c = conversa([cliente('Paguei agora.', 1)],
    { pedido: { numero: '2026-0184', estado: 'pago', cobranca: cobrancaPaga } })
  assert.equal(precisaDeVoce(c, AGORA, false), false)
})

confere('mensagem já respondida de verdade não sobe', () => {
  const c = conversa([
    cliente('Pode devolver o valor.', 10),
    casa('Claro, já devolvo agora.', 8),
  ])
  assert.equal(precisaDeVoce(c, AGORA, false), false)
})

confere('conversa encerrada não sobe', () => {
  const c = conversa([cliente('Pode devolver o valor.', 1)], { estado: 'Encerrado' })
  assert.equal(precisaDeVoce(c, AGORA, false), false)
})

confere('passagem gravada continua mandando no motivo', () => {
  const m = motivo(conversa([cliente('Pode devolver o valor.', 1)],
    { passagem: { assumida: false, motivo: 'Motivo do agente' } }))
  assert.equal(m?.chave, 'passagem')
})

confere('conversa comum segue fora de "Precisa de você"', () => {
  const c = conversa([cliente('Show. Se der, quero um ravióli de abóbora hoje', 1)])
  assert.equal(precisaDeVoce(c, AGORA, false), false)
})

console.log(`\n${passou} verificações passaram`)
