// Testes — F10-C-7: photo-store IndexedDB wrapper
//
// Valida: init, save/get/remove, listPending, markUploaded, getStats
'use strict';

module.exports = function ({ test, sandbox, assert, skip }) {
  const ps = () => sandbox.window && sandbox.window.cdbPhotoStore;

  test('F10-C-7: photo-store existe no window apos boot', () => {
    assert.ok(ps(), 'cdbPhotoStore exposto no window');
    assert.strictEqual(typeof ps().init, 'function', 'init e funcao');
    assert.strictEqual(typeof ps().save, 'function', 'save e funcao');
    assert.strictEqual(typeof ps().get, 'function', 'get e funcao');
    assert.strictEqual(typeof ps().remove, 'function', 'remove e funcao');
    assert.strictEqual(typeof ps().listPending, 'function', 'listPending e funcao');
    assert.strictEqual(typeof ps().listByBatch, 'function', 'listByBatch e funcao');
    assert.strictEqual(typeof ps().markUploaded, 'function', 'markUploaded e funcao');
    assert.strictEqual(typeof ps().getStats, 'function', 'getStats e funcao');
    assert.strictEqual(typeof ps().clear, 'function', 'clear e funcao');
  });

  test('F10-C-7: upload-photos existe no window apos boot', () => {
    const pu = sandbox.window && sandbox.window.cdbPhotoUpload;
    assert.ok(pu, 'cdbPhotoUpload exposto no window');
    assert.strictEqual(typeof pu.flush, 'function', 'flush e funcao');
    assert.strictEqual(typeof pu.cleanup, 'function', 'cleanup e funcao');
  });

  test('F10-C-7: cdbSync expoe computeStrippedBatch', () => {
    const sync = sandbox.window && sandbox.window.cdbSync;
    assert.ok(sync && sync.computeStrippedBatch, 'cdbSync.computeStrippedBatch deve existir apos boot');
    // Testa strip de batch com foto pesada (>4KB data URL)
    var bigPhoto = 'data:image/jpeg;base64,' + 'A'.repeat(5000);
    var batch = {
      id: 'test-batch-1',
      code: 'TEST-001',
      batchPhoto: bigPhoto,
      items: [
        { productId: 'p1', name: 'Item 1', photo: bigPhoto, qty: 5 },
        { productId: 'p2', name: 'Item 2', qty: 3 }
      ]
    };
    var stripped = sync.computeStrippedBatch(batch);
    assert.ok(!stripped.batchPhoto, 'batchPhoto removido');
    assert.ok(stripped.batchPhotoHash, 'batchPhotoHash presente');
    assert.ok(stripped.batchPhotoHash.startsWith('fnv1a:'), 'hash e fnv1a');
    assert.ok(!stripped.items[0].photo, 'item[0].photo removido');
    assert.ok(stripped.items[0].photoHash, 'item[0].photoHash presente');
    assert.strictEqual(stripped.items[1].qty, 3, 'item sem foto preservado');
    // Original batch nao mutou (deep clone)
    assert.ok(batch.batchPhoto, 'original batch.batchPhoto intacto');
    assert.ok(batch.items[0].photo, 'original item.photo intacto');
  });

  test('F10-C-7: getStats retorna estrutura valida', async () => {
    if (!ps() || !ps().ready) skip('photo-store IDB indisponivel no sandbox vm (sem IndexedDB)');
    var stats = await ps().getStats();
    assert.ok(typeof stats.total === 'number', 'total e numero');
    assert.ok(typeof stats.pending === 'number', 'pending e numero');
    assert.ok(typeof stats.uploadedCount === 'number', 'uploadedCount e numero');
    assert.ok(typeof stats.totalSizeEstimate === 'number', 'totalSizeEstimate e numero');
  });
};
