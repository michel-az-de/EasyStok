// #3254 (S2, etapa A): domínio da relação do cliente no grid de Cadastros e tradução da lista. Só dados fictícios.
//   node --import ./ferramentas/ambienteDoVite.mjs ferramentas/rodar-provas.mjs 3254
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'

// A tradução importa sem extensão (padrão do Vite); no Node puro o hook completa o .js.
registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

const { estagioDoCliente, posicaoDoEstagio, percentualDoGasto, tagsVisiveis, diasDesde } = await import('../src/dominio/relacionamento.js')
const { relacaoDoCliente, gastoDaCarteiraDoMeta } = await import('../src/infra/api/traducaoCliente.js')

assert.equal(estagioDoCliente('sem_pedido'), 'Sem pedido')
assert.equal(estagioDoCliente('primeira_compra'), 'Primeira compra')
assert.equal(estagioDoCliente('recorrente'), 'Recorrente')
assert.equal(estagioDoCliente('fiel'), 'Fiel')
for (const x of [undefined, null, '', 'vip']) assert.equal(estagioDoCliente(x), null, `código ${x} fica sem estágio`)

assert.deepEqual(['sem_pedido', 'primeira_compra', 'recorrente', 'fiel'].map(posicaoDoEstagio), [0, 1, 2, 3])
assert.equal(posicaoDoEstagio('vip'), null)
assert.equal(posicaoDoEstagio(undefined), null)

assert.equal(percentualDoGasto(25, 100), 25)
assert.equal(percentualDoGasto(150, 100), 100, 'limitado a 100')
assert.equal(percentualDoGasto(-5, 100), 0, 'limitado a 0')
for (const total of [0, null, undefined, 'x', NaN, -10]) assert.equal(percentualDoGasto(10, total), 0, `total ${total}`)
assert.equal(percentualDoGasto(undefined, 100), 0)

assert.deepEqual(tagsVisiveis(['vip', 'recompra_convidada', 'bairro']), ['vip', 'bairro'])
assert.deepEqual(tagsVisiveis(undefined), [])

const hoje = Date.parse('2026-11-23T15:00:00Z')
assert.equal(diasDesde('2026-11-23T10:00:00Z', hoje), 0)
assert.equal(diasDesde('2026-11-20T10:00:00Z', hoje), 3)
assert.equal(diasDesde(null, hoje), null)
assert.equal(diasDesde('lixo', hoje), null)

// Tradução: a API da S1 pode não estar em produção; campo ausente vira null, nunca erro.
const vazio = relacaoDoCliente({ id: 'c1', nome: 'Ana Teste' })
assert.deepEqual(vazio, { gastoTotal: null, compras: null, ticketMedio: null, criadoEm: null, primeiraCompraEm: null, ultimaCompraEm: null,
  ocorrenciasAbertas: null, ocorrenciasTotal: null, notasTotal: null, ultimaNota: null, tags: [], estagio: null, emRisco: false })
assert.deepEqual(relacaoDoCliente(null), vazio)
const cheio = relacaoDoCliente({ gastoTotal: 120.5, compras: 3, ticketMedio: 40.17, primeiraCompraEm: '2026-01-02T00:00:00Z', ultimaCompraEm: '2026-11-01T00:00:00Z',
  ocorrenciasAbertas: 1, ocorrenciasTotal: 2, notasTotal: 4, ultimaNota: 'Ligar', tags: ['vip', 'bairro', 'x', 'y'], estagio: 'recorrente', emRisco: true })
assert.equal(relacaoDoCliente({ criadoEm: '2026-01-02T00:00:00Z' }).criadoEm, '2026-01-02T00:00:00Z', 'criadoEm passa para a linha')
assert.equal(cheio.gastoTotal, 120.5)
assert.equal(cheio.estagio, 'recorrente')
assert.equal(cheio.emRisco, true)
assert.deepEqual(cheio.tags, ['vip', 'bairro', 'x', 'y'])
assert.equal(gastoDaCarteiraDoMeta({ gastoTotalDaCarteira: 900 }), 900)
for (const m of [undefined, null, {}, { gastoTotalDaCarteira: 'a' }]) assert.equal(gastoDaCarteiraDoMeta(m), null)
console.log('prova-3254-relacionamento: ok')
