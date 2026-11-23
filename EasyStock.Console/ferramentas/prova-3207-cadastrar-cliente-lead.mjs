/* eslint-disable no-console */
// Prova #3207 (S82): converter lead em cliente tem um caminho só. O cartão do lead mostra UM botão
// principal "Cadastrar cliente"; vincular a um cadastro existente vira ação discreta; o formulário
// abre preenchido com o que a conversa captou (nome, telefone, endereço) e continua editável.
//
//   node ferramentas/prova-3207-cadastrar-cliente-lead.mjs

import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

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

const {
  acoesDoCartao, leadSemCadastro, valoresIniciaisDoCadastro, bannerDoEnderecoCapturado,
  ROTULO_CADASTRAR_CLIENTE, ROTULO_VINCULAR_LEAD, ROTULO_CONFERIR_IDENTIDADE,
} = await import('../src/dominio/cadastroDoLead.js')

let passos = 0
const ok = (texto) => { passos += 1; console.log(`ok ${passos} - ${texto}`) }

const lead = { conta: 'lead', canal: 'WhatsApp', clienteId: null, nome: 'Marina (perfil)', contato: '5511987654321', cliente: { telefone: '' } }
const cliente = { conta: 'cliente', clienteId: 'c1', nome: 'Marina', cliente: { telefone: '11987654321' } }
const clienteDaDemo = { conta: 'cliente', clienteId: null, nome: 'Marina', cliente: {} }

// 1. Ação principal do cartão
assert.equal(leadSemCadastro(lead), true)
assert.equal(leadSemCadastro(cliente), false)
assert.equal(leadSemCadastro(clienteDaDemo), false)
assert.equal(leadSemCadastro({ ...lead, clienteId: 'c9' }), false)
ok('só o lead sem cadastro ligado é lead sem cadastro (cliente da demonstração não é)')

assert.equal(ROTULO_CADASTRAR_CLIENTE, 'Cadastrar cliente')
assert.deepEqual(acoesDoCartao(lead, { fonteApi: true }), { cadastrar: true, vincular: true, rotuloDoVinculo: ROTULO_VINCULAR_LEAD })
assert.equal(ROTULO_VINCULAR_LEAD, 'Já é cliente? Vincular a um cadastro')
ok('lead no servidor: "Cadastrar cliente" principal e vínculo como ação secundária com texto de pergunta')

assert.deepEqual(acoesDoCartao(lead, { fonteApi: false }), { cadastrar: true, vincular: false, rotuloDoVinculo: ROTULO_VINCULAR_LEAD })
ok('lead na demonstração: cadastra, mas não há vínculo (quem confere candidatos é o servidor)')

assert.deepEqual(acoesDoCartao(cliente, { fonteApi: true }), { cadastrar: false, vincular: true, rotuloDoVinculo: ROTULO_CONFERIR_IDENTIDADE })
assert.equal(acoesDoCartao(clienteDaDemo, { fonteApi: false }).cadastrar, false)
ok('cliente cadastrado: sem "Cadastrar cliente"; continua "Conferir identidade" para corrigir o vínculo')

// 2. Valores iniciais do formulário
const captado = {
  ...lead,
  cliente: { telefone: '', enderecoCapturado: 'Rua das Flores, 412, apto 71, Centro, 01310-100' },
}
const v = valoresIniciaisDoCadastro(captado)
assert.equal(v.nome, 'Marina (perfil)')
assert.match(v.enderecoSemCep, /Rua das Flores/)
assert.match(v.enderecoSemCep, /412/)
assert.equal(v.cep, '01310-100')
ok('endereço captado vira o campo Endereço (sem CEP) e o CEP vai para o próprio campo')

assert.equal(v.telefone, '(11) 98765-4321')
ok('sem telefone no cadastro, usa o número do contato do canal, no formato da tela')

assert.equal(valoresIniciaisDoCadastro({ ...lead, contato: 'maria@canal', cliente: {} }).telefone, '')
assert.equal(valoresIniciaisDoCadastro({ ...lead, contato: '123', cliente: {} }).telefone, '')
ok('contato que não parece telefone não vira telefone')

assert.equal(valoresIniciaisDoCadastro({ ...lead, cliente: { telefone: '21 91234-5678' } }).telefone, '(21) 91234-5678')
assert.equal(valoresIniciaisDoCadastro({ ...lead, contatoInformado: { telefone: '31999998888', nome: 'Marina Souza' }, cliente: {} }).telefone, '(31) 99999-8888')
assert.equal(valoresIniciaisDoCadastro({ ...lead, contatoInformado: { nome: 'Marina Souza' }, cliente: {} }).nome, 'Marina Souza')
ok('o telefone do cadastro vale antes do contato informado, que vale antes do número do canal; o contato informado traz o nome nos valores iniciais (o salvar manda o nome da conversa, que a dona edita em linha)')

// #3207 (revisão, achado 4): só o WhatsApp tem o número do canal no id; nos outros o id é externo.
const doInstagram = { ...lead, canal: 'Instagram', contato: '17841400000000000', cliente: {} }
assert.equal(valoresIniciaisDoCadastro(doInstagram).telefone, '')
assert.equal(valoresIniciaisDoCadastro({ ...doInstagram, contatoInformado: { telefone: '31999998888' } }).telefone, '(31) 99999-8888')
ok('fora do WhatsApp o Celular nasce vazio, ou com o telefone que o contato informou; nunca com o id externo')

// #3207 (revisão, achado 5): o banner só some quando não tem decisão de área a oferecer.
assert.equal(bannerDoEnderecoCapturado(false, null), true)
assert.equal(bannerDoEnderecoCapturado(true, null), false)
assert.equal(bannerDoEnderecoCapturado(true, 'dentro'), false)
assert.equal(bannerDoEnderecoCapturado(true, 'fora'), true)
assert.equal(bannerDoEnderecoCapturado(true, 'limite'), true)
ok('aberta pelo botão, o banner do endereço some, exceto quando oferece decisão de área (fora ou no limite)')

const vazio = valoresIniciaisDoCadastro({ conta: 'lead', nome: '', cliente: {} })
assert.deepEqual(vazio, { nome: '', telefone: '', enderecoSemCep: '', cep: '' })
ok('conversa sem nada captado devolve campos vazios, sem quebrar')

// 3. A tela: um botão principal, sem os caminhos duplicados
const cartao = readFileSync(new URL('../src/features/ficha-cliente/CartaoDoCliente.jsx', import.meta.url), 'utf8')
assert.doesNotMatch(cartao, /Conferir cadastros/)
assert.match(cartao, /ROTULO_CADASTRAR_CLIENTE/)
assert.match(cartao, /acoes\.vincular && <CorrecaoVinculoCliente/)
const painel = readFileSync(new URL('../src/features/ficha-cliente/PainelFicha.jsx', import.meta.url), 'utf8')
assert.doesNotMatch(painel, />Cadastrar cliente</)
assert.match(painel, /aoCadastrar=\{editavel \? abrirCadastroDoLead/)
const bloco = readFileSync(new URL('../src/features/ficha-cliente/BlocoCliente.jsx', import.meta.url), 'utf8')
assert.match(bloco, /valoresIniciaisDoCadastro\(conversa\)/)
assert.match(bloco, /cadastroForcado/)
// #3207 (revisão, achado 2): só o botão abre a Ficha no cadastro forçado; toda outra abertura zera.
assert.equal((painel.match(/setSecao\('cliente'\)/g) ?? []).length, 2, 'só abrirFicha e abrirCadastroDoLead abrem a seção do cliente')
ok('cartão sem "Conferir cadastros"; "Cadastrar cliente" só no cartão; o formulário é o mesmo da Ficha, aberto pelo botão; outras aberturas zeram o cadastro forçado')

console.log(`\n${passos} verificações ok`)
