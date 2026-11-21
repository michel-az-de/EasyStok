/* eslint-disable no-console */
// #2495 (S2, I1): inativar e reativar cliente e fornecedor. O texto da confirmação diz o que acontece e o
// erro da API vira frase de quem usa a loja (a API responde sem acento e o 403 vem sem corpo).
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
const { confirmacaoDeAtividade, mensagemDeAtividade, temAtividadeNoEditor } = await import('../src/dominio/cadastros.js')

assert.equal(temAtividadeNoEditor('clientes', { id: 'A' }), true)
assert.equal(temAtividadeNoEditor('fornecedores', { id: 'F' }), true)
assert.equal(temAtividadeNoEditor('clientes', {}), false, 'Cadastro novo não inativa')
assert.equal(temAtividadeNoEditor('produtos', { id: 'P' }), false, 'Produto tem situação própria no formulário')
assert.equal(temAtividadeNoEditor('bancos', { id: 'B' }), false, 'Conta tem a caixa Conta ativa')

const inativar = confirmacaoDeAtividade('clientes', { nome: 'Ana', ativo: true })
assert.equal(inativar.ativar, false)
assert.equal(inativar.titulo, 'Inativar Ana?')
assert.equal(inativar.botao, 'Inativar cliente')
assert.match(inativar.texto, /histórico/i, 'Diz que o histórico fica')
assert.match(inativar.texto, /reativar/i, 'Diz que dá para voltar')
assert.equal(inativar.sucesso, 'O cadastro de Ana foi inativado.')
const reativar = confirmacaoDeAtividade('fornecedores', { nome: 'Moinho', ativo: false })
assert.equal(reativar.ativar, true)
assert.equal(reativar.titulo, 'Reativar Moinho?')
assert.equal(reativar.botao, 'Reativar fornecedor')
assert.equal(reativar.sucesso, 'O cadastro de Moinho foi reativado.')
assert.equal(confirmacaoDeAtividade('clientes', { nome: 'Bia', status: 'Inativo' }).ativar, true, 'status Inativo também conta')

// Erro do fornecedor com pedido aberto: frase com acento e o próximo passo.
const pedidoAberto = { status: 400, message: 'Nao e permitido desativar fornecedor com pedido aberto ou em transito.' }
assert.equal(mensagemDeAtividade('fornecedores', false, pedidoAberto),
  'Este fornecedor tem pedido de compra aberto ou em trânsito. Conclua ou cancele o pedido antes de inativar.')
assert.equal(mensagemDeAtividade('clientes', false, { status: 403, message: 'Seu usuário não tem permissão para isso.' }),
  'Seu acesso não permite inativar cliente. Peça a quem administra a loja.')
assert.equal(mensagemDeAtividade('fornecedores', true, { status: 403, message: 'x' }),
  'Seu acesso não permite reativar fornecedor. Peça a quem administra a loja.')
assert.equal(mensagemDeAtividade('clientes', false, { status: 500, message: 'Falhou no servidor.' }), 'Falhou no servidor.', 'O resto segue a mensagem da API')
console.log('#2495 S2 domínio: inativar e reativar só no editor de cliente e fornecedor, com texto e erro legíveis.')
