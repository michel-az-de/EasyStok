/* eslint-disable no-console */
// Prova da issue #3339 (estresse do atendimento de 25/11, console). Regras de fila no domínio puro,
// que valem na demonstração e em produção (mesmo `dominio/`, o ramo API só traduz o dado).
//
//   5a. R1C-03: balão automático de cobrança (regra `cobranca-*` na demonstração, `origemAutomatica`
//       `sistema` na API) não conta como resposta ao cliente: "Mandei o pix" segue em "Diz que pagou".
//   5b. R3R-01: Pix pago a menos ("Valor diferente") sobe na fila com o motivo próprio.
//   5c. R2F-03: o motivo genérico "Pergunta fora do roteiro" não vence "Diz que pagou" nem devolução.
//   6.  R1D-04: o prazo da reclamação e da devolução conta com o automático ligado, antes de Assumir.
//   7.  R2F-02: "não consigo abrir" sem "?" é pergunta sem resposta (palavra de impedimento).
//
//   node ferramentas/prova-3339-fila.mjs

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

const { mensagensSemRespostaReal, MOTIVO_PERGUNTA_FORA_DO_ROTEIRO } = await import('../src/dominio/mensagem.js')
const { motivoDePrecisar, respostaAtrasada, MINUTOS_DE_ESPERA } = await import('../src/dominio/automatico.js')

const AGORA = Date.parse('2026-11-25T12:00:00-03:00')
const minutosAtras = (m) => new Date(AGORA - m * 60000).toISOString()

let passou = 0
const falhas = []
const confere = (descricao, fn) => {
  try { fn(); passou += 1; console.log('ok  ' + descricao) } catch (erro) {
    falhas.push(descricao)
    console.log('FALHA  ' + descricao + '\n      ' + String(erro.message).split('\n').slice(0, 3).join(' | '))
  }
}

// --- Fixtures -----------------------------------------------------------------

const cobrancaAguardando = () => ({ valor: 68, expiraEm: AGORA + 30 * 60000, pagaEm: null, valorPago: null })
const cobrancaDePixAPagarMenos = (extra = {}) => ({
  valor: 68, expiraEm: null, pagaEm: AGORA - 5 * 60000, valorPago: 50, ...extra,
})

// Cliente que mandou o Pix e pergunta algo; o link automático de cobrança saiu depois.
const conversaComLinkAutomatico = (origem) => ({
  id: 'c-eduarda', nome: 'Eduarda', estado: 'Em atendimento', bloqueio: null,
  pedido: { numero: '2026-0196', estado: 'aguardando pagamento', cobranca: cobrancaAguardando() },
  mensagens: [
    { id: 'e1', dir: 'in', texto: 'Mandei o pix. O tortéi vem pronto ou eu preparo em casa?', em: minutosAtras(10) },
    { id: 'e2', dir: 'out', texto: 'Pix de R$ 68,00 gerado: segue o link.', em: minutosAtras(2), status: 'lida', automatica: true, ...origem },
  ],
})

const conversaComPassagem = (motivo, textoCliente) => ({
  id: 'c-helena', nome: 'Helena', estado: 'Em atendimento', bloqueio: null,
  pedido: { numero: '2026-0201', estado: 'aguardando pagamento', cobranca: cobrancaAguardando() },
  passagem: { motivo, em: minutosAtras(20), assumida: false },
  mensagens: [{ id: 'h1', dir: 'in', texto: textoCliente, em: minutosAtras(15) }],
})

const conversaSemPedido = (texto, minutos) => ({
  id: 'c-x', nome: 'Cliente', estado: 'Em atendimento', bloqueio: null,
  mensagens: [{ id: 'x1', dir: 'in', texto, em: minutosAtras(minutos) }],
})

const ATRASO = MINUTOS_DE_ESPERA + 60

// --- 5a. Link automático de cobrança não é resposta --------------------------

confere('5a demo: link automático com regra cobranca-pix não conta como resposta (pendência segue)', () => {
  const c = conversaComLinkAutomatico({ regra: 'cobranca-pix' })
  assert.equal(mensagensSemRespostaReal(c).length, 1)
  assert.equal(motivoDePrecisar(c, AGORA, false)?.chave, 'diz-que-pagou')
})

confere('5a API: aviso de cobrança com origemAutomatica sistema não conta como resposta', () => {
  const c = conversaComLinkAutomatico({ origemAutomatica: 'sistema' })
  assert.equal(mensagensSemRespostaReal(c).length, 1)
  assert.equal(motivoDePrecisar(c, AGORA, false)?.chave, 'diz-que-pagou')
})

confere('5a guarda: resposta automática do Assistente (origem agente) ainda conta como resposta', () => {
  const c = conversaComLinkAutomatico({ origemAutomatica: 'agente', regra: 'captura' })
  assert.equal(mensagensSemRespostaReal(c).length, 0)
})

// --- 5b. Pix pago a menos sobe na fila ---------------------------------------

confere('5b: Pix de R$ 50 para pedido de R$ 68 vira motivo "valor-diferente" na fila', () => {
  const c = { id: 'c-h', nome: 'Helena', estado: 'Em atendimento', bloqueio: null, mensagens: [],
    pedido: { numero: '2026-0205', estado: 'preparo', cobranca: cobrancaDePixAPagarMenos() } }
  const motivo = motivoDePrecisar(c, AGORA, false)
  assert.equal(motivo?.chave, 'valor-diferente')
  assert.equal(motivo?.rotulo, 'Valor diferente')
})

confere('5b guarda: diferença já aceita pela dona não sobe na fila', () => {
  const c = { id: 'c-h', nome: 'Helena', estado: 'Em atendimento', bloqueio: null, mensagens: [],
    pedido: { numero: '2026-0205', estado: 'preparo', cobranca: cobrancaDePixAPagarMenos({ liberadaEm: AGORA - 60000 }) } }
  assert.equal(motivoDePrecisar(c, AGORA, false), null)
})

// --- 5c. Motivo genérico não vence sinal específico --------------------------

confere('5c: passagem genérica não vence "Diz que pagou"', () => {
  const c = conversaComPassagem(MOTIVO_PERGUNTA_FORA_DO_ROTEIRO, 'Mandei o pix agora')
  assert.equal(motivoDePrecisar(c, AGORA, false)?.chave, 'diz-que-pagou')
})

confere('5c: passagem genérica não vence devolução', () => {
  const c = conversaComPassagem(MOTIVO_PERGUNTA_FORA_DO_ROTEIRO, 'quero meu dinheiro de volta')
  assert.equal(motivoDePrecisar(c, AGORA, false)?.chave, 'devolucao')
})

confere('5c guarda: passagem genérica sem sinal específico segue como motivo da passagem', () => {
  const c = conversaComPassagem(MOTIVO_PERGUNTA_FORA_DO_ROTEIRO, 'Qual o horário de vocês?')
  const motivo = motivoDePrecisar(c, AGORA, false)
  assert.equal(motivo?.chave, 'passagem')
  assert.equal(motivo?.texto, MOTIVO_PERGUNTA_FORA_DO_ROTEIRO)
})

confere('5c guarda: passagem com motivo específico (restrição) continua na frente', () => {
  const c = conversaComPassagem('Restrição alimentar, confira a ficha do prato antes de responder', 'Mandei o pix agora')
  assert.equal(motivoDePrecisar(c, AGORA, false)?.chave, 'passagem')
})

// --- 6. Prazo da reclamação conta antes de Assumir ---------------------------

confere('6: reclamação "quero meu dinheiro de volta" há 2 h, automático ligado: respostaAtrasada', () => {
  const c = conversaSemPedido('Quero meu dinheiro de volta', ATRASO)
  assert.equal(respostaAtrasada(c, AGORA, false), true)
})

confere('6 guarda: agradecimento sem pergunta há 2 h, automático ligado: não estoura', () => {
  const c = conversaSemPedido('Obrigada, pode demorar, sem pressa', ATRASO)
  assert.equal(respostaAtrasada(c, AGORA, false), false)
})

// --- 7. Palavra de impedimento mantém a espera -------------------------------

confere('7: "não consigo abrir o link" sem "?" há 2 h, automático ligado: fica em Precisa de você', () => {
  const c = conversaSemPedido('Não consigo abrir o link', ATRASO)
  assert.equal(respostaAtrasada(c, AGORA, false), true)
  assert.equal(motivoDePrecisar(c, AGORA, false)?.chave, 'esperando')
})

confere('7 guarda: "obrigada" sem pergunta nem impedimento não entra', () => {
  const c = conversaSemPedido('Obrigada', ATRASO)
  assert.equal(motivoDePrecisar(c, AGORA, false), null)
})

// Revisão da PR: a palavra de impedimento termina em limite de palavra, senão "abrem" e "verificar" entravam na fila.
confere('7 guarda: "vocês não abrem domingo" e "não consigo verificar" não entram', () => {
  for (const fala of ['Vocês não abrem domingo', 'Não consigo verificar agora']) {
    assert.equal(motivoDePrecisar(conversaSemPedido(fala, ATRASO), AGORA, false), null, fala)
  }
})

confere('7: "o link não abre" e "não consigo ver o cardápio" entram', () => {
  for (const fala of ['O link não abre', 'Não consigo ver o cardápio']) {
    assert.equal(motivoDePrecisar(conversaSemPedido(fala, ATRASO), AGORA, false)?.chave, 'esperando', fala)
  }
})

console.log(`\n${passou} casos ok, ${falhas.length} falhas`)
if (falhas.length > 0) process.exitCode = 1
