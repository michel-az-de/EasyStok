// Testes — A6 strip de fotos pesadas antes de enqueue.
//
// Bytes de foto base64 multi-MB inflavam mutations enfileiradas em
// localStorage (cap 5MB) e estouravam quota em silencio — saveQueue caia
// no catch, mutations perdidas. stripBatchPhotoBytes troca os bytes por
// um hash referencial antes do enqueue.
'use strict';

module.exports = function ({ test, sandbox, assert }) {
  const cdbSync = () => sandbox.window && sandbox.window.cdbSync;

  function makeDataUrl(sizeChars) {
    // Gera um data: URL com sizeChars caracteres apos o prefixo.
    return 'data:image/png;base64,' + 'A'.repeat(Math.max(0, sizeChars - 22));
  }

  test('A6: computeStrippedBatch remove batchPhoto base64 grande', () => {
    assert.ok(cdbSync() && cdbSync().computeStrippedBatch, 'cdbSync.computeStrippedBatch deve existir apos boot');
    const batch = {
      id: 'b1',
      code: 'L-001',
      batchPhoto: makeDataUrl(50_000), // ~50KB, claramente acima do limiar 4KB
      items: []
    };
    const stripped = cdbSync().computeStrippedBatch(batch);
    assert.strictEqual(stripped.batchPhoto, undefined, 'batchPhoto deve ser removido');
    assert.ok(stripped.batchPhotoHash && stripped.batchPhotoHash.indexOf('fnv1a:') === 0,
      'batchPhotoHash deve estar presente como fnv1a:...');
    // Estado original nao mutado
    assert.ok(batch.batchPhoto && batch.batchPhoto.indexOf('data:') === 0,
      'original nao deve ser mutado');
  });

  test('A6: computeStrippedBatch remove items[].photo base64 grande', () => {
    assert.ok(cdbSync() && cdbSync().computeStrippedBatch, 'cdbSync.computeStrippedBatch deve existir apos boot');
    const batch = {
      id: 'b2',
      code: 'L-002',
      items: [
        { productId: 'p1', name: 'Pao', qty: 2, photo: makeDataUrl(60_000) },
        { productId: 'p2', name: 'Doce', qty: 1, photo: makeDataUrl(80_000) }
      ]
    };
    const stripped = cdbSync().computeStrippedBatch(batch);
    assert.strictEqual(stripped.items.length, 2);
    stripped.items.forEach((it, i) => {
      assert.strictEqual(it.photo, undefined, 'items[' + i + '].photo deve sair');
      assert.ok(it.photoHash && it.photoHash.indexOf('fnv1a:') === 0,
        'items[' + i + '].photoHash deve entrar');
      assert.ok(it.name, 'demais campos preservados');
    });
  });

  test('A6: computeStrippedBatch NAO toca dataURLs pequenos (< 4KB)', () => {
    assert.ok(cdbSync() && cdbSync().computeStrippedBatch, 'cdbSync.computeStrippedBatch deve existir apos boot');
    // Icones inline SVG ou placeholders nao devem ser strippados — sao pequenos
    // e tem semantica de UI (nao saturam quota).
    const smallIcon = 'data:image/svg+xml;base64,' + 'A'.repeat(100);
    const batch = {
      id: 'b3',
      batchPhoto: smallIcon,
      items: [{ productId: 'p1', photo: smallIcon }]
    };
    const stripped = cdbSync().computeStrippedBatch(batch);
    assert.strictEqual(stripped.batchPhoto, smallIcon, 'icones pequenos preservados');
    assert.strictEqual(stripped.items[0].photo, smallIcon, 'item icone preservado');
    assert.strictEqual(stripped.batchPhotoHash, undefined);
    assert.strictEqual(stripped.items[0].photoHash, undefined);
  });

  test('A6: computeStrippedBatch e idempotente (re-aplicar mesmo resultado)', () => {
    assert.ok(cdbSync() && cdbSync().computeStrippedBatch, 'cdbSync.computeStrippedBatch deve existir apos boot');
    const batch = {
      id: 'b4',
      batchPhoto: makeDataUrl(20_000),
      items: [{ productId: 'p', photo: makeDataUrl(30_000) }]
    };
    const once = cdbSync().computeStrippedBatch(batch);
    const twice = cdbSync().computeStrippedBatch(once);
    // JSON.stringify normaliza ordem de chaves e remove undefined — comparacao
    // mais robusta que deepStrictEqual pra objetos clonados via JSON.parse.
    assert.strictEqual(JSON.stringify(twice), JSON.stringify(once),
      'segunda passagem nao altera resultado');
  });

  test('A6: hash bate pra mesmo conteudo, difere pra conteudos diferentes', () => {
    assert.ok(cdbSync() && cdbSync().computeStrippedBatch, 'cdbSync.computeStrippedBatch deve existir apos boot');
    const a = { id: 'a', batchPhoto: makeDataUrl(20_000) };
    const b = { id: 'b', batchPhoto: makeDataUrl(20_000) }; // mesmo conteudo
    const c = { id: 'c', batchPhoto: makeDataUrl(30_000) }; // diferente
    const sa = cdbSync().computeStrippedBatch(a);
    const sb = cdbSync().computeStrippedBatch(b);
    const sc = cdbSync().computeStrippedBatch(c);
    assert.strictEqual(sa.batchPhotoHash, sb.batchPhotoHash, 'mesmo conteudo => mesmo hash');
    assert.notStrictEqual(sa.batchPhotoHash, sc.batchPhotoHash, 'conteudo diferente => hash diferente');
  });

  test('A6: stripa nada se batch sem fotos', () => {
    assert.ok(cdbSync() && cdbSync().computeStrippedBatch, 'cdbSync.computeStrippedBatch deve existir apos boot');
    const batch = { id: 'plain', code: 'L-X', items: [{ productId: 'p', qty: 1 }] };
    const stripped = cdbSync().computeStrippedBatch(batch);
    assert.deepStrictEqual(stripped, batch);
  });

  test('A6: tolera batch null/undefined', () => {
    assert.ok(cdbSync() && cdbSync().computeStrippedBatch, 'cdbSync.computeStrippedBatch deve existir apos boot');
    assert.strictEqual(cdbSync().computeStrippedBatch(null), null);
    assert.strictEqual(cdbSync().computeStrippedBatch(undefined), undefined);
  });
};
