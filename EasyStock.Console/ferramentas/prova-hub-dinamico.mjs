import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
registerHooks({ resolve(spec, contexto, proximo) {
  if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
    try { return proximo(spec + '.js', contexto) } catch { /* resolução normal */ }
  }
  return proximo(spec, contexto)
}, load(url, contexto, proximo) {
  // #2078: o leitor do Hall traduz o dia com o mesmo `caixaDiaDaApi` da tela do Caixa.
  if (url.endsWith('/infra/fonteDados.js')) return { format: 'module', shortCircuit: true, source: "export const FONTE_API = true\nexport const API_BASE = ''\n" }
  return proximo(url, contexto)
} })
const { indicadoresDoHall, pendenciaDoModulo } = await import('../src/dominio/pulsoDoHall.js')
const { normalizarPreferenciasHall, aplicarFocoDoHall, ordenarPaineisDoHall } = await import('../src/dominio/preferenciasHall.js')
const ids = ['atendimento', 'cozinha', 'entregas', 'financeiro']
const antes = { ordem: ['entregas', 'atendimento'], favoritos: ['cozinha'] }
assert.equal(normalizarPreferenciasHall(antes).ordem[0], 'entregas')
assert.deepEqual(antes, { ordem: ['entregas', 'atendimento'], favoritos: ['cozinha'] })
assert.equal(aplicarFocoDoHall(antes, 'atendimento').ordem[0], 'atendimento')
assert.equal(aplicarFocoDoHall(antes, 'producao').ordem[0], 'producao')
assert.deepEqual(aplicarFocoDoHall(antes, 'gestao').favoritos, ['cozinha'])
assert.deepEqual(normalizarPreferenciasHall({ paineis: ['privado', 'cozinha', 'cozinha'], movimentos: false }).paineis, ['cozinha'])
assert.deepEqual(normalizarPreferenciasHall({ paineis: [] }).paineis, [])
assert.equal(normalizarPreferenciasHall({ foco: 'privado' }).foco, 'atendimento')
assert.equal('ritmo' in normalizarPreferenciasHall({ ritmo: true }), false)
for (const [foco, esperado] of [
  ['atendimento', ['atendimento', 'entregas', 'cozinha', 'financeiro']],
  ['gestao', ['financeiro', 'atendimento', 'entregas', 'cozinha']],
  ['producao', ['cozinha', 'entregas', 'atendimento', 'financeiro']],
]) {
  assert.deepEqual(ordenarPaineisDoHall({ foco }, ids), esperado)
  assert.deepEqual(ordenarPaineisDoHall({ foco, paineis: ['cozinha', 'financeiro'] }, ['cozinha', 'atendimento']), ['cozinha'])
  assert.deepEqual(ordenarPaineisDoHall({ foco, paineis: [] }, ids), [])
}
const carregando = indicadoresDoHall({}, ids)
assert.ok(carregando.every((item) => item.valor === null && !item.atual))
const fontes = {
  atendimento: { estado: 'ok', dados: { itens: [
    { situacao: 'Automatica', aguardaResposta: true },
    { situacao: 'Assumida', aguardaResposta: true, assumidaPorUsuarioId: 'outro' },
    { situacao: 'Assumida', aguardaResposta: false },
    { situacao: 'Assumida', aguardaResposta: false, assumidaPorUsuarioId: 'eu' },
    { situacao: 'Encerrada', aguardaResposta: true },
  ], parcial: false } },
  pedidos: { estado: 'ok', dados: { itens: ['aguardando', 'preparando', 'pronto', 'saiu_para_entrega', 'entregue'].map((status) => ({ status })), parcial: false } },
  // #2078: o Hall lê o dia já traduzido (`caixaDiaDaApi`); o número é a gaveta, como no Caixa do dia.
  financeiro: { estado: 'ok', dados: { saldoEsperado: 300.03, saldoInicial: 300.03, aberto: true } },
}
assert.deepEqual(indicadoresDoHall(fontes, ids).map((item) => item.valor), [2, 2, 1, 300.03])
// #2055: o pedido fora da área pago e aprovado pela dona (aprovado_baba) está em Na fila na Cozinha; o Hall conta igual.
const comAprovado = { pedidos: { estado: 'ok', dados: { itens: [{ status: 'aprovado_baba' }], parcial: false } } }
assert.equal(indicadoresDoHall(comAprovado, ['cozinha'])[0].valor, 1)
assert.deepEqual(pendenciaDoModulo('cozinha', comAprovado), { texto: '1 pedido no preparo', atencao: true })
// #1810: no hall enxuto só o caixa de outro dia vira alerta escrito; o resto fica no título.
assert.deepEqual(indicadoresDoHall(fontes, ids).map((item) => item.alerta), [null, null, null, null])
assert.equal(indicadoresDoHall({ financeiro: { estado: 'ok', dados: { saldoEsperado: 10, esquecidoAberto: true } } }, ['financeiro'])[0].alerta,
  'Caixa de outro dia ainda aberto. Confira o fechamento.')
// #2078 (B2/D2): a API manda `aberto: true` também no dia que já fechou. Fechado vem primeiro.
const fechadoHoje = { financeiro: { estado: 'ok', dados: { saldoEsperado: 646, saldoInicial: 130, aberto: true, fechado: true, movimentos: [], linhasExtras: [] } } }
assert.deepEqual(pendenciaDoModulo('financeiro', fechadoHoje), { texto: 'Caixa fechado hoje', atencao: false })
assert.equal(indicadoresDoHall(fechadoHoje, ['financeiro'])[0].detalhe, 'Caixa fechado hoje. Dinheiro que ficou na gaveta.')
// #2078 (B7/E3/D7): o número do cartão é o mesmo que a tela do Caixa destaca, com o mesmo rótulo: o
// dinheiro da gaveta. O total com Pix e cartão (`saldoEsperado`) não é o que ela confere na gaveta.
const diaMisto = { financeiro: { estado: 'ok', dados: {
  saldoEsperado: 719.7, saldoInicial: 130, aberto: true, fechado: false,
  movimentos: [
    { tipo: 'entrada', valor: 50, meio: 'dinheiro' }, { tipo: 'entrada', valor: 80, meio: 'pix' },
    { tipo: 'saida', valor: 23.9, meio: 'dinheiro' }, { tipo: 'entrada', valor: 9, meio: 'dinheiro', estornadoEm: '2026-10-26T20:00:00Z' },
  ],
  linhasExtras: [{ valor: 106, meio: 'dinheiro' }, { valor: 299.1, meio: 'pix' }, { valor: 38, meio: null }],
} } }
const [caixaDoHall] = indicadoresDoHall(diaMisto, ['financeiro'])
assert.equal(caixaDoHall.valor, 262.1, '130 + 50 - 23,90 + 106: só dinheiro, sem o estorno')
assert.equal(caixaDoHall.rotulo, 'Na gaveta (dinheiro)')
// Sem abrir o caixa a tela do Caixa não fala em gaveta: diz "Em dinheiro". O Hall diz o mesmo.
const semAbertura = { financeiro: { estado: 'ok', dados: { saldoEsperado: 70, saldoInicial: 0, aberto: false, fechado: false, movimentos: [], linhasExtras: [{ valor: 30, meio: 'dinheiro' }, { valor: 40, meio: 'pix' }] } } }
assert.deepEqual([indicadoresDoHall(semAbertura, ['financeiro'])[0].valor, indicadoresDoHall(semAbertura, ['financeiro'])[0].rotulo], [30, 'Em dinheiro'])
assert.equal(indicadoresDoHall({}, ['financeiro'])[0].rotulo, 'Na gaveta (dinheiro)', 'carregando: o rótulo já é o da tela do Caixa')
fontes.atendimento.estado = 'carregando'
assert.equal(indicadoresDoHall(fontes, ids)[0].valor, 2)
assert.equal(indicadoresDoHall(fontes, ids)[0].atual, false)
fontes.atendimento.estado = 'erro'
assert.equal(indicadoresDoHall(fontes, ids)[0].valor, 2)
assert.equal(indicadoresDoHall(fontes, ids)[0].atual, false)
fontes.atendimento.dados.itens = []
assert.equal(indicadoresDoHall(fontes, ids)[0].valor, 0)
fontes.atendimento.estado = 'ok'; fontes.atendimento.dados.parcial = true
assert.equal(indicadoresDoHall(fontes, ids)[0].parcial, true)
fontes.financeiro.dados = {}
assert.equal(indicadoresDoHall(fontes, ids)[3].valor, null)
// #2078: o leitor do Hall devolve o dia traduzido, com `meio` no lugar do `metodo` da API; sem isso a
// gaveta do Hall não sabia o que era dinheiro.
const { caixaDiaDaApi } = await import('../src/infra/api/caixaApi.js')
assert.match(readFileSync(new URL('../src/infra/api/hubApi.js', import.meta.url), 'utf8'), /return caixaDiaDaApi\(dados\)/)
const daApi = caixaDiaDaApi({ data: '2026-10-26', saldoEsperado: 236, saldoInicial: 130, aberto: true, fechado: false, aberturaPendenteCrossDay: false,
  movimentos: [{ id: 'm', tipo: 'saida', valor: 20, metodo: 'dinheiro', dataMovimento: '2026-10-26T20:00:00Z' }],
  linhasExtras: [{ hora: '2026-10-26T20:02:00Z', tipo: 'pagamento', valor: 106, metodo: 'dinheiro', origem: 'Pedido' }, { hora: '2026-10-26T20:03:00Z', tipo: 'pagamento', valor: 20, metodo: 'pix', origem: 'Pedido' }] })
assert.equal(indicadoresDoHall({ financeiro: { estado: 'ok', dados: daApi } }, ['financeiro'])[0].valor, 216)
assert.equal(pendenciaDoModulo('financeiro', { financeiro: { estado: 'ok', dados: caixaDiaDaApi({ saldoEsperado: 0, aberto: true, aberturaPendenteCrossDay: true }) } }).texto, 'Caixa de outro dia aberto')
console.log('Hub: migração de preferências, modelos, números, recortes, falha e dados parciais conferidos.')
