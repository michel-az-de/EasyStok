// #3076 (#2776): o Endereço do Cadastro de clientes ia só como texto solto. A Ficha mostrava "Endereço: Rua das Flores,
// 100" e, embaixo, "Nenhum endereço cadastrado"; o Novo pedido abria sem endereço e recusava o mesmo texto sem CEP.
// Agora o Cadastro recusa o que o pedido recusaria e grava o endereço estruturado padrão quando a pessoa não tem um.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(s, c, next) { if (s.startsWith('.') && !/\.[a-z]+$/i.test(s)) { try { return next(s + '.js', c) } catch { /* resolução original */ } } return next(s, c) },
})
const { erroDoEnderecoDoCadastro, enderecoDeEntregaDoCadastro, gravaEnderecoDoCadastro } = await import('../src/dominio/fichaCliente.js')
import { readFileSync } from 'node:fs'

// Recusa igual à do pedido, só quando o endereço mudou (cadastro antigo sem CEP salva os outros campos).
assert.match(erroDoEnderecoDoCadastro({ endereco: 'Rua das Flores, 100' }) ?? '', /CEP no fim/, 'novo sem CEP')
assert.equal(erroDoEnderecoDoCadastro({ endereco: 'Rua das Flores, 100, Pinheiros, 05422-010' }), null, 'com CEP')
assert.equal(erroDoEnderecoDoCadastro({ endereco: '' }), null, 'sem endereço é só não informar')
assert.equal(erroDoEnderecoDoCadastro({ endereco: 'Rua velha, 1' }, { endereco: 'Rua velha, 1' }), null, 'endereço antigo sem mudança não trava')

// Endereço de entrega estruturado, com o Complemento do formulário.
const corpo = enderecoDeEntregaDoCadastro({ endereco: 'Rua das Flores, 100, Pinheiros, 05422-010', apt: 'apto 12' }, { empresaId: 'e1' })
assert.deepEqual(
  { logradouro: corpo.logradouro, numero: corpo.numero, complemento: corpo.complemento, bairro: corpo.bairro, cep: corpo.cep, padrao: corpo.padrao, empresaId: corpo.empresaId },
  { logradouro: 'Rua das Flores', numero: '100', complemento: 'apto 12', bairro: 'Pinheiros', cep: '05422010', padrao: true, empresaId: 'e1' })
assert.equal(enderecoDeEntregaDoCadastro({ endereco: 'Rua sem CEP, 1' }, { empresaId: 'e1' }), null, 'sem CEP não vira endereço de entrega')
assert.equal(enderecoDeEntregaDoCadastro({ endereco: 'Rua A, 1, Centro, 01001-000' }, { empresaId: 'e1', inicial: { endereco: 'Rua A, 1, Centro, 01001-000' } }), null, 'sem mudança, nada a gravar')
assert.equal(enderecoDeEntregaDoCadastro({ endereco: 'Rua B, 2, Centro, 01001-000' }, { empresaId: 'e1', inicial: { id: 'c1', endereco: 'x' }, enderecos: [{ id: 'a', padrao: true }] }), null, 'quem já tem padrão (relido da API) escolhe na Ficha')
assert.ok(enderecoDeEntregaDoCadastro({ endereco: 'Rua B, 2, Centro, 01001-000' }, { empresaId: 'e1', inicial: { id: 'c1', endereco: 'x' }, enderecos: [] }), 'editou e não tem padrão: grava')
assert.equal(enderecoDeEntregaDoCadastro({ endereco: 'Rua B, 2, Centro, 01001-000' }, { empresaId: 'e1', inicial: { id: 'c1', endereco: 'x' } }), null, 'cadastro existente sem os endereços relidos não arrisca um segundo padrão')

// Só vale a pena reler e gravar quando o endereço mudou e tem CEP.
assert.equal(gravaEnderecoDoCadastro({ endereco: 'Rua B, 2, Centro, 01001-000' }, { endereco: 'x' }), true)
assert.equal(gravaEnderecoDoCadastro({ endereco: 'Rua B, 2' }, { endereco: 'x' }), false, 'sem CEP')
assert.equal(gravaEnderecoDoCadastro({ endereco: 'x' }, { endereco: 'x' }), false, 'sem mudança')

// Ligação no editor (revisão da #3076): a Ficha abaixo do formulário pode gravar endereço depois que o editor abriu, e a
// API não desmarca o Padrão anterior. O editor relê os endereços antes de decidir e só então grava.
const editor = readFileSync(new URL('../src/features/gestao/cadastros/EditorCadastro.jsx', import.meta.url), 'utf8')
assert.match(editor, /gravaEnderecoDoCadastro\(dados, inicial\)[\s\S]{0,400}obterCadastro\('clientes', atual\.id[\s\S]{0,300}enderecoDeEntregaDoCadastro\([\s\S]{0,200}adicionarEnderecoCliente\(atual\.id/, 'o editor relê os endereços antes de gravar o Padrão')
console.log('ok prova-3076-endereco-cadastro')
