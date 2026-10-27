'use strict';
const { test, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const crypto = require('node:crypto');
const { spawnSync } = require('node:child_process');
const ota = require('../scripts/ota-package.cjs');
const project = path.resolve(__dirname, '../../..');
const remote = path.join(project, 'scripts/deploy/ota-publish.py');
const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'cdb-ota-test-'));
const keys = crypto.generateKeyPairSync('rsa', { modulusLength: 3072, publicKeyEncoding: { type: 'spki', format: 'der' }, privateKeyEncoding: { type: 'pkcs8', format: 'pem' } });
const privateFile = path.join(temporary, 'private.pem'), publicFile = path.join(temporary, 'public.der');
fs.writeFileSync(privateFile, keys.privateKey); fs.writeFileSync(publicFile, keys.publicKey);
const key = crypto.createPublicKey({ key: keys.publicKey, type: 'spki', format: 'der' });
after(() => {
  const actual = fs.realpathSync(temporary);
  const expectedParent = fs.realpathSync(os.tmpdir()) + path.sep;
  assert.ok(actual.startsWith(expectedParent) && path.basename(actual).startsWith('cdb-ota-test-'));
  fs.rmSync(actual, { recursive: true, force: true });
});
function fixture(version = '0.3.0') {
  const dir = fs.mkdtempSync(path.join(temporary, 'web-'));
  fs.writeFileSync(path.join(dir, 'index.html'), '<html>' + version + '</html>');
  fs.writeFileSync(path.join(dir, 'bundle.json'), JSON.stringify({ schemaVersion: 1, appId: ota.APP, channel: 'piloto', bundleVersion: version, nativeVersionCode: require('../package.json').versionCode, dataSchemaVersion: 2 }));
  return dir;
}
function build(version, sequence, content = '') {
  const web = fixture(version);
  if (content) fs.appendFileSync(path.join(web, 'index.html'), content);
  const output = fs.mkdtempSync(path.join(temporary, 'distribution-'));
  const payload = ota.main(['build', '--web-dir', web, '--version', version, '--sequence', String(sequence), '--output', output, '--private-key', privateFile, '--public-key', publicFile]);
  return { output, payload, bundle: path.join(output, 'bundle.zip'), release: path.join(output, 'release-' + sequence + '.json') };
}
function read(file) { return JSON.parse(fs.readFileSync(file)); }
function run(root, stage, mode, success = true) {
  const result = spawnSync(process.env.PYTHON || 'python3', [remote, root, stage, mode], { encoding: 'utf8', env: process.env });
  if (success) assert.equal(result.status, 0, result.error?.message || result.stderr);
  else assert.notEqual(result.status, 0, 'A publicação deveria falhar.');
  return result;
}
function staging(root, release) {
  const stage = path.join(root, '.incoming-' + crypto.randomBytes(16).toString('hex'));
  run(root, stage, 'stage');
  for (const [source, name] of [[release.bundle, 'bundle.zip'], [release.release, path.basename(release.release)], [publicFile, 'ota-public-key.der']]) fs.copyFileSync(source, path.join(stage, name));
  return stage;
}
function current(root) { return ota.verify(read(path.join(root, 'latest.json')), key); }

test('ZIP determinístico, manifesto e integridade assinada', () => {
  const repository = fs.mkdtempSync(path.join(temporary, 'git-fixture-'));
  fs.writeFileSync(path.join(repository, '.git'), 'fixture');
  assert.throws(() => ota.initializeKey(path.join(repository, 'private.pem'), path.join(repository, 'public.der')), /fora de repositórios/);
  const web = fixture(), zip = ota.zipWeb(web, '0.3.0');
  assert.deepEqual(zip, ota.zipWeb(web, '0.3.0'));
  const release = build('0.3.0', 1);
  assert.equal(ota.verify(read(release.release), key).sequence, 1);
  ota.checkBundle(release.bundle, release.payload);
  const forged = read(release.release), payload = JSON.parse(Buffer.from(forged.payloadBase64, 'base64'));
  payload.sequence = 2; forged.payloadBase64 = Buffer.from(JSON.stringify(payload)).toString('base64');
  assert.throws(() => ota.verify(forged, key), /Assinatura/);
  assert.throws(() => ota.verify(read(release.release), key, 1), /replay/);
  fs.appendFileSync(release.bundle, 'x');
  assert.throws(() => ota.checkBundle(release.bundle, release.payload), /Tamanho/);
});
test('contrato rejeita URL, identidade, esquema e metadados incompatíveis', () => {
  const release = build('0.3.0', 1);
  for (const change of [{ url: release.payload.url + '?x=1' }, { appId: 'outra.empresa' }, { dataSchemaVersion: 1 }, { nativeVersionCode: 5 }, { sequence: 0 }, { bundleVersion: '../escape' }]) {
    assert.throws(() => ota.validate({ ...release.payload, ...change }));
  }
  const web = fixture();
  const metadata = read(path.join(web, 'bundle.json')); metadata.dataSchemaVersion = 3;
  fs.writeFileSync(path.join(web, 'bundle.json'), JSON.stringify(metadata));
  assert.throws(() => ota.zipWeb(web, '0.3.0'), /bundle.json/);
});
test('distribuidor gera a versao nativa atual e recusa conteúdo para APK anterior', () => {
  const release = build('0.4.0', 10);
  assert.equal(release.payload.nativeVersionCode, require('../package.json').versionCode);
  const legacy = { ...release.payload, nativeVersionCode: 3 };
  assert.throws(() => ota.sign(legacy, privateFile, key), /incompatível/);
});

test('rollback cria sequência nova preservando exatamente os bytes e hash anteriores', () => {
  const old = build('0.3.0', 1), output = fs.mkdtempSync(path.join(temporary, 'rollback-'));
  const rollback = ota.main(['rollback', '--release', old.release, '--bundle', old.bundle, '--sequence', '3', '--output', output, '--private-key', privateFile, '--public-key', publicFile]);
  assert.equal(rollback.sequence, 3); assert.equal(rollback.bundleVersion, '0.3.0'); assert.equal(rollback.sha256, old.payload.sha256);
  assert.deepEqual(fs.readFileSync(path.join(output, 'bundle.zip')), fs.readFileSync(old.bundle));
  assert.throws(() => ota.main(['rollback', '--release', old.release, '--bundle', old.bundle, '--sequence', '1', '--output', output, '--private-key', privateFile, '--public-key', publicFile]), /sequência/);
});
test('publicação local preserva versões, separa instalação/ativação e permite rollback com sequência crescente', () => {
  const root = fs.mkdtempSync(path.join(temporary, 'server-'));
  const first = build('0.3.0', 1), next = build('0.3.1', 2);
  const stage1 = staging(root, first); run(root, stage1, 'install');
  assert.equal(fs.existsSync(path.join(root, 'latest.json')), false);
  run(root, stage1, 'activate'); assert.equal(current(root).sequence, 1);
  const stage2 = staging(root, next); run(root, stage2, 'install');
  assert.equal(current(root).sequence, 1);
  run(root, stage2, 'activate'); assert.equal(current(root).sequence, 2);
  assert.equal(ota.verify(read(path.join(root, 'previous.json')), key).sequence, 1);
  const output = fs.mkdtempSync(path.join(temporary, 'rollback-'));
  const payload = ota.main(['rollback', '--release', first.release, '--bundle', first.bundle, '--sequence', '3', '--output', output, '--private-key', privateFile, '--public-key', publicFile]);
  const stage3 = staging(root, { output, payload, bundle: path.join(output, 'bundle.zip'), release: path.join(output, 'release-3.json') });
  run(root, stage3, 'install'); run(root, stage3, 'activate');
  assert.equal(current(root).sequence, 3); assert.equal(current(root).sha256, first.payload.sha256);
  for (const sequence of [1, 2, 3]) assert.ok(fs.existsSync(path.join(root, 'release-' + sequence + '.json')));
  for (const release of [first, next]) assert.deepEqual(fs.readFileSync(path.join(root, release.payload.sha256, 'bundle.zip')), fs.readFileSync(release.bundle));
  // Staging novo: o usado já foi removido pela ativação, e a recusa tem de vir do replay.
  run(root, staging(root, next), 'activate', false); run(root, staging(root, first), 'install', false);
  assert.equal(current(root).sequence, 3);
});
test('#2066 staging sai do diretório público depois da ativação ou do descarte', () => {
  const root = fs.mkdtempSync(path.join(temporary, 'server-'));
  const stage = staging(root, build('0.3.0', 1));
  run(root, stage, 'install');
  assert.ok(fs.existsSync(stage), 'activate ainda precisa da release em staging');
  run(root, stage, 'activate');
  assert.equal(fs.existsSync(stage), false, 'activate remove o staging');
  assert.equal(current(root).sequence, 1);
  const abandoned = staging(root, build('0.3.1', 2));
  run(root, abandoned, 'discard');
  assert.equal(fs.existsSync(abandoned), false, 'discard remove staging de publicação falha');
  assert.equal(current(root).sequence, 1, 'discard não mexe em latest');
  run(root, path.join(root, 'latest.json'), 'discard', false);
  assert.ok(fs.existsSync(path.join(root, 'latest.json')));
});
test('#2066 versão nativa vem do package.json, sem número fixo no empacotador', () => {
  const manifest = require.resolve('../package.json'), packager = require.resolve('../scripts/ota-package.cjs');
  const contract = path.resolve(__dirname, '../scripts/ota-contract.cjs');
  const saved = require.cache[manifest];
  try {
    require.cache[manifest] = { id: manifest, filename: manifest, loaded: true, exports: { ...require(manifest), versionCode: 4 } };
    delete require.cache[packager]; delete require.cache[contract];
    const next = require(packager);
    const web = fixture();
    const metadata = read(path.join(web, 'bundle.json')); metadata.nativeVersionCode = 4;
    fs.writeFileSync(path.join(web, 'bundle.json'), JSON.stringify(metadata));
    const output = fs.mkdtempSync(path.join(temporary, 'native-'));
    const payload = next.main(['build', '--web-dir', web, '--version', '0.3.0', '--sequence', '1', '--output', output, '--private-key', privateFile, '--public-key', publicFile]);
    assert.equal(payload.nativeVersionCode, 4);
    assert.throws(() => next.validate({ ...payload, nativeVersionCode: 3 }), /nativa/);
    const root = fs.mkdtempSync(path.join(temporary, 'server-'));
    const stage = staging(root, { bundle: path.join(output, 'bundle.zip'), release: path.join(output, 'release-1.json') });
    run(root, stage, 'install'); run(root, stage, 'activate');
  } finally {
    require.cache[manifest] = saved;
    delete require.cache[packager]; delete require.cache[contract];
  }
});
test('#2066 esquema de dados do empacotador é o mesmo do APK', () => {
  const java = fs.readFileSync(path.resolve(__dirname, '../android/app/src/main/java/com/casadababa/app/updater/UpdatePolicy.java'), 'utf8');
  assert.equal(Number(/DATA_SCHEMA\s*=\s*(\d+)/.exec(java)[1]), require('../scripts/ota-contract.cjs').DATA_SCHEMA);
});
test('publicação recusa adulteração, troca de chave, sequência reutilizada e concorrência sem mudar latest', () => {
  const root = fs.mkdtempSync(path.join(temporary, 'server-')), first = build('0.3.0', 1);
  const initial = staging(root, first); run(root, initial, 'install'); run(root, initial, 'activate');
  const next = build('0.3.1', 2);
  const tamperedZip = staging(root, next); fs.appendFileSync(path.join(tamperedZip, 'bundle.zip'), 'x');
  run(root, tamperedZip, 'install', false);
  const tamperedSignature = staging(root, next), signaturePath = path.join(tamperedSignature, 'release-2.json'), envelope = read(signaturePath);
  envelope.signatureBase64 = Buffer.alloc(384).toString('base64'); fs.writeFileSync(signaturePath, JSON.stringify(envelope));
  run(root, tamperedSignature, 'install', false);
  const wrongKey = staging(root, next); fs.writeFileSync(path.join(wrongKey, 'ota-public-key.der'), 'outra chave');
  run(root, wrongKey, 'install', false);
  const collision = staging(root, build('0.3.9', 1)); run(root, collision, 'install', false);
  const reusedVersion = staging(root, build('0.3.0', 2, 'conteudo diferente')); run(root, reusedVersion, 'install', false);
  const concurrent = staging(root, next); fs.mkdirSync(path.join(root, '.publish-lock'));
  run(root, concurrent, 'install', false); fs.rmdirSync(path.join(root, '.publish-lock'));
  assert.equal(current(root).sequence, 1);
  assert.equal(fs.existsSync(path.join(root, 'release-2.json')), false);
});
