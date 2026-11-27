// #3381 (r3 P1-3): o aviso de restrição alimentar diz o que evitar, curto ("Sem lactose: confira a ficha do prato"),
// a partir da fala do cliente; sem item reconhecido cai no texto genérico. O motivo inteiro segue no detalhe (title).
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      try { return proximo(spec + '.js', contexto) } catch { /* resolução normal */ }
    }
    return proximo(spec, contexto)
  },
})
const { itemDaRestricao } = await import('../src/dominio/agente.js')
const { avisoDeRestricao } = await import('../src/dominio/automatico.js')
const fala = (...textos) => ({ mensagens: textos.map((texto) => ({ dir: 'in', texto })) })

assert.equal(itemDaRestricao(fala('Bom dia. Vocês têm alguma massa sem leite na receita?', 'É pra minha esposa, ela não pode lactose de jeito nenhum.')), 'lactose', 'vale o item dito por último')
assert.equal(itemDaRestricao(fala('Minha filha é celíaca, qualquer traço já dá problema')), 'glúten')
assert.equal(itemDaRestricao(fala('Pode ser sem ovo?')), 'ovo')
assert.equal(itemDaRestricao(fala('meu neto não pode comer amendoim')), 'amendoim')
assert.equal(itemDaRestricao(fala('tenho alergia a camarão')), 'camarão')
assert.equal(itemDaRestricao(fala('Quanto custa a lasanha?')), null)
assert.equal(itemDaRestricao(fala('Claro, sem pressa')), null)
// Revisão da #3381: lista fechada; palavra livre não vira item, "sem" pede espaço, e o item dito lá atrás ainda vale.
assert.equal(itemDaRestricao(fala('não consigo abrir o link')), null)
assert.equal(itemDaRestricao(fala('não posso esperar')), null)
assert.equal(itemDaRestricao(fala('no semestre que vem')), null)
assert.equal(itemDaRestricao(fala('não pode ingerir lactose')), 'lactose')
assert.equal(itemDaRestricao(fala('ela é alérgica a camarão', 'ok', 'pode ser às 19h', 'e o pix?')), 'camarão')
// Segunda revisão: plural, nozes, frutos do mar, intolerância e "zero"; a alergia vence um "sem X" de pergunta depois.
assert.equal(itemDaRestricao(fala('alergia a camarões')), 'camarão')
assert.equal(itemDaRestricao(fala('alergia a peixes')), 'peixe')
assert.equal(itemDaRestricao(fala('alergia a nozes')), 'nozes')
assert.equal(itemDaRestricao(fala('alérgico a frutos do mar')), 'frutos do mar')
assert.equal(itemDaRestricao(fala('sou intolerante a lactose')), 'lactose')
assert.equal(itemDaRestricao(fala('quero zero lactose')), 'lactose')
assert.equal(itemDaRestricao(fala('alergia a camarão', 'ok', 'tem suco sem leite?')), 'camarão')

assert.equal(avisoDeRestricao('lactose').texto, 'Sem lactose: confira a ficha do prato')
assert.match(avisoDeRestricao('lactose').detalhe, /^Restrição alimentar, confira a ficha do prato antes de responder/)
assert.equal(avisoDeRestricao(null).texto, 'Restrição alimentar, confira a ficha do prato antes de responder')
assert.ok(avisoDeRestricao('lactose').texto.length <= 40, 'curto o bastante para não cortar')
console.log('prova-3381-aviso-restricao: ok')
