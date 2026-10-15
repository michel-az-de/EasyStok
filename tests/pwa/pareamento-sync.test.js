// Testes — #1474 pareamento e cursor do pull
//
// Aparelho recem-pareado fazia flush, o flush carimbava cdb-last-sync com
// Date.now() e o primeiro pull saia com since=<agora>: o catalogo que ja
// existia no ERP nunca chegava. O cursor do pull agora e proprio
// (cdb-pull-cursor), so avanca com o serverTime da resposta do pull, e
// pareamento/troca de loja o zeram.
'use strict';

module.exports = function ({ test, sandbox, assert }) {
  function fakeResp(status, body) {
    return {
      ok: status >= 200 && status < 300,
      status,
      headers: { get() { return null; } },
      json: async () => body,
      text: async () => JSON.stringify(body)
    };
  }

  // Intercepta fetch do sandbox; devolve a lista de URLs de /sync/pull.
  // O sandbox base nao tem AbortController/URLSearchParams (o pull usa os dois).
  function stubFetch(handlers) {
    const pulls = [];
    sandbox.AbortController = AbortController;
    sandbox.URLSearchParams = URLSearchParams;
    sandbox.fetch = async (url, opts) => {
      const u = String(url);
      if (u.indexOf('/sync/pull') >= 0) {
        pulls.push(u);
        return fakeResp(200, handlers.pull ? handlers.pull(u) : { serverTime: 0, mutations: [] });
      }
      if (u.indexOf('/devices/pair') >= 0 && handlers.pair) return fakeResp(200, handlers.pair());
      return fakeResp(404, {});
    };
    return pulls;
  }

  function sinceOf(url) {
    const m = /[?&]since=([^&]*)/.exec(url);
    return m ? decodeURIComponent(m[1]) : null;
  }

  function restoreFetch() {
    sandbox.fetch = () => Promise.reject(new Error('fetch nao implementado em testes'));
    delete sandbox.AbortController;
    delete sandbox.URLSearchParams;
  }

  test('#1474 W1: aparelho ja pareado sem cursor semeia o cursor com cdb-last-sync menos 10 min', async () => {
    // Deploy da versao com cursor proprio: o aparelho ja pareado nao tem
    // cdb-pull-cursor. since=0 traria o historico inteiro (fotos de lote,
    // pedidos purgados, fechamentos) e podia estourar a cota do localStorage.
    const cdbSync = sandbox.window.cdbSync;
    cdbSync.clearPairing();
    sandbox.localStorage.setItem('cdb-pairing', JSON.stringify({ apiKey: 'mk_t', empresaId: 'e', lojaId: 'l' }));
    sandbox.localStorage.setItem('cdb-last-sync', '1791502265856');
    sandbox.localStorage.removeItem('cdb-pull-cursor');
    const pulls = stubFetch({});
    try {
      await cdbSync.pull();
    } finally { restoreFetch(); }
    assert.strictEqual(pulls.length, 1, 'um pull');
    assert.strictEqual(sinceOf(pulls[0]), String(1791502265856 - 10 * 60 * 1000),
      'cursor semeado com o ultimo sync menos a margem');
  });

  test('#1474 W1: aparelho sem nenhum historico (sem cursor e sem last-sync) faz pull completo', async () => {
    const cdbSync = sandbox.window.cdbSync;
    cdbSync.clearPairing();
    sandbox.localStorage.setItem('cdb-pairing', JSON.stringify({ apiKey: 'mk_t', empresaId: 'e', lojaId: 'l' }));
    sandbox.localStorage.removeItem('cdb-last-sync');
    sandbox.localStorage.removeItem('cdb-pull-cursor');
    const pulls = stubFetch({});
    try {
      await cdbSync.pull();
    } finally { restoreFetch(); }
    assert.strictEqual(sinceOf(pulls[0]), '0');
  });

  test('#1474 W1: pareamento em aparelho com historico ainda faz pull completo', async () => {
    const cdbSync = sandbox.window.cdbSync;
    cdbSync.clearPairing();
    sandbox.localStorage.removeItem('cdb-pull-cursor');
    sandbox.localStorage.setItem('cdb-last-sync', '1791502265856');
    const pulls = stubFetch({
      pair: () => ({ apiKey: 'mk_novo', empresaId: 'e', lojaId: 'l', label: 'x',
                     defaultOperatorName: null, pairedAt: '2026-10-08T00:00:00Z', deviceId: 'd' }),
      pull: () => ({ serverTime: 1791502299999, mutations: [] })
    });
    try {
      await cdbSync.pairWithCode('123456', 'teste');
    } finally { restoreFetch(); }
    assert.ok(pulls.length >= 1, 'pareamento dispara pull');
    assert.strictEqual(sinceOf(pulls[0]), '0', 'pareamento pede o catalogo inteiro, nao a semente do last-sync');
  });

  // Timeout não comprova recebimento: mantém o cursor para não perder catálogo antigo.
  function stubPullTimeout() {
    const pulls = [];
    sandbox.AbortController = AbortController;
    sandbox.URLSearchParams = URLSearchParams;
    sandbox.fetch = async (url) => {
      pulls.push(String(url));
      const e = new Error('The operation was aborted');
      e.name = 'AbortError';
      throw e;
    };
    return pulls;
  }

  test('Onda 0: timeout nao pula dados antigos ainda nao recebidos', async () => {
    const cdbSync = sandbox.window.cdbSync;
    cdbSync.clearPairing();
    sandbox.localStorage.setItem('cdb-pairing', JSON.stringify({ apiKey: 'mk_t', empresaId: 'e', lojaId: 'l' }));
    sandbox.localStorage.setItem('cdb-pull-cursor', '0');
    sandbox.localStorage.removeItem('cdb-pull-full-fails');
    const pulls = stubPullTimeout();
    try {
      await cdbSync.pull();
      await cdbSync.pull();
      assert.strictEqual(sandbox.localStorage.getItem('cdb-pull-cursor'), '0',
        'dois timeouts ainda tentam o pull completo');
      await cdbSync.pull();
      await cdbSync.pull();
    } finally { restoreFetch(); }
    assert.deepStrictEqual(pulls.slice(0, 3).map(sinceOf), ['0', '0', '0']);
    assert.strictEqual(sinceOf(pulls[3]), '0', 'repetir sem descartar catalogo ou pedidos antigos');
  });

  test('Onda 0: falha ao persistir dados nao avanca cursor e reenvio recupera', async () => {
    const cdbSync = sandbox.window.cdbSync;
    cdbSync.clearPairing();
    sandbox.localStorage.setItem('cdb-pairing', JSON.stringify({ apiKey: 'mk_t', empresaId: 'e', lojaId: 'l' }));
    sandbox.localStorage.setItem('cdb-pull-cursor', '123');
    const appOriginal = sandbox.window.cdbApp;
    const setOriginal = sandbox.localStorage.setItem;
    const state = { products: [] };
    sandbox.window.cdbApp = { getState: () => state };
    const pulls = stubFetch({ pull: () => ({ serverTime: 456, mutations: [
      { type: 'product.upsert', payload: { id: 'produto-regressao', name: 'Nhoque' } }
    ] }) });
    try {
      sandbox.localStorage.setItem = (key, value) => {
        if (key === 'cdb-products') throw new Error('QuotaExceededError');
        setOriginal(key, value);
      };
      await cdbSync.pull();
      assert.strictEqual(sandbox.localStorage.getItem('cdb-pull-cursor'), '123');
      sandbox.localStorage.setItem = setOriginal;
      await cdbSync.pull();
      assert.strictEqual(sinceOf(pulls[1]), '123');
      assert.strictEqual(sandbox.localStorage.getItem('cdb-pull-cursor'), '456');
      assert.strictEqual(JSON.parse(sandbox.localStorage.getItem('cdb-products'))[0].id, 'produto-regressao');
    } finally {
      sandbox.localStorage.setItem = setOriginal;
      sandbox.window.cdbApp = appOriginal;
      restoreFetch();
    }
  });

  test('#1474 W1: pull completo bem-sucedido zera a contagem de timeouts', async () => {
    const cdbSync = sandbox.window.cdbSync;
    cdbSync.clearPairing();
    sandbox.localStorage.setItem('cdb-pairing', JSON.stringify({ apiKey: 'mk_t', empresaId: 'e', lojaId: 'l' }));
    sandbox.localStorage.setItem('cdb-pull-cursor', '0');
    sandbox.localStorage.setItem('cdb-pull-full-fails', '2');
    stubFetch({ pull: () => ({ serverTime: 1700000000123, mutations: [] }) });
    try {
      await cdbSync.pull();
    } finally { restoreFetch(); }
    assert.strictEqual(sandbox.localStorage.getItem('cdb-pull-full-fails'), null);
    assert.strictEqual(sandbox.localStorage.getItem('cdb-pull-cursor'), '1700000000123');
  });

  test('#1474: cursor avanca com o serverTime do pull, nao com o relogio do aparelho', async () => {
    const cdbSync = sandbox.window.cdbSync;
    cdbSync.clearPairing();
    sandbox.localStorage.setItem('cdb-pairing', JSON.stringify({ apiKey: 'mk_t', empresaId: 'e', lojaId: 'l' }));
    sandbox.localStorage.removeItem('cdb-pull-cursor');
    sandbox.localStorage.removeItem('cdb-last-sync');
    const pulls = stubFetch({ pull: () => ({ serverTime: 1700000000123, mutations: [] }) });
    try {
      await cdbSync.pull();
      await cdbSync.pull();
    } finally { restoreFetch(); }
    assert.strictEqual(sinceOf(pulls[0]), '0');
    assert.strictEqual(sinceOf(pulls[1]), '1700000000123', 'segundo pull parte do serverTime');
    assert.strictEqual(sandbox.localStorage.getItem('cdb-pull-cursor'), '1700000000123');
  });

  test('#1474: pairWithCode zera o cursor e o pull inicial e completo', async () => {
    const cdbSync = sandbox.window.cdbSync;
    cdbSync.clearPairing();
    sandbox.localStorage.setItem('cdb-pull-cursor', '1791502265856');
    sandbox.localStorage.setItem('cdb-last-sync', '1791502265856');
    const pulls = stubFetch({
      pair: () => ({ apiKey: 'mk_novo', empresaId: 'e', lojaId: 'l', label: 'x',
                     defaultOperatorName: null, pairedAt: '2026-10-08T00:00:00Z', deviceId: 'd' }),
      pull: () => ({ serverTime: 1791502299999, mutations: [] })
    });
    try {
      await cdbSync.pairWithCode('123456', 'teste');
    } finally { restoreFetch(); }
    assert.ok(pulls.length >= 1, 'pareamento dispara pull');
    assert.strictEqual(sinceOf(pulls[0]), '0', 'pull pos-pareamento nao herda cursor antigo');
  });

  function pairCom(empresaNome) {
    return stubFetch({
      pair: () => ({ apiKey: 'mk_novo', empresaId: 'e', lojaId: 'l', label: 'x', defaultOperatorName: null,
                     pairedAt: '2026-10-08T00:00:00Z', deviceId: 'd', empresaNome, lojaNome: 'Casa da Baba | Centro' }),
      pull: () => ({ serverTime: 1, mutations: [] })
    });
  }

  test('#1474: pareamento troca "Minha empresa" pelo nome devolvido pelo servidor', async () => {
    const cdbSync = sandbox.window.cdbSync;
    cdbSync.clearPairing();
    sandbox.localStorage.removeItem('cdb-empresa-name');
    pairCom('Casa da Baba');
    try {
      await cdbSync.pairWithCode('123456', 'teste');
    } finally { restoreFetch(); }
    assert.strictEqual(sandbox.localStorage.getItem('cdb-empresa-name'), 'Casa da Baba');
    assert.strictEqual(cdbSync.getPairing().empresaNome, 'Casa da Baba');
  });

  test('#1474: pareamento nao sobrescreve nome personalizado em Ajustes', async () => {
    const cdbSync = sandbox.window.cdbSync;
    cdbSync.clearPairing();
    sandbox.localStorage.setItem('cdb-empresa-name', 'Baba Centro');
    pairCom('Casa da Baba');
    try {
      await cdbSync.pairWithCode('123456', 'teste');
    } finally { restoreFetch(); }
    assert.strictEqual(sandbox.localStorage.getItem('cdb-empresa-name'), 'Baba Centro');
  });
  test('#1474: erro do pareamento no formato da API mostra o motivo, nao [object Object]', async () => {
    const cdbSync = sandbox.window.cdbSync;
    sandbox.AbortController = AbortController;
    sandbox.fetch = async () => fakeResp(409, { error: { code: 'DUPLICATE_RESOURCE', message: 'Registro duplicado', detail: 'Ja existe um registro.' } });
    try {
      let msg = null;
      try { await cdbSync.pairWithCode('123456', 'teste'); } catch (e) { msg = e.message; }
      assert.equal(msg, 'Ja existe um registro.');
    } finally {
      restoreFetch();
    }
  });
};
