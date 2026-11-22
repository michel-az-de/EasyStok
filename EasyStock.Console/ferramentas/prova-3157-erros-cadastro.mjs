// #3157 e #3192 (re-pontuação da #3076): no Novo cliente, e-mail "abc@" só tinha o balão nativo e o documento "123" era aceito.
// Regras de domínio: e-mail com algo@domínio.tld; documento vazio ou CPF/CNPJ com dígito verificador conferido.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(s, c, next) { if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução original */ } } return next(s, c) },
})
const { erroDoEmailDoCadastro, erroDoDocumentoDoCadastro } = await import('../src/dominio/validacaoDoCadastro.js')

assert.ok(erroDoEmailDoCadastro('abc@'), '"abc@" é recusado')
assert.ok(erroDoEmailDoCadastro('abc'), 'sem arroba é recusado')
assert.ok(erroDoEmailDoCadastro('abc@dominio'), 'sem ponto no domínio é recusado')
assert.ok(erroDoEmailDoCadastro('a b@dominio.com'), 'com espaço é recusado')
assert.match(erroDoEmailDoCadastro('abc@'), /e-mail/i, 'o texto diz o que corrigir')
assert.equal(erroDoEmailDoCadastro('ana@exemplo.com.br'), null)
assert.equal(erroDoEmailDoCadastro('  ana@exemplo.com  '), null, 'espaços nas pontas não contam')
assert.equal(erroDoEmailDoCadastro(''), null, 'vazio é permitido')
assert.equal(erroDoEmailDoCadastro(null), null)

assert.ok(erroDoDocumentoDoCadastro('123'), '"123" é recusado')
assert.match(erroDoDocumentoDoCadastro('123'), /CPF|CNPJ/)
assert.ok(erroDoDocumentoDoCadastro('111.111.111-11'), 'repetidos não são CPF')
assert.ok(erroDoDocumentoDoCadastro('529.982.247-26'), 'dígito verificador errado')
assert.equal(erroDoDocumentoDoCadastro('529.982.247-25'), null, 'CPF válido, com máscara')
assert.equal(erroDoDocumentoDoCadastro('52998224725'), null, 'CPF válido, só dígitos')
assert.ok(erroDoDocumentoDoCadastro('11.222.333/0001-80'), 'CNPJ com dígito errado')
assert.equal(erroDoDocumentoDoCadastro('11.222.333/0001-81'), null, 'CNPJ válido')
assert.equal(erroDoDocumentoDoCadastro('11222333000181'), null)
assert.equal(erroDoDocumentoDoCadastro(''), null, 'vazio é permitido')
assert.equal(erroDoDocumentoDoCadastro('   '), null)
assert.equal(erroDoDocumentoDoCadastro(undefined), null)
console.log('ok prova-3157-erros-cadastro')
