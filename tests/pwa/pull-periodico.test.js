// Testes — #2347 o laço de 30 s também puxa do servidor
//
// Mudança feita no servidor (console, pedido do site, pagamento) não gera SSE para o aparelho.
// O ciclo do laço passou a chamar o pull, com intervalo mínimo para não dobrar com o SSE.
'use strict';

module.exports = function ({ test, sandbox, assert }) {
  function fakeResp(body) {
    return { ok: true, status: 200, headers: { get() { return null; } }, json: async () => body };
  }

  async function comRelogio(fn) {
    const RealDate = Date;
    let agora = RealDate.now() + 3600 * 1000; // bem depois de qualquer pull do boot
    sandbox.Date = class extends RealDate { static now() { return agora; } };
    let pulls = 0;
    sandbox.AbortController = AbortController;
    sandbox.URLSearchParams = URLSearchParams;
    sandbox.fetch = async (url) => {
      if (String(url).indexOf('/sync/pull') >= 0) { pulls++; return fakeResp({ serverTime: 1, mutations: [] }); }
      return fakeResp({ acceptedIds: [] });
    };
    try {
      await fn({ avancar: ms => { agora += ms; }, pulls: () => pulls });
    } finally {
      sandbox.Date = RealDate;
      sandbox.fetch = () => Promise.reject(new Error('fetch nao implementado em testes'));
      delete sandbox.AbortController;
      delete sandbox.URLSearchParams;
    }
  }
  const assentar = () => new Promise(r => setTimeout(r, 30));

  test('#2347 ciclo do laço com fila vazia puxa; dentro do intervalo mínimo não puxa de novo', async () => {
    const S = sandbox.window.cdbSync;
    await comRelogio(async (c) => {
      await S.runSyncCycle(); await assentar();
      assert.strictEqual(c.pulls(), 1, 'primeiro ciclo deve puxar');
      c.avancar(30000);
      await S.runSyncCycle(); await assentar();
      assert.strictEqual(c.pulls(), 1, 'dentro de 60 s não puxa');
      c.avancar(31000);
      await S.runSyncCycle(); await assentar();
      assert.strictEqual(c.pulls(), 2, 'passado o intervalo puxa de novo');
    });
  });

  test('#2347 ciclo com a importação de backup segurando a sync (review) não puxa', async () => {
    const S = sandbox.window.cdbSync;
    await comRelogio(async (c) => {
      await S.beginBackupImport();
      try {
        const antes = c.pulls();
        const status = await S.runSyncCycle(); await assentar();
        assert.strictEqual(status, 'review');
        assert.strictEqual(c.pulls(), antes, 'importação em andamento não puxa');
      } finally { S.endBackupImport(); }
    });
  });
};
