/* eslint-disable no-console */
// Prova da issue #1608: as três filas do painel "Agora" da entrada do console
// (Responder, Cozinhar, Despachar) saem das MESMAS regras do Balcão e da
// Cozinha (dominio/automatico.js e dominio/cozinha.js):
//
//   node ferramentas/prova-1608-painel-agora.mjs
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

const { filasDoAgora } = await import('../src/dominio/agora.js')
const { contarPrecisaDeVoce } = await import('../src/dominio/automatico.js')

let passou = 0
const confere = (descricao, fn) => { fn(); passou += 1; console.log('ok  ' + descricao) }

const AGORA = new Date('2026-10-11T11:05:00-03:00').getTime()
const ha = (minutos) => new Date(AGORA - minutos * 60000).toISOString()
const JANELAS = [
  { id: 'j-almoco', faixa: '11h30 às 12h30', corteMinutos: 60 },
  { id: 'j-noite', faixa: '19h30 às 20h30', corteMinutos: 60 },
]
const conversa = (id, nome, extra = {}) => ({
  id, nome, estado: 'Aberto', cliente: { tags: [] }, mensagens: [], ...extra,
})
const pedido = (numero, estado, janela) => ({ numero, estado, janela })

const CONVERSAS = [
  conversa('c1', 'Renan', { passagem: { assumida: false, motivo: 'Restrição alimentar' } }),
  conversa('c2', 'Bianca', { mensagens: [{ dir: 'in', texto: 'Posso retirar aí?', em: ha(40) }] }),
  conversa('c3', 'Calmo', { mensagens: [{ dir: 'in', texto: 'Obrigado!', em: ha(1) }] }),
  conversa('c4', 'Noite', { pedido: pedido('0191', 'pago', 'j-noite') }),
  conversa('c5', 'Almoço atrasado', { pedido: pedido('0184', 'pago', 'j-almoco') }),
  conversa('c6', 'No fogo', { pedido: pedido('0187', 'preparo', 'j-almoco') }),
  conversa('c7', 'Embalado', { pedido: pedido('0188', 'embalado', 'j-almoco') }),
  conversa('c8', 'Na rua', { pedido: pedido('0181', 'entrega', 'j-almoco') }),
  conversa('c9', 'Entregue', { pedido: pedido('0170', 'entregue', 'j-almoco') }),
  conversa('c10', 'Sem pagar', { pedido: pedido('0194', 'aguardando', 'j-noite') }),
  conversa('c11', 'Encerrada', { estado: 'Encerrado', passagem: { assumida: false, motivo: 'x' } }),
]

const filas = filasDoAgora(CONVERSAS, AGORA, { janelas: JANELAS })

confere('Responder bate com a contagem de "Precisa de você" do Balcão', () => {
  const abertas = CONVERSAS.filter((c) => c.estado !== 'Encerrado')
  assert.equal(filas.responder.length, contarPrecisaDeVoce(abertas, AGORA, {}, true, JANELAS))
  assert.ok(filas.responder.some((i) => i.id === 'c1'))
  assert.ok(filas.responder.some((i) => i.id === 'c2'))
  assert.ok(!filas.responder.some((i) => i.id === 'c3'))
})

confere('Responder leva o motivo escrito e põe o que é perigo primeiro', () => {
  const tons = filas.responder.map((i) => i.tom)
  assert.deepEqual(tons, [...tons].sort((a, b) => (a === 'perigo' ? 0 : 1) - (b === 'perigo' ? 0 : 1)))
  assert.ok(filas.responder.every((i) => i.rotulo && i.texto && i.nome))
})

confere('conversa encerrada não entra em Responder', () => {
  assert.ok(!filas.responder.some((i) => i.id === 'c11'))
})

confere('Cozinhar tem pago e em preparo, atrasado primeiro e escrito', () => {
  assert.deepEqual(filas.cozinhar.map((i) => i.id), ['c5', 'c6', 'c4'])
  assert.equal(filas.cozinhar[0].situacao, 'Atrasado')
  assert.equal(filas.cozinhar[0].atrasado, true)
  assert.equal(filas.cozinhar[1].situacao, 'Em preparo')
  assert.equal(filas.cozinhar[2].situacao, 'Esperando preparo')
  assert.equal(filas.cozinhar[2].faixa, '19h30 às 20h30')
})

confere('Despachar tem embalado antes de quem já saiu', () => {
  assert.deepEqual(filas.despachar.map((i) => i.id), ['c7', 'c8'])
  assert.equal(filas.despachar[0].situacao, 'Embalado')
})

confere('entregue e aguardando pagamento ficam fora da cozinha e do despacho', () => {
  const ids = [...filas.cozinhar, ...filas.despachar].map((i) => i.id)
  assert.ok(!ids.includes('c9') && !ids.includes('c10'))
})

confere('sem conversa nenhuma as três filas vêm vazias, sem quebrar', () => {
  assert.deepEqual(filasDoAgora(null, AGORA), { responder: [], cozinhar: [], despachar: [] })
})

console.log(`\n${passou} verificações passaram`)
