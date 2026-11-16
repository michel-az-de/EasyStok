/* eslint-disable no-console */
// #2734: o cliente é o mesmo em Cadastros e na Ficha do Atendimento. Regras puras do relacionamento
// (nota, tag, telefone principal, atalho direto ao cadastro, prévia das notas) e a tradução única do dossiê.
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

const cadastro = await import('../src/dominio/fichaCliente.js')
const { previaDasNotas } = await import('../src/dominio/ficha.js')
const { moeda } = await import('../src/dominio/formato.js')
const { clienteDoDossie, relacionamentoDoDossie } = await import('../src/infra/api/traducaoCliente.js')

const ID = '3f2b8c1e-5a47-4d09-9b6e-1c2d3e4f5a6b'

// Nota: o teto do servidor (500) vira mensagem no campo, sem cortar o texto colado.
assert.equal(cadastro.erroDaNota('   '), 'Escreva a nota.')
assert.equal(cadastro.erroDaNota('a'.repeat(500)), null)
assert.equal(cadastro.erroDaNota('a'.repeat(501)), 'Use até 500 caracteres. Agora tem 501.')

// Tag: teto do servidor (40) e repetida pela mesma chave da Ficha (sem caixa, acento nem sublinhado).
assert.equal(cadastro.erroDaTag('', []), 'Escreva a tag.')
assert.equal(cadastro.erroDaTag('x'.repeat(41), []), 'Use até 40 caracteres. Agora tem 41.')
assert.equal(cadastro.erroDaTag('Alérgico', ['alergico']), 'Esta tag já existe.')
assert.equal(cadastro.erroDaTag('sem gluten', ['sem_gluten']), 'Esta tag já existe.')
assert.equal(cadastro.erroDaTag('vip', ['alergico']), null)

// Telefones: o principal vem primeiro e não se remove pela lista (ele muda no campo Telefone).
assert.deepEqual(cadastro.telefonesDaFicha([
  { id: 'T1', numero: '+5511955554444', whatsapp: true, principal: false },
  { id: 'T0', numero: '+5511999998888', whatsapp: true, principal: true },
]).map(t => [t.id, t.removivel]), [['T0', false], ['T1', true]])
assert.deepEqual(cadastro.telefonesDaFicha(undefined), [])

// Atalho da Ficha para o cadastro completo: terceiro segmento da rota, só com id de cadastro.
assert.equal(cadastro.hashDoCadastroDoCliente(ID), `#/m/cadastros/clientes/${ID}`)
assert.equal(cadastro.clienteDaHash(`#/m/cadastros/clientes/${ID}`), ID)
assert.equal(cadastro.clienteDaHash('#/m/cadastros/clientes'), null)
assert.equal(cadastro.clienteDaHash(`#/m/cadastros/produtos/${ID}`), null)
assert.equal(cadastro.clienteDaHash('#/m/cadastros/clientes/novo'), null)

// Números do relacionamento, com as palavras da Ficha ("valor total", "costuma pedir").
assert.deepEqual(cadastro.numerosDoRelacionamento({ pedidos: 7, totalGasto: 1234.5, favorito: 'Lasanha' }),
  [`Valor total ${moeda(1234.5)}`, 'Costuma pedir Lasanha'])
assert.deepEqual(cadastro.numerosDoRelacionamento({ pedidos: 0, totalGasto: 0, favorito: null }), [])
assert.deepEqual(cadastro.numerosDoRelacionamento(null), [])

// O dossiê traz as 20 notas mais recentes: com 20 na tela, o cadastro diz onde estão as outras.
assert.equal(cadastro.avisoDasNotas(20), 'Aqui aparecem as 20 notas mais recentes. As anteriores estão na Ficha do Atendimento.')
assert.equal(cadastro.avisoDasNotas(19), null)

// Ficha do Atendimento: a nota aparece sem clique. Recolhida, mostra as 2 mais recentes.
assert.deepEqual(previaDasNotas(['a', 'b', 'c'], false), { visiveis: ['a', 'b'], escondidas: 1 })
assert.deepEqual(previaDasNotas(['a', 'b', 'c'], true), { visiveis: ['a', 'b', 'c'], escondidas: 0 })
assert.deepEqual(previaDasNotas(['a'], false), { visiveis: ['a'], escondidas: 0 })
assert.deepEqual(previaDasNotas(undefined, false), { visiveis: [], escondidas: 0 })

// Uma tradução só do dossiê para as duas telas.
const dossie = {
  cliente: { id: ID, nome: 'Marina Teste', telefone: '+5511999998888', email: null, observacoes: 'Portão azul', motivoBloqueio: 'Pedido não pago', fotoUrl: null },
  enderecos: [],
  tags: [{ tag: 'alergico', origem: 'Manual', criadoEm: '2026-11-01T10:00:00' }],
  notas: [{ id: 'N1', texto: 'Alergia a camarao.', autor: 'Felipe', pedidoId: null, mensagemId: null, criadoEm: '2026-11-12T21:00:00' }],
  ultimosPedidos: [], itemFavorito: { nome: 'Lasanha', pedidos: 3 }, ultimaCompraEm: '2026-11-10T15:30:00',
  totalPedidos: 7, bloqueado: true, totalGasto: 1234.5,
  conversasRecentes: [{ id: 'C1', canal: 'ChatSite', situacao: 'Automatica', iniciadaEm: '2026-11-12T20:00:00', ultimaMensagemEm: '2026-11-12T21:05:00' }],
}
const lido = relacionamentoDoDossie(dossie)
assert.deepEqual(lido.tags, ['alergico'])
assert.deepEqual(lido.notas.map(n => [n.id, n.texto, n.autor, n.em]), [['N1', 'Alergia a camarao.', 'Felipe', '12/11, 18:00']])
assert.deepEqual(lido.bloqueio, { motivo: 'Pedido não pago', em: null, por: null, alcance: 'todos os canais' })
assert.deepEqual([lido.pedidos, lido.totalGasto, lido.favorito], [7, 1234.5, 'Lasanha'])
assert.deepEqual(lido.conversas, [{ id: 'C1', canal: 'Chat do site', estado: 'Aberto', em: '12/11, 18:05' }])
assert.equal(relacionamentoDoDossie({}), null)
assert.equal(relacionamentoDoDossie({ ...dossie, bloqueado: false, conversasRecentes: undefined }).bloqueio, null)
assert.deepEqual(relacionamentoDoDossie({ ...dossie, conversasRecentes: undefined }).conversas, [])

// C-03: a observação do cadastro chega à Ficha do Atendimento (antes o campo era lido e descartado).
assert.equal(clienteDoDossie(dossie).cliente.observacoes, 'Portão azul')
assert.equal(clienteDoDossie({ ...dossie, cliente: { ...dossie.cliente, observacoes: '   ' } }).cliente.observacoes, null)

// O editor de cliente também abre pela Buscar na Casa, fora do AtendimentoProvider: nada nele pode ler o contexto
// do Atendimento (useAtendimento, useAcoes e useCatalogo lançam sem o provider e derrubam o console).
const { readFileSync } = await import('node:fs')
for (const arquivo of ['EditorCadastro.jsx', 'FichaCliente.jsx', 'RelacionamentoCliente.jsx']) {
  const fonte = readFileSync(new URL(`../src/features/gestao/cadastros/${arquivo}`, import.meta.url), 'utf8')
  assert.equal(/aplicacao\/contextos'/.test(fonte), false, `${arquivo} não lê o contexto do Atendimento`)
}

// Por isso as restrições do catálogo chegam ao editor por propriedade, também pela Buscar na Casa.
const app = readFileSync(new URL('../src/app/App.jsx', import.meta.url), 'utf8')
assert.match(app, /renderizarCadastro=\{props => <EditorCadastro \{\.\.\.props\} restricoes=\{RESTRICOES\} \/>\}/, 'a Buscar na Casa entrega as restrições ao editor')

console.log('prova-2734-coerencia-cliente ok: nota, tag, telefone principal, atalho, números, prévia, tradução do dossiê e editor fora do Atendimento')
