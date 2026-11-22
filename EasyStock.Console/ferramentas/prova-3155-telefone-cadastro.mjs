// #3155 (re-pontuação da #3076): o Cadastro aceitava telefone "12" e celular que o Novo pedido recusa depois
// (regra da API `NormalizadorTelefone.EhCelularBr`: ^55([1-9][0-9])9([6-9]\d{7})$).
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(s, c, next) { if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução original */ } } return next(s, c) },
})
const { avisoDeCelularBr } = await import('../src/dominio/validacaoDoCadastro.js')
const { erroDoTelefoneExtra } = await import('../src/dominio/fichaCliente.js')
const { erroDoCelular } = await import('../src/dominio/celularDoPedido.js')

// 1. O Cadastro de clientes usa a regra do telefone extra (a mesma rota: Telefone.cs, 7 a 15 dígitos). Vazio fica de fora
//    na tela (é opcional) e o erro só vale para o número que mudou: o que já está gravado não trava a edição de outro campo.
assert.ok(erroDoTelefoneExtra('12'), '"12" é recusado')
assert.ok(erroDoTelefoneExtra('(11) 9876'), 'incompleto é recusado')
assert.equal(erroDoTelefoneExtra('(11) 98765-4321'), null, 'celular válido passa')
assert.equal(erroDoTelefoneExtra('(11) 3333-4444'), null, 'fixo com DDD passa')
assert.equal(erroDoTelefoneExtra('+44 20 7946 0958'), null, 'de fora do Brasil passa')

// 2. Número aceito que o Novo pedido recusa (fixo, 0800, de outro país, celular sem o 6 a 9) ganha aviso, sem bloquear.
assert.ok(avisoDeCelularBr('(11) 91234-5678'), 'depois do 9 vem 6 a 9 (Anatel)')
assert.ok(avisoDeCelularBr('(11) 3333-4444'), 'fixo não recebe pedido pelo WhatsApp')
assert.ok(avisoDeCelularBr('0800 123 4567'), '0800 não recebe pedido')
assert.ok(avisoDeCelularBr('+44 20 7946 0958'), 'de fora: o Novo pedido só aceita celular do Brasil')
assert.equal(avisoDeCelularBr('(11) 98765-4321'), null)
assert.equal(avisoDeCelularBr('5511987654321'), null, 'com o 55 na frente')
assert.equal(avisoDeCelularBr('+55 11 98765-4321'), null, 'com +55')
assert.equal(avisoDeCelularBr(''), null, 'vazio não avisa')
assert.equal(avisoDeCelularBr('12'), null, 'inválido já tem o erro, sem aviso por cima')

// 3. O celular do Novo pedido segue a mesma regra da API.
assert.ok(erroDoCelular('(11) 91234-5678'), 'Novo pedido recusa antes de ir à API')
assert.equal(erroDoCelular('(11) 98765-4321'), null)
assert.equal(erroDoCelular('(21) 96543-2109'), null)
console.log('ok prova-3155-telefone-cadastro')
