'use strict';
// Avisos individuais assinados. A conta FCM e a chave de publicação ficam fora do Git/APK.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const https = require('node:https');
const push = require('./ota-push.cjs');
const API = 'https://api.easystok.online/api/mobile/push/';
const DEFAULT_KEY = 'C:\\rep\\keystores\\casa-da-baba-piloto\\ota-signing-private.pem';
const check = (ok, message) => { if (!ok) throw new Error(message); };
function publisher(file) {
  const resolved = fs.realpathSync(file);
  for (let dir = path.dirname(resolved); ; dir = path.dirname(dir)) {
    check(!fs.existsSync(path.join(dir, '.git')), 'Chave privada deve permanecer fora do Git.');
    if (path.dirname(dir) === dir) break;
  }
  const key = crypto.createPrivateKey(fs.readFileSync(resolved));
  check(key.asymmetricKeyType === 'rsa' && key.asymmetricKeyDetails.modulusLength >= 3072, 'Chave de publicação inválida.');
  const expected = crypto.createPublicKey({ key: fs.readFileSync(path.resolve(__dirname, '../android/app/src/main/assets/ota-public-key.der')), format: 'der', type: 'spki' });
  check(crypto.createPublicKey(key).export({ format: 'der', type: 'spki' }).equals(expected.export({ format: 'der', type: 'spki' })), 'Chave diverge da chave pública do APK.');
  return key;
}
function sign(payload, key) {
  const bytes = Buffer.from(JSON.stringify(payload));
  return { payloadBase64: bytes.toString('base64'), signatureBase64: crypto.sign('RSA-SHA256', bytes, { key, padding: crypto.constants.RSA_PKCS1_PADDING }).toString('base64') };
}
function control(action, extra, key) {
  return sign({ kind: 'cdb-push-control-v1', action, issuedAt: Date.now(), requestId: crypto.randomUUID(), ...extra }, key);
}
function notice(input, target, now = Date.now()) {
  check(/^[a-f0-9]{64}$/.test(target), 'Informe o identificador completo da instalação.');
  check(input && typeof input.title === 'string' && input.title.trim() && input.title.length <= 100, 'Título inválido.');
  check(typeof input.body === 'string' && input.body.trim() && input.body.length <= 1200, 'Texto inválido.');
  check(['home', 'migration-guide'].includes(input.screen), 'Tela de destino inválida.');
  return { kind: 'cdb-notice-v1', id: crypto.randomUUID(), createdAt: now, expiresAt: now + 86400000,
    target, nonce: crypto.randomBytes(24).toString('base64url'), title: input.title, body: input.body, screen: input.screen };
}
function fcm(envelope, token, expiresAt, now = Date.now(), validateOnly = true) {
  check(typeof token === 'string' && token.length > 0 && token.length <= 4096, 'Token de destino inválido.');
  const ttl = Math.floor((expiresAt - now) / 1000);
  check(ttl > 0 && ttl <= 86400, 'Aviso expirado ou prazo inválido.');
  const result = { validate_only: validateOnly, message: { token,
    data: { kind: 'signed-notice', payloadBase64: envelope.payloadBase64, signatureBase64: envelope.signatureBase64 },
    android: { priority: 'high', ttl: ttl + 's' } } };
  // O token não conta no limite FCM, mas mantemos margem para nomes/metadados.
  check(Buffer.byteLength(JSON.stringify(result.message.data)) <= 3800, 'Aviso assinado excede o limite FCM. Reduza o texto.');
  return result;
}
function requestApi(route, envelope) {
  check(['control', 'dispatch'].includes(route), 'Rota de controle inválida.');
  const body = JSON.stringify(envelope);
  return new Promise((resolve, reject) => {
    const req = https.request(API + route, { method: 'POST', timeout: 20000,
      headers: { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(body) } }, response => {
      let size = 0; const parts = [];
      response.on('data', part => { size += part.length; if (size > 1048576) req.destroy(); else parts.push(part); });
      response.on('error', () => reject(new Error('Resposta da API interrompida.')));
      response.on('end', () => {
        if (response.statusCode < 200 || response.statusCode >= 300) return reject(new Error('API recusou o controle: HTTP ' + response.statusCode + '.'));
        try { resolve(JSON.parse(Buffer.concat(parts).toString('utf8'))); } catch { reject(new Error('Resposta da API inválida.')); }
      });
    });
    req.on('timeout', () => req.destroy());
    req.on('error', () => reject(new Error('Não foi possível concluir a conexão segura com a API.')));
    req.end(body);
  });
}
async function send(options, dependencies = {}) {
  const api = dependencies.api || requestApi;
  const google = dependencies.google || push.requestJson;
  const payload = notice(options.notice, options.target);
  const envelope = sign(payload, options.key);
  // Valida limite antes de persistir ou solicitar envio.
  fcm(envelope, 'preflight', payload.expiresAt);
  if (!options.send) return { status: 'prepared', target: payload.target, messageId: payload.id, title: payload.title };
  const credentials = (dependencies.configuration || push.configuration)(options.credentials, options.appConfig);
  const grant = new URLSearchParams({ grant_type: 'urn:ietf:params:oauth:grant-type:jwt-bearer', assertion: push.assertion(credentials) }).toString();
  const auth = await google(push.TOKEN_URL, grant, { 'Content-Type': 'application/x-www-form-urlencoded' });
  check(typeof auth.access_token === 'string' && auth.access_token && !/[\r\n]/.test(auth.access_token), 'OAuth inválido.');
  const dispatched = await api('dispatch', envelope);
  check(dispatched.messageId === payload.id && dispatched.targets?.length === 1 && dispatched.targets[0].installationId === payload.target,
    'API retornou destinatário divergente; nenhum push enviado.');
  let providerMessageId;
  try {
    const result = await google('https://fcm.googleapis.com/v1/projects/' + encodeURIComponent(credentials.project_id) + '/messages:send',
      JSON.stringify(fcm(envelope, dispatched.targets[0].token, payload.expiresAt, Date.now(), false)),
      { 'Content-Type': 'application/json', Authorization: 'Bearer ' + auth.access_token });
    check(typeof result.name === 'string' && result.name.startsWith('projects/' + credentials.project_id + '/messages/'), 'FCM não retornou identificador de envio.');
    providerMessageId = result.name;
  } catch {
    // Uma resposta perdida pode ter sido aceita pelo FCM. Não reenviar automaticamente.
    return { status: 'unconfirmed', messageId: payload.id, target: payload.target, detail: 'Envio sem confirmação do provedor. Consulte o relatório antes de reenviar.' };
  }
  let recorded = false;
  try {
    await api('control', control('provider', { messageId: payload.id, installationId: payload.target, status: 'accepted', providerMessageId }, options.key));
    recorded = true;
  } catch {}
  return { status: 'accepted', messageId: payload.id, target: payload.target, providerMessageId, recorded,
    delivery: 'unconfirmed', detail: recorded ? 'Aguardando recibo do aparelho.' : 'FCM aceitou, mas o registro desse aceite na API ficou pendente. Preserve este identificador.' };
}
async function main(argv) {
  const action = argv.shift(); const args = {};
  check(['installations', 'send', 'report'].includes(action), 'Use installations, send ou report.');
  while (argv.length) {
    const flag = argv.shift();
    if (flag === '--send') { check(!args.send, 'Opção duplicada.'); args.send = true; continue; }
    const value = argv.shift();
    check(['--key', '--target', '--notice', '--message', '--credentials', '--app-config'].includes(flag) && value && !value.startsWith('--') && !args[flag], 'Argumentos inválidos.');
    args[flag] = value;
  }
  const key = publisher(args['--key'] || DEFAULT_KEY);
  if (action === 'installations') {
    const data = await requestApi('control', control('installations', {}, key));
    return { installations: data.installations.map(item => ({ installationId: item.installationId, apkVersion: item.apkVersion, registeredAt: item.registeredAt, lastSeenAt: item.lastSeenAt })) };
  }
  if (action === 'report') {
    check(/^[a-f0-9-]{36}$/.test(args['--message'] || ''), 'Informe --message com o UUID do aviso.');
    return requestApi('control', control('report', { messageId: args['--message'] }, key));
  }
  check(args['--notice'] && fs.statSync(args['--notice']).size <= 8192, 'Informe --notice com JSON de até 8 KiB.');
  return send({ key, target: args['--target'], notice: JSON.parse(fs.readFileSync(args['--notice'], 'utf8')),
    send: args.send, credentials: args['--credentials'], appConfig: args['--app-config'] });
}
module.exports = { API, sign, control, notice, fcm, send, main };
if (require.main === module) main(process.argv.slice(2)).then(result => console.log(JSON.stringify(result))).catch(error => { console.error(error.message); process.exitCode = 1; });
