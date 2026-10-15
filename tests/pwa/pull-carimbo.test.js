// Testes — #1520 (ADR-0060) o que o pull passou a trazer com o carimbo do servidor
//
// Antes o pull de lote filtrava pela data de criacao: um lote que o aparelho ja
// tinha nunca voltava. Com o carimbo do servidor, o descarte e a exclusao feitos
// em outro aparelho descem. O lote local so recebe as marcas: trocar o objeto
// inteiro apagaria a foto e o status locais, que o servidor nao guarda.
'use strict';

module.exports = function ({ test, sandbox, assert }) {
  function fakeResp(body) {
    return { ok: true, status: 200, headers: { get() { return null; } }, json: async () => body };
  }

  async function pullCom(state, mutations) {
    const cdbSync = sandbox.window.cdbSync;
    const original = sandbox.window.cdbApp;
    // Reload agendado por pull de teste anterior (setTimeout de 100 ms) nao pode cair na contagem.
    await new Promise(r => setTimeout(r, 200));
    const reloads = [];
    const reloadOriginal = sandbox.location.reload;
    sandbox.location.reload = () => reloads.push(1);
    sandbox.window.cdbApp = Object.assign({}, original, {
      getState: () => state, setPendingSync() {}, markAllSynced() {}, showToast() {}
    });
    sandbox.AbortController = AbortController;
    sandbox.URLSearchParams = URLSearchParams;
    sandbox.fetch = async (url) => fakeResp(String(url).indexOf('/sync/pull') >= 0
      ? { serverTime: 1, mutations }
      : { acceptedIds: [] });
    try {
      await cdbSync.pull();
      await new Promise(r => setTimeout(r, 250));   // o reload sai num setTimeout apos o flush
    } finally {
      sandbox.fetch = () => Promise.reject(new Error('fetch nao implementado em testes'));
      delete sandbox.AbortController;
      delete sandbox.URLSearchParams;
      sandbox.location.reload = reloadOriginal;
      if (original) sandbox.window.cdbApp = original; else delete sandbox.window.cdbApp;
      cdbSync.clearQueue();
    }
    return reloads.length;
  }

  function loteLocal() {
    return {
      id: 'b-pull-1', code: 'LOT-1', lote: 'LOT-261010', createdAt: 1, status: 'open',
      batchPhoto: 'data:image/jpeg;base64,FOTO-LOCAL', lastOperatorName: 'Thati',
      items: [{ productId: 'p1', name: 'Lasanha', qty: 5, photo: 'data:image/jpeg;base64,ITEM' }]
    };
  }
  // O servidor devolve o lote sem as fotos pesadas e sem os campos so do aparelho.
  function loteDoServidor(marcas) {
    return Object.assign({
      id: 'b-pull-1', code: 'LOT-1', lote: 'LOT-261010', createdAt: 1, batchPhoto: null,
      items: [{ productId: 'p1', name: 'Lasanha', qty: 5, photo: null }],
      deleted: null, deletedAt: null, deletedBy: null,
      discarded: null, discardedAt: null, discardedBy: null, discardReason: null
    }, marcas || {});
  }
  function estado(batches) {
    return { products: [], clients: [], orders: [], cashEntries: [], batches, cashClosings: [] };
  }

  test('#1520: descarte feito em outro aparelho chega ao lote local sem apagar foto nem status', async () => {
    const state = estado([loteLocal()]);

    const reloads = await pullCom(state, [{
      id: 'srv-1', deviceId: 'outro', type: 'batch.upsert', ts: 1,
      payload: loteDoServidor({ discarded: true, discardedAt: 123, discardedBy: 'Ana', discardReason: 'vencido' })
    }]);

    const lote = state.batches[0];
    assert.strictEqual(lote.discarded, true);
    assert.strictEqual(lote.discardReason, 'vencido');
    assert.strictEqual(lote.batchPhoto, 'data:image/jpeg;base64,FOTO-LOCAL', 'foto local preservada');
    assert.strictEqual(lote.items[0].photo, 'data:image/jpeg;base64,ITEM');
    assert.strictEqual(lote.status, 'open');
    assert.strictEqual(reloads, 1, 'mudou: a tela recarrega');
  });

  test('#1520: desfazer o descarte em outro aparelho limpa a marca local', async () => {
    const local = Object.assign(loteLocal(), { discarded: true, discardedAt: 123, discardReason: 'vencido' });
    const state = estado([local]);

    await pullCom(state, [{ id: 'srv-2', deviceId: 'outro', type: 'batch.upsert', ts: 1, payload: loteDoServidor() }]);

    assert.ok(!state.batches[0].discarded, 'marca removida');
    assert.ok(!state.batches[0].discardReason);
    assert.strictEqual(state.batches[0].batchPhoto, 'data:image/jpeg;base64,FOTO-LOCAL');
  });

  test('#1520: lote que desce igual ao local nao recarrega a tela', async () => {
    const state = estado([loteLocal()]);

    const reloads = await pullCom(state, [{ id: 'srv-3', deviceId: 'outro', type: 'batch.upsert', ts: 1, payload: loteDoServidor() }]);

    assert.strictEqual(reloads, 0);
    assert.strictEqual(state.batches[0].status, 'open');
  });

  test('#1520: lote que o aparelho nao tem continua entrando inteiro', async () => {
    const state = estado([]);

    await pullCom(state, [{ id: 'srv-4', deviceId: 'outro', type: 'batch.upsert', ts: 1, payload: loteDoServidor() }]);

    assert.strictEqual(state.batches.length, 1);
    assert.strictEqual(state.batches[0].id, 'b-pull-1');
  });
};
