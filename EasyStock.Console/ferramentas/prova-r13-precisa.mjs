/* eslint-disable no-console */
// Prova da issue #41 (varredura R13, "Precisa de você deixa de fora pergunta
// de cliente sem resposta"). Duas metades do mesmo achado:
//
//   1. Pergunta real do cliente ("Consegue entregar na hora do almoço?",
//      "Cadê meu pedido?") fica de fora de "Precisa de você" enquanto o
//      automático segue "ligado" na conversa (não pausado), porque a regra
//      só contava espera com o automático pausado (RN-04).
//   2. Aviso transacional de esteira (preparo, embalado...) sai sozinho
//      (RN-05) e vira a ÚLTIMA mensagem da lista: isso apagava o "sem
//      resposta" tanto do contador quanto do selo do cartão e do lembrete
//      automático de responder, mesmo a pergunta nunca tendo sido respondida.
//
// Testa as MESMAS funções de domínio que a tela usa (automatico.js,
// lembrete.js, mensagem.js), sem duplicar a conta em JS solto: contador,
// selo e lembrete têm que bater entre si (aceite da issue).
//
//   node ferramentas/prova-r13-precisa.mjs
//
// Mesmo gancho de resolução de `prova-bloqueio-preparo.mjs`.

process.env.TZ = 'America/Sao_Paulo'

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const { mensagensSemRespostaReal, respondeAoCliente } = await import('../src/dominio/mensagem.js')
const { motivoDePrecisar, precisaDeVoce, MINUTOS_DE_ESPERA } = await import('../src/dominio/automatico.js')
const { lembretesAutomaticos, MINUTOS_SEM_RESPOSTA } = await import('../src/dominio/lembrete.js')

const AGORA = Date.parse('2026-09-27T12:00:00-03:00')
const minutosAtras = (m) => new Date(AGORA - m * 60000).toISOString()

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

// --- Fixtures ----------------------------------------------------------------

// José Moretti (massa real, src/infra/conversasSemente.js#c1): pagou e
// perguntou se dá pra entregar na hora do almoço. O pedido anda pra "preparo"
// e o aviso de esteira sai sozinho, sem responder a pergunta dele.
const jose = () => ({
  id: 'c-jose', nome: 'José Moretti', estado: 'Em atendimento', bloqueio: null,
  pedido: { numero: '2026-0184', estado: 'preparo' },
  mensagens: [
    { id: 'j1', dir: 'in', texto: 'Paguei agora. Consegue entregar na hora do almoço?', em: minutosAtras(9) },
    {
      id: 'j2', dir: 'out', texto: 'Seu pedido está em preparo.', em: minutosAtras(6),
      status: 'lida', automatica: true, regra: 'esteira-preparo',
    },
  ],
})

// Laís Ferreira (massa real, #c3): a dona já respondeu de verdade, o cliente
// só agradeceu depois. Não pode virar pendência (cuidado do brief: "obrigado"
// depois de resposta de verdade não é pergunta parada).
const lais = () => ({
  id: 'c-lais', nome: 'Laís Ferreira', estado: 'Em atendimento', bloqueio: null,
  pedido: { numero: '2026-0181', estado: 'entrega' },
  mensagens: [
    { id: 'l1', dir: 'in', texto: 'Vou querer dois raviólis de abóbora pro almoço.', em: minutosAtras(50) },
    { id: 'l2', dir: 'out', texto: 'Anotado, chega ao meio-dia.', em: minutosAtras(48), status: 'lida' },
    { id: 'l3', dir: 'in', texto: 'Perfeito, obrigada!', em: minutosAtras(45) },
  ],
})

// Fábio Meneghel: pergunta, sai aviso de esteira, e DEPOIS a dona responde de
// verdade. A resposta de verdade tem que zerar a pendência do mesmo jeito.
const fabio = () => ({
  id: 'c-fabio', nome: 'Fábio Meneghel', estado: 'Em atendimento', bloqueio: null,
  pedido: { numero: '2026-0199', estado: 'entrega' },
  mensagens: [
    { id: 'f1', dir: 'in', texto: 'Cadê meu pedido?', em: minutosAtras(20) },
    {
      id: 'f2', dir: 'out', texto: 'Seu pedido está embalado.', em: minutosAtras(15),
      status: 'lida', automatica: true, regra: 'esteira-embalado',
    },
    { id: 'f3', dir: 'out', texto: 'Já saiu, chega em 15 min!', em: minutosAtras(1), status: 'lida' },
  ],
})

// Conversa com nota de sistema (cadastro criado) entre a pergunta e agora:
// nota de sistema não fala com o cliente, mesma regra do aviso de esteira.
const comNotaDeSistema = () => ({
  id: 'c-sistema', nome: 'Aline Duarte', estado: 'Em atendimento', bloqueio: null,
  pedido: { numero: '2026-0210', estado: 'aguardando' },
  mensagens: [
    { id: 's1', dir: 'in', texto: 'Paguei pelo pix, confere aí?', em: minutosAtras(12) },
    { id: 's2', dir: 'sistema', texto: 'Cadastro criado.', em: minutosAtras(11) },
  ],
})

// Passou para a dona (US-016/RN-11 style) e o aviso de esteira sai no meio da
// espera: o lembrete "Responder Fulano" não pode sumir por causa disso.
const comPassagemEEsteira = () => ({
  id: 'c-passagem', nome: 'Renata Bicudo', estado: 'Em atendimento', bloqueio: null,
  passagem: { motivo: 'Pergunta fora do roteiro automático, sem resposta pronta.', em: minutosAtras(30), assumida: false },
  pedido: { estado: 'preparo' },
  mensagens: [
    { id: 'p1', dir: 'in', texto: 'Vocês entregam no feriado?', em: minutosAtras(30) },
    {
      id: 'p2', dir: 'out', texto: 'Seu pedido está em preparo.', em: minutosAtras(20),
      status: 'lida', automatica: true, regra: 'esteira-preparo',
    },
  ],
})

// --- 1. dominio/mensagem.js: aviso de esteira e nota de sistema são transparentes
confere('esteira-preparo não é resposta que conversa com o cliente', () => {
  assert.equal(respondeAoCliente({ dir: 'out', automatica: true, regra: 'esteira-preparo' }), false)
})
confere('resposta automática que não é de esteira conta como resposta', () => {
  assert.equal(respondeAoCliente({ dir: 'out', automatica: true, regra: 'faq-horario' }), true)
})
confere('José: aviso de esteira no meio não esconde a pergunta pendente', () => {
  const pendentes = mensagensSemRespostaReal(jose())
  assert.equal(pendentes.length, 1)
  assert.equal(pendentes[0].id, 'j1')
})
confere('Fábio: resposta de verdade depois do aviso de esteira zera a pendência', () => {
  assert.deepEqual(mensagensSemRespostaReal(fabio()), [])
})
confere('nota de sistema também não esconde a pergunta pendente', () => {
  const pendentes = mensagensSemRespostaReal(comNotaDeSistema())
  assert.equal(pendentes.length, 1)
  assert.equal(pendentes[0].id, 's1')
})

// --- 2. Contador "Precisa de você" (motivoDePrecisar / precisaDeVoce) --------
confere('José precisa de você mesmo com o automático ligado (não pausado)', () => {
  const motivo = motivoDePrecisar(jose(), AGORA, false)
  assert.equal(motivo?.chave, 'esperando')
  assert.equal(precisaDeVoce(jose(), AGORA, false), true)
})
confere('Aline (nota de sistema no meio) também precisa de você, não pausada', () => {
  assert.equal(precisaDeVoce(comNotaDeSistema(), AGORA, false), true)
})
confere('Laís não vira pendência: "obrigada" depois de resposta de verdade não é pergunta', () => {
  assert.equal(motivoDePrecisar(lais(), AGORA, false), null)
})
confere('Laís pausada (RN-04, ela assumiu): silêncio já é dela, conta mesmo sem "?"', () => {
  assert.equal(motivoDePrecisar(lais(), AGORA, true)?.chave, 'esperando')
})
confere('Fábio não precisa de você: a resposta de verdade depois da esteira já resolveu', () => {
  assert.equal(motivoDePrecisar(fabio(), AGORA, false), null)
})
confere('dentro dos 5 min de graça (MINUTOS_DE_ESPERA) o automático ainda tem chance', () => {
  const recemChegado = jose()
  recemChegado.mensagens[0].em = minutosAtras(2)
  assert.equal(motivoDePrecisar(recemChegado, AGORA, false), null)
  assert.equal(MINUTOS_DE_ESPERA, 5)
})

// --- 3. Selo do cartão bate com o contador (mesma fonte, dominio/mensagem.js)
confere('selo (contagem) e contador concordam: José tem 1 pendente e precisa de você', () => {
  assert.equal(mensagensSemRespostaReal(jose()).length, 1)
  assert.equal(precisaDeVoce(jose(), AGORA, false), true)
})
confere('selo e contador concordam: Fábio tem 0 pendentes e não precisa de você', () => {
  assert.equal(mensagensSemRespostaReal(fabio()).length, 0)
  assert.equal(precisaDeVoce(fabio(), AGORA, false), false)
})

// --- 4. Lembrete automático (dominio/lembrete.js) segue o mesmo cálculo -------
confere('lembrete "Responder Renata" sobrevive ao aviso de esteira no meio da espera', () => {
  const lembretes = lembretesAutomaticos([comPassagemEEsteira()], AGORA)
  const lembrete = lembretes.find((l) => l.origem === 'passagem')
  assert.ok(lembrete, 'o lembrete automático de passagem deveria existir')
  assert.equal(lembrete.quando, new Date(minutosAtras(30)).getTime() + MINUTOS_SEM_RESPOSTA * 60000)
})

console.log(`\n${passou} verificações passaram.`)
