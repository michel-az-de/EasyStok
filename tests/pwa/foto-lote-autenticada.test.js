// Testes — #2459: a foto de lote do servidor sai por rota autenticada.
//
// A <img> não manda header; o PWA busca a foto com a chave do aparelho e usa blob URL.
'use strict';

module.exports = function ({ test, sandbox, assert }) {
  const pu = () => sandbox.window && sandbox.window.cdbPhotoUpload;
  const ROTA = '/api/mobile/batches/lote-1/photos/abc123.jpg';

  function comFetch(fn, impl) {
    const original = sandbox.fetch;
    sandbox.fetch = impl;
    return Promise.resolve().then(fn).finally(() => { sandbox.fetch = original; });
  }

  function pareado() {
    sandbox.localStorage.setItem('cdb-pairing', JSON.stringify({ apiKey: 'chave-1', deviceId: 'dev-1' }));
  }

  test('#2459: reconhece só a rota da foto de lote', () => {
    assert.strictEqual(pu().ehFotoAutenticada(ROTA), true);
    assert.strictEqual(pu().ehFotoAutenticada('/api/mobile/batches/lote-1/photos/abc123.png'), true);
    assert.strictEqual(pu().ehFotoAutenticada('data:image/jpeg;base64,AAAA'), false);
    assert.strictEqual(pu().ehFotoAutenticada('https://x/files/produtos/a.jpg'), false);
    assert.strictEqual(pu().ehFotoAutenticada('/api/mobile/batches/lote-1/photos/../x.jpg'), false);
  });

  test('#2459: busca a foto com a chave do aparelho e devolve blob URL', () => comFetch(async () => {
    pareado();
    const src = await pu().srcAutenticado(ROTA);
    assert.strictEqual(src, 'blob://stub');
  }, (url, init) => {
    assert.strictEqual(url, ROTA);
    assert.strictEqual(init.headers['X-Mobile-Api-Key'], 'chave-1');
    assert.strictEqual(init.headers['X-Device-Id'], 'dev-1');
    return Promise.resolve({ ok: true, blob: () => Promise.resolve(new sandbox.Blob([])) });
  }));

  test('#2459: sem pareamento ou com 401 não exibe foto', () => comFetch(async () => {
    // URL própria: a busca com sucesso do teste anterior fica em cache na sessão.
    const outra = '/api/mobile/batches/lote-2/photos/def456.jpg';
    sandbox.localStorage.removeItem('cdb-pairing');
    assert.strictEqual(await pu().srcAutenticado(outra), null);
    pareado();
    assert.strictEqual(await pu().srcAutenticado(outra), null, 'falha não fica em cache e 401 não vira foto');
  }, () => Promise.resolve({ ok: false, status: 401 })));

  test('#2459: data URL e https seguem como estão, sem fetch', () => comFetch(async () => {
    assert.strictEqual(await pu().srcAutenticado('data:image/png;base64,AAAA'), 'data:image/png;base64,AAAA');
    assert.strictEqual(await pu().srcAutenticado('https://x/a.jpg'), 'https://x/a.jpg');
  }, () => { throw new Error('não deveria buscar'); }));

  test('#2459: safePhotoUrl aceita a rota da foto de lote', () => {
    assert.strictEqual(sandbox.safePhotoUrl(ROTA), ROTA);
    assert.strictEqual(sandbox.safePhotoUrl('/files/mobile-photos/x/y.jpg'), '');
  });
};
