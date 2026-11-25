// #3294: regras puras dos indicadores e dos sinais da linha de Pedidos. A consulta do resumo leva os filtros da lista
// sem situação, página e ordem; a comparação só existe com período fechado; a linha diz itens, ordem da compra e tempo.
import assert from 'node:assert/strict'
import { registerHooks } from 'node:module'
registerHooks({
  resolve(spec, contexto, proximo) {
    if (spec.startsWith('.') && !/\.[a-z]+$/i.test(spec)) {
      try { return proximo(spec + '.js', contexto) } catch { /* resolução normal */ }
    }
    return proximo(spec, contexto)
  },
})
const d = await import('../src/dominio/pedidosCadastro.js')
const comum = (t) => t?.replace(/\s/g, ' ')
const AGORA = Date.parse('2026-11-24T21:00:00Z') // 18h em Brasília
let testes = 0

// 1. Resumo: mesmos filtros da lista, sem situação, página, tamanho e ordem.
{
  const q = d.consultaDoResumo({ busca: ' Ana ', pagina: 3, situacao: 'cancelado', periodo: '7d', ordem: 'maior-valor', entrega: 'hoje' }, AGORA)
  assert.deepEqual(q, { search: 'Ana', desde: '2026-11-18', entregaDesde: '2026-11-24', entregaAte: '2026-11-24' }); testes++
  assert.deepEqual(d.consultaDoResumo({}, AGORA), {}, 'sem filtro, sem parâmetro'); testes++
}
// 2. Recorte legível.
assert.equal(d.rotuloDoRecorte({}), 'Em todo o período'); testes++
assert.equal(d.rotuloDoRecorte({ periodo: 'hoje', entrega: 'hoje', busca: 'Ana' }), 'Hoje · entrega hoje · busca "Ana"'); testes++
// 3. Comparação: só com período fechado e anterior; queda, alta, igual e base zero.
assert.equal(d.variacaoDoVendido({ vendido: 100, anterior: { vendido: 100 } }, 'tudo'), null, 'qualquer data não compara'); testes++
assert.equal(d.variacaoDoVendido({ vendido: 100, anterior: null }, '7d'), null); testes++
assert.deepEqual({ ...d.variacaoDoVendido({ vendido: 9842.5, anterior: { vendido: 8120 } }, '7d'), texto: comum(d.variacaoDoVendido({ vendido: 9842.5, anterior: { vendido: 8120 } }, '7d').texto) },
  { texto: '+21% sobre os 7 dias anteriores (R$ 8.120,00)', tom: 'sobe' }); testes++
assert.equal(d.variacaoDoVendido({ vendido: 80, anterior: { vendido: 100 } }, 'hoje').tom, 'desce'); testes++
assert.match(comum(d.variacaoDoVendido({ vendido: 80, anterior: { vendido: 100 } }, 'hoje').texto), /^−20% sobre ontem até esta hora/); testes++
assert.equal(comum(d.variacaoDoVendido({ vendido: 100, anterior: { vendido: 100 } }, '30d').texto), 'Igual aos 30 dias anteriores (R$ 100,00)'); testes++
assert.equal(d.variacaoDoVendido({ vendido: 50, anterior: { vendido: 0 } }, '7d').texto, 'Nada vendido nos 7 dias anteriores'); testes++
// 4. Clientes que voltaram.
assert.deepEqual(d.clientesQueVoltaram({ primeiraCompra: 31, recompra: 87 }), { primeira: 31, voltaram: 87, percentual: 74 }); testes++
assert.equal(d.variacaoDoVendido({ vendido: 0, anterior: { vendido: 0 } }, 'hoje').texto, 'Sem vendas também ontem até esta hora'); testes++
assert.equal(d.clientesQueVoltaram({ primeiraCompra: 0, recompra: 0 }), null); testes++
// 5. Itens da linha: ordem da API, "+N" pelo total, unidade pesada.
assert.deepEqual(d.itensDaLinha({ itensCount: 5, itensResumo: [{ nome: 'Bolo', quantidade: 2, unidade: 'un' }, { nome: 'Brigadeiro', quantidade: 1.5, unidade: 'kg' }, { nome: 'Torta', quantidade: 1 }] }),
  { texto: '2× Bolo, 1,5 kg Brigadeiro, 1× Torta', resto: '+2' }); testes++
assert.equal(d.itensDaLinha({ itensCount: 0, itensResumo: [] }), null); testes++
assert.equal(d.itensDaLinha({ itensCount: 2, itensSemFrete: 1, itensResumo: [{ nome: 'Bolo', quantidade: 1 }] }).resto, null, 'frete não vira +1'); testes++
assert.equal(d.itensDaLinha({}), null, 'API antiga sem itensResumo'); testes++
// 6. Ordem da compra.
assert.deepEqual(d.compraDoClienteNaLinha({ compraDoCliente: 1 }), { texto: '1ª compra', nova: true }); testes++
assert.deepEqual(d.compraDoClienteNaLinha({ compraDoCliente: 12 }), { texto: '12ª compra', nova: false }); testes++
assert.equal(d.compraDoClienteNaLinha({ compraDoCliente: null }), null); testes++
// 7. Tempo na situação: aberto conta, fechado não.
assert.equal(d.tempoNaSituacao({ status: 'preparando', situacaoDesde: '2026-11-24T18:00:00Z' }, AGORA), 'há 3 h'); testes++
assert.equal(d.tempoNaSituacao({ status: 'entregue', situacaoDesde: '2026-11-24T18:00:00Z' }, AGORA), null); testes++
assert.equal(d.tempoNaSituacao({ status: 'preparando' }, AGORA), null); testes++

console.log(`prova-3294-dominio: ${testes} verificações OK`)
