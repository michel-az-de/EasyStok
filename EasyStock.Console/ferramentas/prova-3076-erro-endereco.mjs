// #3076: no passo Entrega e pagamento a falta de endereço mandava "Cadastre o endereço no passo Entrega e pagamento",
// o passo em que a pessoa já estava, e repetia em vermelho a recusa do formato logo acima. No passo, aponta o campo.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(s, c, next) { if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução original */ } } return next(s, c) },
})
const { textoDoErro, erroDaSimulacaoNaEntrega } = await import('../src/aplicacao/janelaDoPedido.js')

const semEndereco = { status: 409, message: 'Cadastre o endereço de entrega do cliente antes de gerar o pedido.' }
assert.match(textoDoErro(semEndereco), /passo Entrega e pagamento/, 'fora do passo segue dizendo onde resolver')
assert.doesNotMatch(textoDoErro(semEndereco, { noPassoDaEntrega: true }), /passo Entrega e pagamento/, 'no passo não manda ir a ele')
assert.match(textoDoErro(semEndereco, { noPassoDaEntrega: true }), /campo Endereço de entrega/)

// Recusa do formato já está no campo: a falta de endereço da simulação não aparece de novo embaixo.
assert.equal(erroDaSimulacaoNaEntrega(semEndereco, 'Não salvei: escreva o endereço…'), null, 'uma mensagem só')
assert.equal(erroDaSimulacaoNaEntrega(semEndereco, null), semEndereco, 'sem recusa no campo, a da simulação aparece')
const esgotada = { status: 409, message: 'Janela esgotada.' }
assert.equal(erroDaSimulacaoNaEntrega(esgotada, 'Não salvei…'), esgotada, 'outro erro da simulação continua')
console.log('ok prova-3076-erro-endereco')
