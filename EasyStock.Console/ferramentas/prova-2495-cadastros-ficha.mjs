/* eslint-disable no-console */
// #2495 (S6, I4): regras da ficha do cliente no editor de Cadastros. Endereço estruturado com bairro e CEP,
// telefones extras e o resumo de pedidos. A API só adiciona e remove: não há edição de item gravado.
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
const {
  errosDoEndereco, corpoDoEndereco, linhaDoEndereco, cepNaTela, erroDoTelefoneExtra, corpoDoTelefone, resumoDePedidos, mensagemDaFicha, aplicarNaFicha, enderecoVaiComoPadrao,
} = await import('../src/dominio/fichaCliente.js')

// Endereço: rua obrigatória; CEP, quando preenchido, com 8 dígitos; UF com 2 letras.
assert.deepEqual(errosDoEndereco({ logradouro: '  ' }), { logradouro: 'Informe a rua ou avenida.' })
assert.deepEqual(errosDoEndereco({ logradouro: 'Rua A', cep: '0123' }), { cep: 'CEP precisa de 8 dígitos' })
assert.deepEqual(errosDoEndereco({ logradouro: 'Rua A', cep: '012345678' }), { cep: 'CEP precisa de 8 dígitos' })
assert.deepEqual(errosDoEndereco({ logradouro: 'Rua A', estado: 'São' }), { estado: 'Use a sigla com 2 letras, como SP.' })
assert.deepEqual(errosDoEndereco({ logradouro: 'Rua A', cep: '01234-567', estado: 'sp' }), {})

// Corpo: texto aparado, vazio vira null, UF maiúscula, CEP só dígitos; o primeiro endereço é o padrão.
assert.deepEqual(corpoDoEndereco({ logradouro: ' Rua das Flores ', numero: '10', complemento: '', bairro: 'Centro', cidade: 'São Paulo', estado: 'sp', cep: '01234-567', referencia: ' ' }, 'E', true), {
  empresaId: 'E', logradouro: 'Rua das Flores', numero: '10', complemento: null, bairro: 'Centro', cidade: 'São Paulo', estado: 'SP', cep: '01234567', referencia: null, padrao: true,
})
assert.equal(corpoDoEndereco({ logradouro: 'Rua B' }, 'E', false).padrao, false)

// Linha do endereço na lista.
assert.deepEqual(linhaDoEndereco({ logradouro: 'Rua das Flores', numero: '10', complemento: 'Apto 3', bairro: 'Centro', cidade: 'São Paulo', estado: 'SP', cep: '01234567' }),
  { principal: 'Rua das Flores, 10 - Apto 3', detalhe: 'Centro · São Paulo/SP · CEP 01234-567' })
assert.deepEqual(linhaDoEndereco({ logradouro: 'Rua B', bairro: null, cidade: 'Santos' }), { principal: 'Rua B', detalhe: 'Santos' })
assert.equal(cepNaTela('01234567'), '01234-567')
assert.equal(cepNaTela(null), '')

// Telefone extra: com DDD, como no cadastro rápido.
assert.equal(erroDoTelefoneExtra(''), 'Informe o telefone com DDD')
assert.equal(erroDoTelefoneExtra('3333-2222'), 'Telefone incompleto')
assert.equal(erroDoTelefoneExtra('(11) 3333-2222'), null)
assert.deepEqual(corpoDoTelefone(' (11) 3333-2222 ', false, 'E'), { empresaId: 'E', numero: '(11) 3333-2222', whatsapp: false })
assert.deepEqual(corpoDoTelefone('11955554444', 'sim', 'E'), { empresaId: 'E', numero: '11955554444', whatsapp: true })

// Resumo de pedidos: contagem e o último, no fuso da loja.
assert.deepEqual(resumoDePedidos({ orderCount: 0 }), { contagem: 'Nenhum pedido ainda', ultimo: null })
assert.deepEqual(resumoDePedidos({ orderCount: 1, lastOrderAt: '2026-11-10T15:30:00Z' }), { contagem: '1 pedido', ultimo: 'Último pedido em 10/11/2026, 12:30' })
assert.equal(resumoDePedidos({ orderCount: 7 }).contagem, '7 pedidos')

// A lista muda na hora com o que foi gravado; a recarga do detalhe só confirma (revisão S6, Major).
const listas = { enderecos: [{ id: 'E1', logradouro: 'Rua A', padrao: true }], telefones: [{ id: 'T1', numero: '1' }] }
assert.deepEqual(aplicarNaFicha(listas, 'removerTelefone', { id: 'T1' }).telefones, [])
assert.deepEqual(aplicarNaFicha(listas, 'removerEndereco', { id: 'E1' }).enderecos, [])
assert.deepEqual(aplicarNaFicha(listas, 'telefone', null, { numero: '2', whatsapp: false }, 'T2').telefones.at(-1), { id: 'T2', numero: '2', whatsapp: false })
assert.equal(aplicarNaFicha(listas, 'endereco', null, { logradouro: 'Rua B' }, 'E2').enderecos.length, 2)
// Padrão: vai como padrão quando nenhum endereço restante é o padrão, não só quando a lista está vazia.
assert.equal(enderecoVaiComoPadrao([]), true)
assert.equal(enderecoVaiComoPadrao([{ id: 'E1', padrao: false }]), true)
assert.equal(enderecoVaiComoPadrao(listas.enderecos), false)

// Mensagens.
assert.equal(mensagemDaFicha(Object.assign(new Error('x'), { status: 403 })), 'Seu acesso não permite mudar este cadastro. Peça a quem administra a loja.')
assert.equal(mensagemDaFicha(Object.assign(new Error('Cliente não encontrado.'), { status: 404 })), 'Cliente não encontrado.')
assert.equal(mensagemDaFicha(null), 'Não deu para concluir agora. Tente de novo.')
console.log('#2495 S6 domínio: endereço com bairro e CEP, telefone com DDD, primeiro endereço padrão e resumo de pedidos.')
