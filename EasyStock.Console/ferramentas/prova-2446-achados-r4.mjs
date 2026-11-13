// #2446: achados da revisão da R4 que ficaram abertos no master depois da #2400.
//   A1: troca de cliente feita por outro operador não deixa bloqueio, contato informado nem captura do anterior.
//   M4: editar a conta bancária não troca a chave da operação a cada tecla (duplicaria a conta após timeout).
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(nome, contexto, proximo) {
    if (nome.startsWith('.') && !/\.[a-z]+$/i.test(nome)) { try { return proximo(nome + '.js', contexto) } catch { /* resolução normal */ } }
    return proximo(nome, contexto)
  },
  load(url, contexto, proximo) {
    if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true; export const API_BASE = '';" }
    return proximo(url, contexto)
  },
})
const { mesclarDoServidor } = await import('../src/aplicacao/reducer/mesclaDoServidor.js')
const { conversaDaApi } = await import('../src/infra/api/traducaoConversas.js')
const { abrirEdicaoCadastro, mudarDadosCadastro } = await import('../src/dominio/cadastros.js')

const resumo = { id: 'c1', contatoNome: 'Contato', contatoIdExterno: '5511999999999', canal: 'WhatsApp', situacao: 'Assumida' }
const naTela = {
  ...conversaDaApi({ ...resumo, clienteId: 'cliente-x', vinculoRevisao: 1 }, [], { id: 'operador-a' }),
  bloqueio: { motivo: 'Calote do cliente X', em: '2026-11-12T10:00:00Z', por: 'Thatiane' },
  contatoInformado: { nome: 'Cliente X', telefone: '5511999999999', email: 'x@exemplo.test' },
  captura: { aguardando: 'endereco', cadastroEm: null, pedidoEm: null },
}

// A1: o operador B trocou o vínculo de X para Y; a polling do operador A traz Y.
const trocada = mesclarDoServidor(naTela, conversaDaApi({ ...resumo, clienteId: 'cliente-y', vinculoRevisao: 2 }, [], { id: 'operador-a' }))
assert.equal(trocada.clienteId, 'cliente-y')
assert.equal(trocada.bloqueio ?? null, null, 'A1: o bloqueio de X não pode aparecer em Y')
assert.equal(trocada.contatoInformado ?? null, null, 'A1: o contato informado de X não pode ficar em Y')
assert.equal(trocada.captura ?? null, null, 'A1: a captura em curso de X não pode seguir em Y')

// A1: retirada do vínculo (X para nenhum) também solta o que era de X.
const retirada = mesclarDoServidor(naTela, conversaDaApi({ ...resumo, clienteId: null, vinculoRevisao: 2, vinculoEmRevisao: true }, [], { id: 'operador-a' }))
assert.equal(retirada.bloqueio ?? null, null, 'A1: retirar o vínculo solta o bloqueio de X')

// A1: a mesma pessoa relida não perde o bloqueio que o dossiê trouxe.
const mesma = mesclarDoServidor(naTela, conversaDaApi({ ...resumo, clienteId: 'cliente-x', vinculoRevisao: 1 }, [], { id: 'operador-a' }))
assert.deepEqual(mesma.bloqueio, naTela.bloqueio, 'A1: relida com o mesmo cliente mantém o bloqueio')
assert.deepEqual(mesma.contatoInformado, naTela.contatoInformado)

// A1: lead sem cadastro que ganhou cadastro mantém o contato que informou no chat.
const lead = { ...naTela, clienteId: null, conta: 'lead', vinculoRevisao: 0, bloqueio: null }
const vinculado = mesclarDoServidor(lead, conversaDaApi({ ...resumo, clienteId: 'cliente-novo', vinculoRevisao: 1 }, [], { id: 'operador-a' }))
assert.deepEqual(vinculado.contatoInformado, naTela.contatoInformado, 'A1: o contato do lead não some quando ele ganha cadastro')

// M4: a chave da operação nasce ao abrir o editor e não muda enquanto a dona digita.
const aberto = { nome: '', tipo: 'corrente', ativo: true, requestId: 'abertura-1' }
const digitado = mudarDadosCadastro('bancos', mudarDadosCadastro('bancos', aberto, 'nome', 'Itaú'), 'conta', '123')
assert.equal(digitado.requestId, 'abertura-1', 'M4: a mesma abertura do editor repete a mesma operação')
assert.equal(digitado.nome, 'Itaú'); assert.equal(digitado.conta, '123')
const desfeito = mudarDadosCadastro('bancos', mudarDadosCadastro('bancos', aberto, 'nome', 'X'), 'nome', '')
assert.deepEqual(desfeito, aberto, 'M4: digitar e apagar volta ao inicial, sem "Descartar?" falso')
// M4: a conta existente ganha a operação ao abrir; sem ela o PATCH seria recusado (requestId obrigatório).
const contaDaLista = { id: 'conta-1', nome: 'Itaú', revisao: 3 }
const editando = abrirEdicaoCadastro('bancos', contaDaLista, () => 'abertura-2')
assert.equal(editando.requestId, 'abertura-2', 'M4: editar conta existente leva requestId desde a abertura')
assert.equal(mudarDadosCadastro('bancos', editando, 'nome', 'Itaú PJ').requestId, 'abertura-2')
assert.equal(abrirEdicaoCadastro('clientes', contaDaLista), contaDaLista, 'só conta bancária ganha requestId na abertura')
assert.equal(mudarDadosCadastro('produtos', { categoriaId: 'a', subcategoriaId: 's' }, 'categoriaId', 'b').subcategoriaId, null, 'trocar a categoria limpa a subcategoria')
assert.equal(mudarDadosCadastro('produtos', { categoriaId: 'a', subcategoriaId: 's' }, 'categoriaId', 'a').subcategoriaId, 's')

console.log('ok prova-2446-achados-r4')
