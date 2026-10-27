// Testes — A5 restore-on-reinstall.
//
// Apos reinstalar o APK, localStorage do WebView e' limpo. Se o backup
// local (MediaStore publico ou Filesystem scoped) sobreviveu, oferecemos
// restore via evento window 'cdb-restore-prompt' que index.html escuta.
'use strict';

module.exports = function ({ test, sandbox, assert }) {
  const cdbSync = () => sandbox.window && sandbox.window.cdbSync;

  // Executa a função real do HTML em armazenamento isolado, sem tocar nos dados
  // do harness nem depender de permissões Android para reproduzir o primeiro boot.
  function onboardingFixture(backup, accept = false, existing = null) {
    const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm');
    const html = fs.readFileSync(path.resolve(__dirname, '../../EasyStock.Api/wwwroot/pwa/index.html'), 'utf8');
    const begin = html.indexOf('async function maybeOfferLocalRestore() {');
    const end = html.indexOf('// HEALTH CHECK', begin);
    assert.ok(begin >= 0 && end > begin);
    const calls = { reads: 0, prompts: [], picks: 0, applied: [], writes: [], alerts: [], timers: [] };
    const context = {
      localStorage: { getItem: key => key === 'cdb-orders' ? existing : null, setItem: (key, value) => calls.writes.push([key, value]) },
      window: { cdbSync: {
        readLocalBackup: async () => { calls.reads++; return backup; },
        hasPublicBackupPlugin: () => true,
        pickBackupViaSAF: async () => { calls.picks++; return null; }
      } },
      confirm: message => { calls.prompts.push(message); return accept; },
      applyBackupPayload: async payload => calls.applied.push(payload),
      alert: message => calls.alerts.push(message),
      setTimeout: (callback, delay) => calls.timers.push(delay),
      console: { warn: error => { throw error; } }
    };
    vm.createContext(context);
    vm.runInContext(html.slice(begin, end), context);
    return { calls, run: () => context.maybeOfferLocalRestore() };
  }

  test('#2091 onboarding: instalação vazia com plugin e sem backup não interrompe nem escreve', async () => {
    const fixture = onboardingFixture(null, true);
    await fixture.run();
    assert.deepStrictEqual(fixture.calls, { reads: 1, prompts: [], picks: 0, applied: [], writes: [], alerts: [], timers: [] });
  });

  test('#2091 onboarding: backup vazio não oferece restauração', async () => {
    const fixture = onboardingFixture({ data: { 'cdb-orders': '[]' } }, true);
    await fixture.run();
    assert.strictEqual(fixture.calls.prompts.length, 0);
    assert.strictEqual(fixture.calls.picks, 0);
    assert.strictEqual(fixture.calls.applied.length, 0);
    assert.strictEqual(fixture.calls.writes.length, 0);
  });

  for (const accept of [false, true]) {
    test('#2091 onboarding: backup real ' + (accept ? 'restaura somente após confirmação' : 'respeita recusa sem escrita'), async () => {
      const backup = { capturedAt: 1700000000000, data: { 'cdb-orders': '[{"id":"preservado"}]' } };
      const fixture = onboardingFixture(backup, accept);
      await fixture.run();
      assert.strictEqual(fixture.calls.prompts.length, 1);
      assert.ok(fixture.calls.prompts[0].includes('1 pedidos'));
      assert.strictEqual(fixture.calls.picks, 0);
      assert.deepStrictEqual(fixture.calls.applied, accept ? [backup] : []);
      assert.strictEqual(fixture.calls.writes.length, accept ? 2 : 0);
      assert.deepStrictEqual(fixture.calls.timers, accept ? [600] : []);
    });
  }

  test('#2091 onboarding: dados existentes impedem consulta e oferta de backup', async () => {
    const fixture = onboardingFixture({ data: { 'cdb-orders': '[{"id":"backup"}]' } }, true, '[{"id":"atual"}]');
    await fixture.run();
    assert.deepStrictEqual(fixture.calls, { reads: 0, prompts: [], picks: 0, applied: [], writes: [], alerts: [], timers: [] });
  });

  test('A5: applyBackupSnapshot escreve chaves cdb-* no localStorage', () => {
    assert.ok(cdbSync() && cdbSync().applyBackupSnapshot, 'cdbSync.applyBackupSnapshot deve existir apos boot');
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
    assert.ok(cdbSync() && cdbSync().applyBackupSnapshot, 'cdbSync.applyBackupSnapshot deve existir apos boot');
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
    assert.ok(cdbSync() && cdbSync().applyBackupSnapshot, 'cdbSync.applyBackupSnapshot deve existir apos boot');
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
    assert.ok(cdbSync() && cdbSync().applyBackupSnapshot, 'cdbSync.applyBackupSnapshot deve existir apos boot');
    assert.throws(() => cdbSync().applyBackupSnapshot(null), /invalido/);
    assert.throws(() => cdbSync().applyBackupSnapshot({}), /invalido/);
    assert.throws(() => cdbSync().applyBackupSnapshot({ data: null }), /invalido/);
    assert.throws(() => cdbSync().applyBackupSnapshot({ data: 'string' }), /invalido/);
  });

  test('A5: maybeOfferRestoreOnReinstall existe na API publica', () => {
    assert.ok(cdbSync(), 'cdbSync deve existir apos boot');
    assert.strictEqual(typeof cdbSync().maybeOfferRestoreOnReinstall, 'function',
      'maybeOfferRestoreOnReinstall exposta no cdbSync');
    assert.strictEqual(typeof cdbSync().applyBackupSnapshot, 'function');
    assert.strictEqual(typeof cdbSync().readLocalBackup, 'function');
  });
};
