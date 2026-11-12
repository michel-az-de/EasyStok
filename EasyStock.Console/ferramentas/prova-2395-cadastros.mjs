import assert from 'node:assert/strict'
import { corpoCliente, corpoFornecedor, corpoProduto, emailCadastro, filtrarSemCadastro, relacionamentoCadastro, chaveContextoCadastro, telefoneCadastro, termoBuscaCadastro } from '../src/dominio/cadastros.js'

const original = { id: 'c1', nome: 'Ana', tipoPessoa: 'juridica', nomeFantasia: 'Loja', inscricaoEstadual: '123', apt: '4', documento: 'legado', observacoes: 'Preservar' }
const cliente = corpoCliente({ ...original, nome: 'Ana Maria' }, 'empresa')
assert.equal(cliente.pessoaJuridica, true)
assert.equal(cliente.inscricaoEstadual, '123')
assert.equal(cliente.apt, '4')
assert.equal(cliente.empresaId, 'empresa')
assert.equal(cliente.nome, 'Ana Maria')
assert.equal(cliente.observacoes, 'Preservar')
const produto = corpoProduto({ produtoId: 'p1', nome: 'Lasanha', categoriaId: 'cat', tipo: 'Alimento', status: 'Ativo', precoReferencia: 32.5, atributosJson: '{"nutricao":"preservada"}', variacoes: [{ variacaoId: 'v1' }] }, 'empresa')
assert.equal(produto.variacoes, null, 'Edição cadastral não recria variações com saldo')
assert.equal(produto.embalagens, null)
assert.equal(produto.caracteristicas, null)
assert.equal(produto.atributosJson, '{"nutricao":"preservada"}')
assert.equal(produto.precoReferencia, 32.5)
assert.equal(corpoFornecedor({ nome: 'Fornecedor', leadTimeEstimadoDias: 3, pedidoMinimo: 'R$ 100' }, 'e').pedidoMinimo, 'R$ 100')
const conversas = [{ id: 'lead', clienteId: null, nome: 'João', telefone: '+5511998877665' }, { id: 'cliente', clienteId: 'c1', nome: 'João' }]
assert.deepEqual(filtrarSemCadastro(conversas, 'joao').map(c => c.id), ['lead'])
assert.equal(filtrarSemCadastro(conversas, '998877').length, 1)
const contatoWhatsApp = { id: 'wa', clienteId: null, canal: 'WhatsApp', contato: '5511912345678' }
const contatoEmail = { id: 'email', clienteId: null, canal: 'E-mail', contato: 'pessoa@fixture.test' }
assert.equal(telefoneCadastro(contatoWhatsApp), '5511912345678')
assert.equal(telefoneCadastro(contatoEmail), null, 'Endereço de e-mail não vira telefone')
assert.equal(emailCadastro(contatoEmail), 'pessoa@fixture.test')
assert.equal(telefoneCadastro({ canal: 'Chat do site', contato: '123456789' }), null, 'ID de visitante não vira telefone')
assert.deepEqual(filtrarSemCadastro([contatoWhatsApp, contatoEmail], '(11) 91234-5678').map(c => c.id), ['wa'])
assert.deepEqual(filtrarSemCadastro([contatoWhatsApp, contatoEmail], 'pessoa@fixture.test').map(c => c.id), ['email'])
assert.equal(relacionamentoCadastro({ orderCount: 0 }), 'Lead')
assert.equal(relacionamentoCadastro({ orderCount: 1 }), 'Cliente')
assert.equal(relacionamentoCadastro({}), 'Histórico não informado')
assert.equal(relacionamentoCadastro({ orderCount: null }), 'Histórico não informado')
assert.equal(termoBuscaCadastro('(11) 99988-7766'), '11999887766')
assert.equal(termoBuscaCadastro('11999887766'), '11999887766')
assert.equal(termoBuscaCadastro('Ana-Maria'), 'Ana-Maria')
assert.notEqual(chaveContextoCadastro('clientes', 'a'), chaveContextoCadastro('clientes', 'b'))
assert.notEqual(chaveContextoCadastro('clientes', 'a'), chaveContextoCadastro('produtos', 'a'))
console.log('Cadastros: verificações de preservação, relacionamento e contexto passaram.')
