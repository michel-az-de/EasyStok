/* eslint-disable no-console */
// Prova #1804 e #1805 (revisão gravada de 14/10): conversa reaberta de pedido já concluído não
// fica em "Precisa de você" sem o cliente ter escrito, e a cobrança não "gera e envia" com a
// janela de 24 h fechada.
//
//   node ferramentas/prova-1804-balcao-fim.mjs

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

const { motivoDePrecisar } = await import('../src/dominio/automatico.js')
const { motivoAntesDeCobrar } = await import('../src/dominio/resumoPedido.js')

let passos = 0
const ok = (texto) => { passos += 1; console.log(`ok ${passos} - ${texto}`) }
const AGORA = Date.parse('2026-10-14T20:20:00Z')

const reaberta = (estadoDoPedido, ultimaFala) => ({
  id: 'c5', nome: 'Cliente Cinco', estado: 'Em atendimento', canal: 'WhatsApp',
  passagem: { motivo: 'Conversa reaberta para atendimento.', origem: 'reabertura', em: null, assumida: false },
  pedido: { numero: '5', estado: estadoDoPedido, itens: [{ sku: 'X', qtd: 1 }] },
  mensagens: [
    { id: 'm1', dir: 'in', texto: 'Oi', em: '2026-10-14T13:00:00Z' },
    { id: 'm2', dir: 'out', texto: 'Que bom que você gostou!', em: '2026-10-14T14:05:00Z', automatica: true },
    ...(ultimaFala === 'in' ? [{ id: 'm3', dir: 'in', texto: 'Mais uma coisa', em: '2026-10-14T20:00:00Z' }] : []),
  ],
})

assert.equal(motivoDePrecisar(reaberta('entregue', 'out'), AGORA)?.chave ?? null, null,
  'reaberta, pedido entregue e o cliente não escreveu: nada pendente')
ok('reaberta com pedido entregue e sem fala nova do cliente sai de "Precisa de você"')

assert.equal(motivoDePrecisar(reaberta('entregue', 'in'), AGORA)?.chave, 'passagem',
  'o cliente escreveu depois: volta a precisar de você')
ok('se o cliente escreve, a reaberta volta a "Precisa de você"')

assert.equal(motivoDePrecisar(reaberta('preparo', 'out'), AGORA)?.chave, 'passagem',
  'pedido em andamento segue pendente')
ok('reaberta com pedido em andamento continua em "Precisa de você"')

const pedidoPronto = { numero: '4', estado: 'aguardando', janela: 'j1|2026-10-15', itens: [{ sku: 'X', qtd: 2 }] }
const pronto = { pedido: pedidoPronto, clienteId: 'cli', endereco: 'Rua A, 1' }
const fechada = { pode: false, ofereceModelo: true, motivo: { titulo: 'Janela fechada.', detalhe: 'Só modelo aprovado sai até ele responder.' } }
const motivo = motivoAntesDeCobrar(pronto, { fonteApi: true, escrita: fechada })
assert.ok(motivo && /Janela fechada/.test(motivo) && /modelo aprovado/.test(motivo), `com a janela fechada a cobrança não sai: ${motivo}`)
assert.ok(/rascunho/.test(motivo), 'diz para onde vai a comanda anotada')
ok(`janela fechada trava "Gerar cobrança e enviar": "${motivo}"`)

assert.equal(motivoAntesDeCobrar(pronto, { fonteApi: true, escrita: { pode: true } }), null, 'janela aberta não trava')
assert.equal(motivoAntesDeCobrar({ ...pronto, pedido: { ...pedidoPronto, pedidoId: 'p1' } }, { fonteApi: true, escrita: fechada }), null,
  'pedido já criado não volta a ser travado pela janela')
assert.equal(motivoAntesDeCobrar(pronto, { fonteApi: false, escrita: fechada }), null, 'demonstração segue igual')
ok('janela aberta, pedido já criado e demonstração não mudam')

console.log(`\n${passos} passos verdes.`)
