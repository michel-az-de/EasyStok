// Testes — A5 restore-on-reinstall.
//
// Apos reinstalar o APK, localStorage do WebView e' limpo. Se o backup
// local (MediaStore publico ou Filesystem scoped) sobreviveu, oferecemos
// restore via evento window 'cdb-restore-prompt' que index.html escuta.
'use strict';

module.exports = function ({ test, sandbox, assert }) {
  const cdbSync = () => sandbox.window && sandbox.window.cdbSync;

  test('A5: applyBackupSnapshot escreve chaves cdb-* no localStorage', () => {
    if (!cdbSync() || !cdbSync().applyBackupSnapshot) return;
    sandbox.localStorage.clear();
    const backup = {
      schema: 'cdb-backup-v1',
      capturedAt: 1700000000000,
      data: {
        'cdb-orders': '[{"id":"o1","status":"pronto"}]',
        'cdb-products': '[{"id":"p1","name":"Pao"}]',
        'cdb-clients': '[]',
        'cdb-batches': '[]'
      }
    };
    const r = cdbSync().applyBackupSnapshot(backup);
    assert.strictEqual(r.restored, 4, '4 chaves cdb-* restauradas');
    assert.strictEqual(r.skipped, 0);
    assert.strictEqual(sandbox.localStorage.getItem('cdb-orders'), '[{"id":"o1","status":"pronto"}]');
    assert.strictEqual(sandbox.localStorage.getItem('cdb-products'), '[{"id":"p1","name":"Pao"}]');
  });

  test('A5: applyBackupSnapshot NAO sobrescreve cdb-pairing nem cdb-device-id', () => {
    if (!cdbSync() || !cdbSync().applyBackupSnapshot) return;
    sandbox.localStorage.clear();
    // Cenario: device foi re-pareado apos backup. O pairing atual deve sobreviver.
    sandbox.localStorage.setItem('cdb-pairing', JSON.stringify({ apiKey: 'NOVO_KEY' }));
    sandbox.localStorage.setItem('cdb-device-id', 'dev-novo');
    const backup = {
      schema: 'cdb-backup-v1',
      data: {
        'cdb-pairing': JSON.stringify({ apiKey: 'VELHO_KEY' }),
        'cdb-device-id': 'dev-velho',
        'cdb-orders': '[{"id":"o1"}]'
      }
    };
    const r = cdbSync().applyBackupSnapshot(backup);
    assert.strictEqual(r.restored, 1, 'apenas cdb-orders deve restaurar');
    assert.strictEqual(r.skipped, 2, 'pairing e device-id pulados');
    const p = JSON.parse(sandbox.localStorage.getItem('cdb-pairing'));
    assert.strictEqual(p.apiKey, 'NOVO_KEY', 'pairing atual preservado');
    assert.strictEqual(sandbox.localStorage.getItem('cdb-device-id'), 'dev-novo');
  });

  test('A5: applyBackupSnapshot ignora chaves nao-cdb', () => {
    if (!cdbSync() || !cdbSync().applyBackupSnapshot) return;
    sandbox.localStorage.clear();
    const backup = {
      schema: 'cdb-backup-v1',
      data: {
        'cdb-orders': '[]',
        'random-key': 'lixo',
        'theme': 'dark'
      }
    };
    const r = cdbSync().applyBackupSnapshot(backup);
    assert.strictEqual(r.restored, 1);
    assert.strictEqual(r.skipped, 2);
    assert.strictEqual(sandbox.localStorage.getItem('random-key'), null);
    assert.strictEqual(sandbox.localStorage.getItem('theme'), null);
  });

  test('A5: applyBackupSnapshot rejeita backup invalido', () => {
    if (!cdbSync() || !cdbSync().applyBackupSnapshot) return;
    assert.throws(() => cdbSync().applyBackupSnapshot(null), /invalido/);
    assert.throws(() => cdbSync().applyBackupSnapshot({}), /invalido/);
    assert.throws(() => cdbSync().applyBackupSnapshot({ data: null }), /invalido/);
    assert.throws(() => cdbSync().applyBackupSnapshot({ data: 'string' }), /invalido/);
  });

  test('A5: maybeOfferRestoreOnReinstall existe na API publica', () => {
    if (!cdbSync()) return;
    assert.strictEqual(typeof cdbSync().maybeOfferRestoreOnReinstall, 'function',
      'maybeOfferRestoreOnReinstall exposta no cdbSync');
    assert.strictEqual(typeof cdbSync().applyBackupSnapshot, 'function');
    assert.strictEqual(typeof cdbSync().readLocalBackup, 'function');
  });
};
