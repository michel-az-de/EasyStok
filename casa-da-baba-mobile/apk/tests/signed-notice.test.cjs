'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const notices = require('../scripts/signed-notice.cjs');
const { privateKey, publicKey } = crypto.generateKeyPairSync('rsa', { modulusLength: 3072 });
const target = 'a'.repeat(64);
const input = { title: 'Aviso da Casa da Baba', body: 'Teste de confirmação, sem dados de clientes.', screen: 'home' };
const decode = envelope => JSON.parse(Buffer.from(envelope.payloadBase64, 'base64'));
test('API e Android confiam na mesma chave pública incluída no deploy', () => {
  const android = fs.readFileSync(path.resolve(__dirname, '../android/app/src/main/assets/ota-public-key.der'));
  const api = fs.readFileSync(path.resolve(__dirname, '../../../EasyStock.Api/Mobile/Security/push-publisher.der'));
  assert.deepEqual(api, android);
});
test('assinatura protege bytes, identidade, nonce e destino', () => {
  const payload = notices.notice(input, target);
  const other = notices.notice(input, target);
  assert.notEqual(payload.id, other.id); assert.notEqual(payload.nonce, other.nonce);
  const envelope = notices.sign(payload, privateKey);
  assert.equal(crypto.verify('RSA-SHA256', Buffer.from(envelope.payloadBase64, 'base64'), publicKey, Buffer.from(envelope.signatureBase64, 'base64')), true);
  assert.equal(crypto.verify('RSA-SHA256', Buffer.from(JSON.stringify({ ...payload, target: 'b'.repeat(64) })), publicKey, Buffer.from(envelope.signatureBase64, 'base64')), false);
});
test('não aceita broadcast, tela arbitrária ou aviso vazio', () => {
  assert.throws(() => notices.notice(input, '*'));
  assert.throws(() => notices.notice({ ...input, screen: 'https://malicious.invalid' }, target));
  assert.throws(() => notices.notice({ ...input, body: '' }, target));
});
test('FCM usa data-only e valida tamanho e expiração antes do envio', () => {
  const payload = notices.notice(input, target);
  const body = notices.fcm(notices.sign(payload, privateKey), 'test-token', payload.expiresAt);
  assert.equal(body.validate_only, true);
  assert.equal(body.message.notification, undefined);
  assert.equal(body.message.topic, undefined);
  assert.equal(body.message.data.kind, 'signed-notice');
  assert.throws(() => notices.fcm({ payloadBase64: 'a'.repeat(3800), signatureBase64: 'a' }, 't', payload.expiresAt));
  assert.throws(() => notices.fcm({}, 't', Date.now() - 1));
});
function scenario(mode) {
  const calls = []; let message;
  const credentials = { project_id: 'casa-da-baba-app', client_email: 'test@example.iam.gserviceaccount.com', private_key: privateKey.export({ format: 'pem', type: 'pkcs8' }) };
  return { calls, dependencies: {
    configuration: () => credentials,
    api: async (route, envelope) => {
      const payload = decode(envelope); calls.push({ route, payload });
      if (route === 'dispatch') { message = payload; return { messageId: payload.id, targets: [{ installationId: mode === 'wrong-target' ? 'b'.repeat(64) : target, token: 'private-fcm-token' }] }; }
      if (mode === 'receipt-write-fails') throw new Error('offline');
      return { recorded: true };
    },
    google: async (url, body) => {
      calls.push({ url });
      if (url.includes('oauth2')) return { access_token: 'private-oauth-token' };
      assert.equal(JSON.parse(body).message.token, 'private-fcm-token');
      assert.equal(decode(JSON.parse(body).message.data).id, message.id);
      if (mode === 'lost-response') throw new Error('lost');
      return { name: 'projects/casa-da-baba-app/messages/test-provider-id' };
    }
  } };
}
const options = { key: privateKey, target, notice: input, send: true };
test('preparação não publica nem envia por padrão', async () => {
  const s = scenario(); const result = await notices.send({ ...options, send: false }, s.dependencies);
  assert.equal(result.status, 'prepared'); assert.equal(s.calls.length, 0);
});
test('aceite do Firebase não equivale a entrega; tokens nunca aparecem no resultado', async () => {
  const s = scenario(); const result = await notices.send(options, s.dependencies);
  assert.equal(result.status, 'accepted'); assert.equal(result.delivery, 'unconfirmed'); assert.equal(result.recorded, true);
  assert.equal(s.calls.at(-1).payload.status, 'accepted');
  assert.doesNotMatch(JSON.stringify(result), /private-fcm-token|private-oauth-token/);
});
test('destinatário divergente impede envio FCM', async () => {
  const s = scenario('wrong-target'); await assert.rejects(notices.send(options, s.dependencies), /destinatário/);
  assert.equal(s.calls.filter(c => c.url?.includes('fcm.googleapis')).length, 0);
});
test('perda de resposta preserva ID, não repete envio e não afirma entrega', async () => {
  const s = scenario('lost-response'); const result = await notices.send(options, s.dependencies);
  assert.equal(result.status, 'unconfirmed'); assert.ok(result.messageId);
  assert.equal(s.calls.filter(c => c.url?.includes('fcm.googleapis')).length, 1);
  assert.equal(s.calls.filter(c => c.route === 'control').length, 0, 'Resposta perdida não é prova de falha do provedor.');
});
test('falha ao registrar aceite preserva prova FCM e não duplica envio', async () => {
  const s = scenario('receipt-write-fails'); const result = await notices.send(options, s.dependencies);
  assert.equal(result.status, 'accepted'); assert.equal(result.recorded, false); assert.equal(result.delivery, 'unconfirmed');
  assert.equal(result.providerMessageId, 'projects/casa-da-baba-app/messages/test-provider-id');
});
