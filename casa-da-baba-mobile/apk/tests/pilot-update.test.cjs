'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { validateManifest } = require('../pilot.js');
const config = { appId: 'com.casadababa.piloto', versionCode: 1 };
const manifest = {
  schemaVersion: 1, appId: config.appId, versionCode: 2, version: '0.1.1',
  url: 'https://easystok.online/downloads/casa-da-baba/casa-da-baba-2.apk',
  sha256: 'a'.repeat(64),
};
test('oferece versão superior e não oferece downgrade ou reinstalação', () => {
  assert.equal(validateManifest(manifest, config).available, true);
  assert.equal(validateManifest({ ...manifest, versionCode: 1 }, config).available, false);
  assert.equal(validateManifest(manifest, { ...config, versionCode: 3 }).available, false);
});
test('rejeita pacote de outro aplicativo e metadados corrompidos', () => {
  for (const extra of [{ appId: 'com.casadababa.app' }, { versionCode: 2.5 }, { versionCode: -1 }, { sha256: '' }, { schemaVersion: 0 }, { version: 2 }]) {
    assert.throws(() => validateManifest({ ...manifest, ...extra }, config));
  }
});
test('não abre links de atualização fora da distribuição aprovada', () => {
  for (const url of ['http://easystok.online/downloads/casa-da-baba/a.apk', 'https://evil.example/a.apk', 'https://easystok.online.evil.example/downloads/casa-da-baba/a.apk', 'https://user@easystok.online/downloads/casa-da-baba/a.apk', 'https://easystok.online/downloads/a.apk', 'https://easystok.online/downloads/casa-da-baba/a.apk?redirect=evil', 'https://easystok.online/downloads/casa-da-baba/a.apk#fragment', 'https://easystok.online/downloads/casa-da-baba/../../a.apk', 'https://easystok.online/downloads/casa-da-baba/a%2Ffile.apk']) {
    assert.throws(() => validateManifest({ ...manifest, url }, config));
  }
});
