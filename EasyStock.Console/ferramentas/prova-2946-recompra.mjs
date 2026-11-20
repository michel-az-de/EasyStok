/* eslint-disable no-console */
// #2946: lista de recompra. Telefone para wa.me, texto do convite e resumo "N pedidos · último há X dias".
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})
const { linkDoConvite, resumoDaRecompra, telefoneParaWhatsapp, textoDoConvite } = await import('../src/dominio/recompra.js')

assert.equal(telefoneParaWhatsapp('11999990000'), '5511999990000', '11 dígitos ganham 55')
assert.equal(telefoneParaWhatsapp('(11) 8888-0000'), '551188880000', 'celular antigo de 10 dígitos ganha 55')
assert.equal(telefoneParaWhatsapp('+55 11 99999-0000'), '5511999990000', 'com 55 e 13 dígitos fica')
assert.equal(telefoneParaWhatsapp('551188880000'), '551188880000', 'com 55 e 12 dígitos fica')
for (const ruim of [null, undefined, '', 'abc', '99999', '12345678901234', '5511999', '(11) 3333-0000', '551133330000', '11899990000']) assert.equal(telefoneParaWhatsapp(ruim), null, `fora do padrão: ${ruim}`)

const texto = textoDoConvite('Maria Souza', 'Loja Teste')
assert.ok(texto.startsWith('Oi, Maria! Aqui é da Loja Teste.'), 'só o primeiro nome e o nome da loja')
assert.ok(!texto.includes('Souza'), 'não usa o sobrenome')
assert.ok(textoDoConvite('', null).startsWith('Oi! Aqui é da Casa da Baba.'), 'sem nome na sessão vale o nome da Casa')
assert.ok(textoDoConvite(null, 'X').startsWith('Oi! '), 'nome nulo')
assert.ok(!/[\u{1F300}-\u{1FAFF}\u2600-\u27BF]/u.test(texto), 'sem emoji')

const link = linkDoConvite('11999990000', 'Maria', 'Loja Teste')
assert.ok(link.startsWith('https://wa.me/5511999990000?text='), 'link com DDI')
assert.equal(decodeURIComponent(link.split('?text=')[1]), textoDoConvite('Maria', 'Loja Teste'), 'texto codificado volta igual')
assert.equal(linkDoConvite('123', 'Maria'), null, 'telefone inválido não gera link')

const agora = Date.parse('2026-11-20T15:00:00Z')
assert.equal(resumoDaRecompra(3, '2026-10-10T15:00:00Z', agora), '3 pedidos · último há 41 dias')
assert.equal(resumoDaRecompra(2, '2026-11-19T15:00:00Z', agora), '2 pedidos · último há 1 dia', 'singular de dia')
assert.equal(resumoDaRecompra(1, '2026-11-20T10:00:00Z', agora), '1 pedido · último hoje', 'singular de pedido')
assert.equal(resumoDaRecompra(2, null, agora), '2 pedidos', 'sem data não quebra')
assert.equal(resumoDaRecompra(2, 'lixo', agora), '2 pedidos', 'data inválida não quebra')
assert.equal(resumoDaRecompra(undefined, null, agora), '0 pedidos', 'sem contagem não quebra')
console.log('prova-2946-recompra: ok')
