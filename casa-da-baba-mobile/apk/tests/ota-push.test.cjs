'use strict';
const { test, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const crypto = require('node:crypto');
const ota = require('../scripts/ota-package.cjs');
const push = require('../scripts/ota-push.cjs');
const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'cdb-push-test-'));
const pair = crypto.generateKeyPairSync('rsa', { modulusLength: 3072, publicKeyEncoding: { type: 'spki', format: 'der' }, privateKeyEncoding: { type: 'pkcs8', format: 'pem' } });
const key = crypto.createPublicKey({ key: pair.publicKey, format: 'der', type: 'spki' });
const privateFile = path.join(temporary, 'ota-private.pem'), publicFile = path.join(temporary, 'ota-public.der');
fs.writeFileSync(privateFile, pair.privateKey); fs.writeFileSync(publicFile, pair.publicKey);
after(() => {
  const resolved = fs.realpathSync(temporary);
  assert.ok(resolved.startsWith(fs.realpathSync(os.tmpdir()) + path.sep) && path.basename(resolved).startsWith('cdb-push-test-'));
  fs.rmSync(resolved, { recursive: true, force: true });
});
function fixture() {
  const directory = fs.mkdtempSync(path.join(temporary, 'case-'));
  const account = { type: 'service_account', project_id: 'cdb-test-project', client_email: 'ota@cdb-test-project.iam.gserviceaccount.com', private_key: pair.privateKey, private_key_id: 'test-key', token_uri: push.TOKEN_URL };
  const app = { project_info: { project_id: account.project_id }, client: [{ client_info: { android_client_info: { package_name: ota.APP } } }] };
  const hash = 'a'.repeat(64);
  const payload = { schemaVersion: 1, appId: ota.APP, channel: 'piloto', sequence: 1, bundleVersion: '0.3.1', nativeVersionCode: require('../scripts/ota-contract.cjs').NATIVE_VERSION_CODE, dataSchemaVersion: require('../scripts/ota-contract.cjs').DATA_SCHEMA, url: ota.BASE + hash + '/bundle.zip', sizeBytes: 123, sha256: hash };
  const options = { credentials: path.join(directory, 'service.json'), appConfig: path.join(directory, 'google-services.json'), release: path.join(directory, 'release-1.json'), publicKey: publicFile };
  fs.writeFileSync(options.credentials, JSON.stringify(account));
  fs.writeFileSync(options.appConfig, JSON.stringify(app));
  fs.writeFileSync(options.release, JSON.stringify(ota.sign(payload, privateFile, key)));
  return { options, account, app, payload, directory };
}
test('JWT OAuth usa RS256, escopo FCM e validade máxima de uma hora', () => {
  const { account } = fixture();
  const jwt = push.assertion(account, 123456);
  const [header, payload, signature] = jwt.split('.');
  assert.deepEqual(JSON.parse(Buffer.from(header, 'base64url')), { alg: 'RS256', typ: 'JWT', kid: 'test-key' });
  assert.deepEqual(JSON.parse(Buffer.from(payload, 'base64url')), { iss: account.client_email, scope: push.SCOPE, aud: push.TOKEN_URL, iat: 123456, exp: 127056 });
  assert.ok(crypto.verify('RSA-SHA256', Buffer.from(header + '.' + payload), key, Buffer.from(signature, 'base64url')));
});
test('push leva somente metadados públicos, prioridade normal e TTL24h', () => {
  const { payload } = fixture();
  assert.deepEqual(push.message(payload, true), { validate_only: true, message: { topic: 'casa-da-baba-piloto-updates', data: { kind: 'ota-update', sequence: '1', bundleVersion: '0.3.1' }, android: { priority: 'normal', ttl: '86400s' } } });
});
test('validate_only é padrão e valida sem notificar usando transporte simulado', async () => {
  const { options } = fixture(), calls = [];
  const result = await push.sendUpdate(options, async (url, body, headers) => {
    calls.push({ url, body, headers });
    return calls.length === 1 ? { access_token: 'test-token', token_type: 'Bearer' } : { name: 'validated' };
  });
  assert.equal(calls.length, 2);
  assert.equal(calls[0].url, push.TOKEN_URL);
  assert.equal(new URLSearchParams(calls[0].body).get('grant_type'), 'urn:ietf:params:oauth:grant-type:jwt-bearer');
  assert.equal(calls[1].url, 'https://fcm.googleapis.com/v1/projects/cdb-test-project/messages:send');
  assert.equal(calls[1].headers.Authorization, 'Bearer test-token');
  assert.equal(JSON.parse(calls[1].body).validate_only, true);
  assert.equal(result.status, 'validated');
  assert.equal(JSON.stringify(result).includes('test-token'), false);
});
test('envio explícito só sinaliza aceitação pelo FCM e não entrega ao aparelho', async () => {
  const { options } = fixture(); let calls = 0;
  const result = await push.sendUpdate({ ...options, validateOnly: false }, async (url, body) => {
    if (++calls === 1) return { access_token: 'test-token' };
    assert.equal(JSON.parse(body).validate_only, false);
    return { name: 'projects/cdb-test-project/messages/example' };
  });
  assert.equal(result.status, 'accepted');
});
test('projeto divergente, manifesto adulterado e OAuth arbitrário falham antes da rede', async () => {
  const { options, app, account } = fixture();
  let calls = 0; const request = async () => { calls++; };
  app.project_info.project_id = 'wrong-project'; fs.writeFileSync(options.appConfig, JSON.stringify(app));
  await assert.rejects(push.sendUpdate(options, request), /Projeto/);
  app.project_info.project_id = account.project_id; fs.writeFileSync(options.appConfig, JSON.stringify(app));
  const envelope = JSON.parse(fs.readFileSync(options.release)); envelope.signatureBase64 = Buffer.alloc(384).toString('base64');
  fs.writeFileSync(options.release, JSON.stringify(envelope));
  await assert.rejects(push.sendUpdate(options, request), /Assinatura/);
  account.token_uri = 'https://example.invalid/token'; fs.writeFileSync(options.credentials, JSON.stringify(account));
  await assert.rejects(push.sendUpdate(options, request), /Endpoint/);
  assert.equal(calls, 0);
});
test('credencial dentro de repositório é recusada e erro de envio não vira sucesso', async () => {
  const { options, directory } = fixture();
  fs.writeFileSync(path.join(directory, '.git'), 'fixture');
  assert.throws(() => push.configuration(options.credentials, options.appConfig), /fora de repositórios/);
  fs.unlinkSync(path.join(directory, '.git'));
  await assert.rejects(push.sendUpdate(options, async () => { throw new Error('falha de rede simulada'); }), /falha de rede/);
});
function apkRelease(overrides = {}) {
  return { schemaVersion: 1, appId: ota.APP, version: '0.8.8', versionCode: 15, sha256: 'b'.repeat(64), fileName: 'casa-da-baba-15-bbbbbbbbbbbb.apk',
    url: 'https://easystok.online/downloads/casa-da-baba/casa-da-baba-15-bbbbbbbbbbbb.apk', publishedAt: '2026-11-03T23:00:00Z', notes: [], ...overrides };
}
test('aviso de APK novo é visível no Android, vai ao tópico de updates e leva só dados públicos', () => {
  assert.deepEqual(push.apkMessage(apkRelease(), true), { validate_only: true, message: {
    topic: 'casa-da-baba-piloto-updates',
    notification: { title: 'Casa da Baba 0.8.8 disponível', body: 'Abra o app e toque em Atualizar para instalar sem desinstalar. Se já estiver nesta versão, ignore.' },
    data: { kind: 'apk-update', version: '0.8.8', versionCode: '15' },
    android: { priority: 'high', ttl: '604800s', collapse_key: 'apk-update', notification: { channel_id: 'cdb-operacao', tag: 'apk-update' } } } });
});
test('aviso de APK recusa pacote, versão ou endereço fora da distribuição', () => {
  for (const bad of [{ appId: 'com.outro.app' }, { versionCode: 0 }, { versionCode: '15' }, { version: '0.8' }, { url: 'https://example.invalid/app.apk' }, { url: 'https://easystok.online/downloads/casa-da-baba/../x.apk' }])
    assert.throws(() => push.apkMessage(apkRelease(bad), true), /APK/);
});
test('aviso de APK usa validate_only por padrão e o mesmo OAuth do OTA', async () => {
  const { options, directory } = fixture(), calls = [];
  const file = path.join(directory, 'latest.json'); fs.writeFileSync(file, JSON.stringify(apkRelease()));
  const result = await push.sendApkUpdate({ credentials: options.credentials, appConfig: options.appConfig, apkRelease: file }, async (url, body) => {
    calls.push({ url, body });
    return calls.length === 1 ? { access_token: 'test-token' } : { name: 'validated' };
  });
  assert.equal(calls.length, 2);
  assert.equal(JSON.parse(calls[1].body).validate_only, true);
  assert.equal(JSON.parse(calls[1].body).message.data.kind, 'apk-update');
  assert.deepEqual(result, { status: 'validated', versionCode: 15, projectId: 'cdb-test-project', topic: push.TOPIC });
});
test('apk-deploy valida o push antes de publicar e só envia depois de ativar o latest', () => {
  const script = fs.readFileSync(path.resolve(__dirname, '../../../scripts/deploy/apk-deploy.ps1'), 'utf8');
  const preflight = script.indexOf('--apk-release'), stage = script.indexOf("Invoke-ApkRemote 'stage'");
  const send = script.lastIndexOf('$apkPushMode'), activate = script.indexOf("Invoke-ApkRemote 'activate'");
  assert.ok(preflight > 0 && stage > 0 && preflight < stage, 'preflight antes do staging');
  assert.ok(send > activate && activate > 0, 'envio depois do activate');
  assert.match(script, /\[switch\]\$SkipPush/);
  assert.match(script, /\[switch\]\$PushValidateOnly/);
  assert.match(script, /if \(\$PrepareOnly\) \{ return \}[\s\S]*--validate-only/);
});
