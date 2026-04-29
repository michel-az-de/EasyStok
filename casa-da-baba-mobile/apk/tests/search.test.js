// Testes unitarios — F3 Busca Global (_normalizeSearch, buildSearchGroups)
'use strict';

module.exports = function ({ test, runInSandbox, assert }) {
  test('search: _normalizeSearch remove acentos e lowercase', () => {
    assert.strictEqual(runInSandbox(`_normalizeSearch('João')`), 'joao');
    assert.strictEqual(runInSandbox(`_normalizeSearch('CAFÉ')`), 'cafe');
    assert.strictEqual(runInSandbox(`_normalizeSearch('açaí')`), 'acai');
    assert.strictEqual(runInSandbox(`_normalizeSearch('  Pão  ')`), '  pao  ');
  });

  test('search: _normalizeSearch tolera null/undefined', () => {
    assert.strictEqual(runInSandbox(`_normalizeSearch(null)`), '');
    assert.strictEqual(runInSandbox(`_normalizeSearch(undefined)`), '');
    assert.strictEqual(runInSandbox(`_normalizeSearch(123)`), '123');
  });

  test('search: query curta (<2 chars) retorna so atalhos', () => {
    const r = JSON.parse(runInSandbox(`(function(){
      orders = [{ id: 'o1', clientSnapshot: { name: 'Joao' }, items: [], total: 0, status: 'pronto' }];
      clients = []; products = []; batches = [];
      var g = buildSearchGroups('a');
      return JSON.stringify({ pedidos: g.pedidos.length, acoes: g.acoes.length });
    })()`));
    assert.strictEqual(r.pedidos, 0, 'sem busca real com 1 char');
    assert.ok(r.acoes > 0, 'atalhos sempre presentes em estado vazio');
  });

  test('search: pedido por nome cliente (acento-insensitive)', () => {
    const r = JSON.parse(runInSandbox(`(function(){
      orders = [
        { id: 'oabc1234', clientSnapshot: { name: 'João' }, items: [], total: 50, status: 'pronto' },
        { id: 'oabc5678', clientSnapshot: { name: 'Maria' }, items: [], total: 30, status: 'pronto' }
      ];
      clients = []; products = []; batches = [];
      return JSON.stringify(buildSearchGroups('joao').pedidos.map(o => o.id));
    })()`));
    assert.deepStrictEqual(r, ['oabc1234']);
  });

  test('search: produto por substring', () => {
    const r = JSON.parse(runInSandbox(`(function(){
      orders = []; clients = []; batches = [];
      products = [
        { id: 'p1', name: 'Pão de queijo', archived: false, stock: 5 },
        { id: 'p2', name: 'Coxinha', archived: false, stock: 0 },
        { id: 'p3', name: 'Pão integral', archived: false, stock: 1 }
      ];
      return JSON.stringify(buildSearchGroups('pao').produtos.map(p => p.id));
    })()`));
    assert.deepStrictEqual(r.sort(), ['p1', 'p3']);
  });

  test('search: produto archived nao aparece', () => {
    const r = JSON.parse(runInSandbox(`(function(){
      orders = []; clients = []; batches = [];
      products = [
        { id: 'p1', name: 'Pao', archived: false, stock: 5 },
        { id: 'p2', name: 'Pao morto', archived: true, stock: 0 }
      ];
      return JSON.stringify(buildSearchGroups('pao').produtos.map(p => p.id));
    })()`));
    assert.deepStrictEqual(r, ['p1']);
  });

  test('search: cliente por phone/doc', () => {
    const r = JSON.parse(runInSandbox(`(function(){
      orders = []; products = []; batches = [];
      clients = [
        { id: 'c1', name: 'Maria', phone: '11999887766' },
        { id: 'c2', name: 'Joao', doc: '123.456.789-00' }
      ];
      return JSON.stringify({
        byPhone: buildSearchGroups('99988').clientes.map(c => c.id),
        byDoc: buildSearchGroups('123').clientes.map(c => c.id)
      });
    })()`));
    assert.deepStrictEqual(r.byPhone, ['c1']);
    assert.deepStrictEqual(r.byDoc, ['c2']);
  });

  test('search: lote por codigo lote ou item', () => {
    const r = JSON.parse(runInSandbox(`(function(){
      orders = []; clients = []; products = [];
      batches = [
        { id: 'b1', lote: 'L-2026-001', deleted: false, items: [{ name: 'Lasanha' }] },
        { id: 'b2', lote: 'L-2026-002', deleted: false, items: [{ name: 'Coxinha' }] },
        { id: 'b3', lote: 'L-2026-003', deleted: true,  items: [{ name: 'X' }] }
      ];
      return JSON.stringify({
        byLote: buildSearchGroups('001').lotes.map(b => b.id),
        byItem: buildSearchGroups('coxinha').lotes.map(b => b.id),
        deletedExcluded: buildSearchGroups('L-2026').lotes.map(b => b.id).sort()
      });
    })()`));
    assert.deepStrictEqual(r.byLote, ['b1']);
    assert.deepStrictEqual(r.byItem, ['b2']);
    assert.deepStrictEqual(r.deletedExcluded, ['b1', 'b2'], 'lote deleted nao aparece');
  });

  test('search: limite max 5 por grupo', () => {
    const r = JSON.parse(runInSandbox(`(function(){
      orders = []; clients = []; batches = [];
      products = Array.from({length:10}, (_,i) => ({ id:'p'+i, name:'pao'+i, archived:false, stock:1 }));
      return buildSearchGroups('pao').produtos.length;
    })()`));
    assert.strictEqual(r, 5);
  });

  test('search: atalhos sempre presentes (estado vazio)', () => {
    const r = JSON.parse(runInSandbox(`(function(){
      orders=[]; clients=[]; products=[]; batches=[];
      return JSON.stringify(buildSearchGroups('').acoes.map(a => a.key));
    })()`));
    assert.ok(r.length >= 5, 'pelo menos 5 atalhos');
    assert.ok(r.includes('novo-pedido'), 'inclui novo-pedido');
    assert.ok(r.includes('producao'), 'inclui producao');
  });

  test('search: atalhos filtrados por keyword', () => {
    const r = JSON.parse(runInSandbox(`(function(){
      orders=[]; clients=[]; products=[]; batches=[];
      return JSON.stringify(buildSearchGroups('caixa').acoes.map(a => a.key));
    })()`));
    assert.ok(r.includes('caixa'), 'atalho caixa filtra por keyword');
  });
};
