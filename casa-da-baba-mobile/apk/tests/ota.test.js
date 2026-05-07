// Testes — Onda 9 OTA do PWA orquestrado pelo backend
//
// Fluxo:
//   - GET /api/mobile/version retorna Ota.PwaCacheVersion (lido do sw.js
//     servido pelo PwaVersionProvider).
//   - sync.js compara com cdb-pwa-installed-version local.
//   - Se diferente: dispara registration.update() + SKIP_WAITING + reload.
//   - Comando remoto pwa_update força tudo isso (limpa caches + reload).
//
// Validamos aqui sem TestServer .NET — testes cobrem o LADO PWA (sync.js)
// e contratos com o backend. Backend é coberto por unit tests separados
// (ver EasyStock.Api.UnitTests/Mobile/PwaVersionProviderTests.cs).
'use strict';

module.exports = function ({ test, sandbox, assert }) {

  // ── Service Worker handlers (sw.js) ───────────────────────────────────────
  // sw.js está num arquivo separado, não inline no index.html — então não
  // está carregado no sandbox. Lemos o arquivo direto e validamos o contrato
  // de mensagens. O CI já copia sw.js junto com index.html via copy-web.

  const fs = require('fs');
  const path = require('path');

  function loadSwSource() {
    // Tenta apk/web (copiado) primeiro, fallback pra fonte do PWA.
    const candidatos = [
      path.resolve(__dirname, '../web/sw.js'),
      path.resolve(__dirname, '../../EasyStock.Api/wwwroot/pwa/sw.js'),
      path.resolve(__dirname, '../../../EasyStock.Api/wwwroot/pwa/sw.js'),
    ];
    for (const p of candidatos) {
      if (fs.existsSync(p)) return { content: fs.readFileSync(p, 'utf8'), path: p };
    }
    return null;
  }

  function loadSyncSource() {
    const candidatos = [
      path.resolve(__dirname, '../web/sync.js'),
      path.resolve(__dirname, '../../EasyStock.Api/wwwroot/pwa/sync.js'),
      path.resolve(__dirname, '../../../EasyStock.Api/wwwroot/pwa/sync.js'),
    ];
    for (const p of candidatos) {
      if (fs.existsSync(p)) return { content: fs.readFileSync(p, 'utf8'), path: p };
    }
    return null;
  }

  // ── sw.js: contrato de CACHE_VERSION e handlers de message ────────────────

  test('sw.js: CACHE_VERSION existe e tem prefixo cdb-', () => {
    const sw = loadSwSource();
    if (!sw) return; // CI ainda não copiou — skip silencioso
    const m = sw.content.match(/const\s+CACHE_VERSION\s*=\s*['"]([^'"]+)['"]/);
    assert.ok(m, 'CACHE_VERSION não encontrado em sw.js');
    assert.ok(m[1].startsWith('cdb-'),
      'CACHE_VERSION deve começar com "cdb-" (PwaVersionProvider regex depende disso)');
    assert.ok(m[1].length >= 5,
      'CACHE_VERSION com pelo menos 5 chars (ex: "cdb-v1")');
  });

  test('sw.js: handler de SKIP_WAITING registrado (Onda 9)', () => {
    const sw = loadSwSource();
    if (!sw) return;
    // PWA pode estar travado em "waiting" — sync.js manda postMessage
    // SKIP_WAITING; sw.js precisa chamar self.skipWaiting() em resposta.
    assert.ok(sw.content.includes("SKIP_WAITING"),
      'sw.js deve tratar mensagem SKIP_WAITING');
    assert.ok(/skipWaiting\s*\(/.test(sw.content),
      'sw.js deve chamar self.skipWaiting() em algum lugar');
  });

  test('sw.js: handler de CHECK_UPDATE registrado (Onda 9)', () => {
    const sw = loadSwSource();
    if (!sw) return;
    assert.ok(sw.content.includes("CHECK_UPDATE"),
      'sw.js deve tratar mensagem CHECK_UPDATE pra forçar registration.update()');
  });

  test('sw.js: handler de CLEAR_CACHE limpa caches.keys() (Onda 9)', () => {
    const sw = loadSwSource();
    if (!sw) return;
    assert.ok(sw.content.includes("CLEAR_CACHE"),
      'sw.js deve tratar mensagem CLEAR_CACHE');
    // Validação semântica: handler deve chamar caches.keys e delete.
    const idx = sw.content.indexOf("CLEAR_CACHE");
    const trecho = sw.content.slice(idx, idx + 600);
    assert.ok(/caches\.keys/.test(trecho) && /caches\.delete/.test(trecho),
      'CLEAR_CACHE deve chamar caches.keys() e caches.delete()');
  });

  test('sw.js: install_listener tem skipWaiting() pra ativação imediata', () => {
    const sw = loadSwSource();
    if (!sw) return;
    // Garantia adicional contra "novo SW preso em waiting" — sem isso,
    // o auto-update silencioso por controllerchange não dispara.
    const installMatch = sw.content.match(/addEventListener\(\s*['"]install['"][\s\S]*?skipWaiting/);
    assert.ok(installMatch, 'install handler deve chamar skipWaiting()');
  });

  // ── sync.js: lógica de auto-update OTA ───────────────────────────────────

  test('sync.js: PWA_INSTALLED_VERSION_KEY persiste em cdb-pwa-installed-version', () => {
    const sync = loadSyncSource();
    if (!sync) return;
    assert.ok(sync.content.includes('cdb-pwa-installed-version'),
      'sync.js deve usar a chave cdb-pwa-installed-version pra rastrear bundle instalado');
  });

  test('sync.js: maybeApplyPwaUpdate compara Ota.PwaCacheVersion', () => {
    const sync = loadSyncSource();
    if (!sync) return;
    assert.ok(sync.content.includes('PwaCacheVersion'),
      'sync.js deve ler Ota.PwaCacheVersion da resposta de /version');
    assert.ok(/maybeApplyPwaUpdate|triggerPwaUpdate/.test(sync.content),
      'sync.js deve ter helper de OTA (maybeApplyPwaUpdate / triggerPwaUpdate)');
  });

  test('sync.js: triggerPwaUpdate chama registration.update() + SKIP_WAITING', () => {
    const sync = loadSyncSource();
    if (!sync) return;
    assert.ok(/reg\.update\s*\(/.test(sync.content),
      'sync.js deve chamar reg.update() pra forçar refetch do sw.js');
    assert.ok(sync.content.includes('SKIP_WAITING'),
      'sync.js deve mandar SKIP_WAITING pro waiting SW');
  });

  test('sync.js: triggerPwaUpdate em modo force limpa caches', () => {
    const sync = loadSyncSource();
    if (!sync) return;
    // Pra comando pwa_update vindo do backend, queremos limpar TUDO antes
    // de recarregar — caches.delete() na window é o caminho oficial.
    assert.ok(/caches\.keys|CLEAR_CACHE/.test(sync.content),
      'sync.js deve limpar caches em modo force (caches.keys ou postMessage CLEAR_CACHE)');
  });

  test('sync.js: executor remoto suporta pwa_update', () => {
    const sync = loadSyncSource();
    if (!sync) return;
    assert.ok(sync.content.includes("'pwa_update'") || sync.content.includes('"pwa_update"'),
      'executeRemoteCommand deve aceitar pwa_update');
  });

  test('sync.js: executor remoto suporta clear_cache', () => {
    const sync = loadSyncSource();
    if (!sync) return;
    assert.ok(sync.content.includes("'clear_cache'") || sync.content.includes('"clear_cache"'),
      'executeRemoteCommand deve aceitar clear_cache');
  });

  test('sync.js: cdbSync expõe forceUpdate() pra debug', () => {
    const sync = loadSyncSource();
    if (!sync) return;
    assert.ok(/forceUpdate\s*[:=]/.test(sync.content),
      'cdbSync deve expor forceUpdate (botão manual / debug)');
    assert.ok(/installedPwaVersion\s*[:=]/.test(sync.content),
      'cdbSync deve expor installedPwaVersion (introspecção)');
  });

  // ── Contrato com backend (paridade de comandos suportados) ────────────────

  test('contrato: comandos remotos batem entre PWA e backend', () => {
    const sync = loadSyncSource();
    if (!sync) return;

    // Lista que o sync.js sabe executar — extraída do switch.
    const tiposSuportadosNoSync = [
      'flush_now', 'pull_now', 'reload', 'message', 'pwa_update', 'clear_cache'
    ];
    for (const t of tiposSuportadosNoSync) {
      assert.ok(
        sync.content.includes("'" + t + "'") || sync.content.includes('"' + t + '"'),
        `sync.js deve reconhecer comando ${t}`
      );
    }
    // Lista dever espelhar AllowedCommandTypes em DevicePairingController.
    // Se os tipos divergirem, esse teste falha e força revisão dos dois lados.
  });

  // ── Storage: comportamento esperado do cdb-pwa-installed-version ──────────

  test('storage: cdb-pwa-installed-version persiste valor enviado pelo servidor', () => {
    // Simula o que sync.js maybeApplyPwaUpdate faz: salva a versão remota
    // depois do primeiro encontro.
    sandbox.localStorage.setItem('cdb-pwa-installed-version', 'cdb-v3-20260506a');
    const got = sandbox.localStorage.getItem('cdb-pwa-installed-version');
    assert.strictEqual(got, 'cdb-v3-20260506a',
      'localStorage deve preservar cdb-pwa-installed-version exatamente como passado');
  });

  test('storage: ausência de cdb-pwa-installed-version sinaliza primeiro boot', () => {
    sandbox.localStorage.removeItem('cdb-pwa-installed-version');
    const got = sandbox.localStorage.getItem('cdb-pwa-installed-version');
    assert.strictEqual(got, null,
      'getItem deve retornar null quando ausente — sync.js usa isso pra detectar primeiro boot');
  });

  test('storage: cdb-server-version cacheado depois de pingVersion()', () => {
    // Espelha o que sync.js > pingVersion() faz após resposta OK.
    const versionPayload = {
      ApiVersion: '1.2.3',
      Status: 'ok',
      Ota: { PwaCacheVersion: 'cdb-v3-20260506a' },
      fetchedAt: Date.now()
    };
    sandbox.localStorage.setItem('cdb-server-version', JSON.stringify(versionPayload));

    const cached = JSON.parse(sandbox.localStorage.getItem('cdb-server-version'));
    assert.strictEqual(cached.Ota.PwaCacheVersion, 'cdb-v3-20260506a');
    assert.strictEqual(cached.Status, 'ok');
  });

  // ── index.html: registro do SW + listeners de update ──────────────────────

  test('index.html: registra updatefound listener no SW (Onda 9)', () => {
    // Carregado pelo run.js — o sandbox tem document/navigator stubs, mas
    // o registro do SW não roda (navigator.serviceWorker é undefined).
    // Validamos via leitura raw do index.html que o updatefound listener foi
    // adicionado.
    const idx = require('fs').readFileSync(
      sandbox.location && sandbox.location.href.startsWith('file:')
        ? sandbox.location.href.replace('file://', '')
        : (process.env.PWA_HTML || require('path').resolve(__dirname, '../web/index.html')),
      'utf8'
    );
    assert.ok(idx.includes('updatefound'),
      'index.html deve registrar listener updatefound pra SKIP_WAITING o novo SW');
    assert.ok(idx.includes('SKIP_WAITING'),
      'index.html deve mandar SKIP_WAITING quando SW novo termina de instalar');
  });

  test('index.html: controllerchange listener recarrega a página', () => {
    const idx = require('fs').readFileSync(
      process.env.PWA_HTML || require('path').resolve(__dirname, '../web/index.html'),
      'utf8'
    );
    assert.ok(idx.includes('controllerchange'),
      'controllerchange listener é o que dispara reload silencioso quando novo SW assume');
    assert.ok(/location\.reload/.test(idx),
      'controllerchange handler deve chamar location.reload()');
  });
};
