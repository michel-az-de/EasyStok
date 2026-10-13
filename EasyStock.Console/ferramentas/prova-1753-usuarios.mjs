/* eslint-disable no-console */
// Prova da #1753: as regras da tela de Usuários, do convite e do histórico de acesso.
// A homologação de 13/10 achou a dona se desativando e ficando de fora, e uma auditoria que só
// falava em identificadores. Aqui fica o que a tela promete, sem navegador:
//
//   node ferramentas/prova-1753-usuarios.mjs

import assert from 'node:assert/strict'
import {
  acoesDoUsuario, agruparPorDia, condensarAuditoria, ehRotaDeConvite, erroDoConvite, instanteDaApi,
  linhaDeAuditoria, ordenarPerfis, regrasDaSenha, senhaValida, situacaoDoUsuario, tokenDoConvite,
} from '../src/dominio/usuarios.js'

let passaram = 0
const conferir = (nome, teste) => { teste(); passaram += 1; console.log('ok  ' + nome) }

const dona = { usuarioId: 'dona', ativo: true, convite: { estado: 'nenhum' } }
const convidada = { usuarioId: 'ana', ativo: true, convite: { estado: 'pendente' } }
const desativada = { usuarioId: 'bia', ativo: false, convite: { estado: 'pendente' } }

conferir('a dona não recebe botão para mexer no próprio acesso', () => {
  assert.deepEqual(acoesDoUsuario(dona, 'dona'), { trocarPerfil: false, reenviarConvite: false, desativar: false, reativar: false })
})

conferir('quem ainda não aceitou pode receber o convite de novo; quem aceitou, não', () => {
  assert.equal(acoesDoUsuario(convidada, 'dona').reenviarConvite, true)
  assert.equal(acoesDoUsuario({ ...convidada, convite: { estado: 'aceito' } }, 'dona').reenviarConvite, false)
})

conferir('desativada só tem o caminho de volta, e aparece como desativada mesmo com convite aberto', () => {
  assert.deepEqual(acoesDoUsuario(desativada, 'dona'), { trocarPerfil: false, reenviarConvite: false, desativar: false, reativar: true })
  assert.equal(situacaoDoUsuario(desativada).rotulo, 'Desativado')
  assert.equal(situacaoDoUsuario(convidada).rotulo, 'Convite enviado')
  assert.equal(situacaoDoUsuario(dona).rotulo, 'Ativo')
})

conferir('os perfis da casa vêm antes do legado, na ordem Dona, Atendimento, Cozinha', () => {
  const nomes = ordenarPerfis(['Operador', 'Cozinha', 'Admin', 'Dona', 'Atendimento'].map((nome) => ({ nome }))).map((p) => p.nome)
  assert.deepEqual(nomes, ['Dona', 'Atendimento', 'Cozinha', 'Admin', 'Operador'])
})

conferir('convite sem nome, com e-mail torto ou sem perfil não sai', () => {
  assert.match(erroDoConvite({ nome: ' ', email: 'a@b.co', perfilId: 'p' }), /nome/)
  assert.match(erroDoConvite({ nome: 'Ana', email: 'ana@casa', perfilId: 'p' }), /e-mail/)
  assert.match(erroDoConvite({ nome: 'Ana', email: 'ana@casa.com', perfilId: '' }), /perfil/)
  assert.equal(erroDoConvite({ nome: 'Ana', email: ' ana@casa.com ', perfilId: 'p' }), null)
})

conferir('a senha segue a mesma política da API', () => {
  assert.equal(senhaValida('casadababa'), false)
  assert.deepEqual(regrasDaSenha('casadababa').filter((r) => !r.ok).map((r) => r.id), ['maiuscula', 'numero', 'especial'])
  assert.equal(senhaValida('Casa-da-Baba1'), true)
})

conferir('o token do convite vem do fragmento e a rota é reconhecida sem ele', () => {
  assert.equal(tokenDoConvite('#/convite?t=abc.DEF-1'), 'abc.DEF-1')
  assert.equal(tokenDoConvite('#/convite'), null)
  assert.equal(tokenDoConvite('#/m/configuracoes/usuarios'), null)
  assert.equal(ehRotaDeConvite('#/convite'), true)
  assert.equal(ehRotaDeConvite('#/convites'), false)
})

conferir('data da API sem fuso é lida como UTC', () => {
  assert.equal(instanteDaApi('2026-10-13T20:38:39.123'), Date.UTC(2026, 9, 13, 20, 38, 39, 123))
  assert.equal(instanteDaApi('2026-10-13T20:38:39Z'), Date.UTC(2026, 9, 13, 20, 38, 39))
  assert.equal(instanteDaApi(null), null)
})

const linha = (acao, quando, usuarioId = 'ana', detalhes = null) =>
  linhaDeAuditoria({ id: acao + quando, acao, dataHora: quando, usuarioId, usuarioNome: usuarioId, detalhes, sucesso: true })

conferir('a linha conta o fato inteiro e o jargão antigo some', () => {
  assert.equal(linha('perfil-trocado', 'x', 'ana', 'Perfil trocado de Cozinha para Atendimento por Baba').oQue,
    'Perfil trocado de Cozinha para Atendimento por Baba')
  assert.equal(linha('convite-reenviado', 'x', 'ana', 'Convite reenviado por Baba (Admin); WhatsApp: nao').oQue, 'Convite reenviado por Baba')
  assert.equal(linha('convite-reenviado', 'x', 'ana', 'Convite reenviado por Baba (Admin); WhatsApp: sim').oQue,
    'Convite reenviado por Baba, também pelo WhatsApp')
  assert.equal(linha('login', 'x', 'ana', 'Login realizado com sucesso').oQue, 'Entrou')
  assert.equal(linha('login_novo_dispositivo', 'x', 'ana', 'Login detectado em novo dispositivo. 1 sessão(ões) anterior(es) encerrada(s).').oQue,
    'Entrou em outro aparelho')
  assert.equal(linha('acao-nova_da-api', 'x').oQue, 'acao nova da api')
})

conferir('entrada e aviso do mesmo login viram uma linha; entradas seguidas viram contagem', () => {
  const linhas = [
    linha('login_novo_dispositivo', '2026-10-13T20:50:01'), linha('login', '2026-10-13T20:50:00'),
    linha('login', '2026-10-13T20:48:00'), linha('login', '2026-10-13T20:40:00'),
    linha('perfil-trocado', '2026-10-13T20:30:00', 'ana', 'Perfil trocado de Cozinha para Atendimento por Baba'),
    linha('login', '2026-10-13T20:20:00'), linha('login', '2026-10-13T20:10:00', 'bia'),
  ]
  const condensadas = condensarAuditoria(linhas)
  assert.deepEqual(condensadas.map((l) => l.oQue), [
    'Entrou 3 vezes', 'Perfil trocado de Cozinha para Atendimento por Baba', 'Entrou', 'Entrou',
  ])
  assert.equal(condensadas[0].primeira, '2026-10-13T20:40:00')
  assert.equal(linhas.length, 7, 'a lista original não é alterada')
  assert.equal(linhas[0].vezes, undefined)
})

conferir('entradas de dias diferentes não se juntam', () => {
  const linhas = [linha('login', '2026-10-13T03:10:00'), linha('login', '2026-10-13T02:50:00')]
  assert.equal(condensarAuditoria(linhas, () => false).length, 2)
})

conferir('o histórico agrupa por dia na ordem em que chegou', () => {
  const grupos = agruparPorDia([{ quando: 'a1' }, { quando: 'a2' }, { quando: 'b1' }], (q) => q[0])
  assert.deepEqual(grupos.map((g) => [g.dia, g.linhas.length]), [['a', 2], ['b', 1]])
})

console.log(`\n${passaram} verificações passaram.`)
