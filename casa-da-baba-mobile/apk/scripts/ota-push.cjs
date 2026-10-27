'use strict';
// FCM HTTP v1 + OAuth JWT, sem SDK extra. Credenciais nunca são copiadas ou registradas.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const https = require('node:https');
const ota = require('./ota-package.cjs');
const TOKEN_URL = 'https://oauth2.googleapis.com/token';
const SCOPE = 'https://www.googleapis.com/auth/firebase.messaging';
const TOPIC = 'casa-da-baba-piloto-updates';
const PUBLIC_ROOT = '/opt/stacks/shared/docs/easystok-site';
function requireValue(ok, message) { if (!ok) throw new Error(message); }
function readJson(file, maximum = 65536) {
  const stat = fs.statSync(file);
  requireValue(stat.isFile() && stat.size <= maximum, 'Arquivo de configuração inválido.');
  try { return JSON.parse(fs.readFileSync(file, 'utf8')); }
  catch { throw new Error('JSON de configuração inválido.'); }
}
function credentialPath(file) {
  const resolved = fs.realpathSync(file);
  const normalized = resolved.replaceAll('\\', '/').toLowerCase();
  requireValue(normalized !== PUBLIC_ROOT && !normalized.startsWith(PUBLIC_ROOT + '/'), 'Credencial não pode ficar no diretório público da VPS.');
  for (let parent = path.dirname(resolved); ; parent = path.dirname(parent)) {
    requireValue(!fs.existsSync(path.join(parent, '.git')), 'Credencial deve ficar fora de repositórios Git.');
    if (parent === path.dirname(parent)) break;
  }
  return resolved;
}
function configuration(credentialsFile, appConfigFile) {
  const credentials = readJson(credentialPath(credentialsFile));
  const app = readJson(appConfigFile);
  requireValue(credentials.type === 'service_account' && typeof credentials.project_id === 'string' && /^[a-z][a-z0-9-]{4,62}$/.test(credentials.project_id), 'Conta de serviço Firebase inválida.');
  requireValue(app.project_info?.project_id === credentials.project_id, 'Projeto da credencial diverge do aplicativo.');
  requireValue(Array.isArray(app.client) && app.client.some(client => client.client_info?.android_client_info?.package_name === ota.APP), 'Configuração Firebase não pertence ao APK Casa da Baba.');
  requireValue(typeof credentials.client_email === 'string' && /^[^\s@]+@[^\s@]+\.gserviceaccount\.com$/.test(credentials.client_email), 'Identidade da conta de serviço inválida.');
  requireValue(typeof credentials.private_key === 'string' && credentials.private_key.length <= 16384, 'Chave da conta de serviço inválida.');
  requireValue(!credentials.token_uri || credentials.token_uri === TOKEN_URL, 'Endpoint OAuth da credencial não permitido.');
  return credentials;
}
function assertion(credentials, now = Math.floor(Date.now() / 1000)) {
  const header = { alg: 'RS256', typ: 'JWT' };
  if (credentials.private_key_id) header.kid = credentials.private_key_id;
  const claims = { iss: credentials.client_email, scope: SCOPE, aud: TOKEN_URL, iat: now, exp: now + 3600 };
  const encoded = value => Buffer.from(JSON.stringify(value)).toString('base64url');
  const input = encoded(header) + '.' + encoded(claims);
  let key;
  try { key = crypto.createPrivateKey(credentials.private_key); }
  catch { throw new Error('Chave da conta de serviço inválida.'); }
  requireValue(key.asymmetricKeyType === 'rsa' && key.asymmetricKeyDetails.modulusLength >= 2048, 'Chave OAuth precisa ser RSA de pelo menos 2048 bits.');
  return input + '.' + crypto.sign('RSA-SHA256', Buffer.from(input), { key, padding: crypto.constants.RSA_PKCS1_PADDING }).toString('base64url');
}
function message(payload, validateOnly) {
  ota.validate(payload);
  return {
    validate_only: validateOnly,
    message: {
      topic: TOPIC,
      data: { kind: 'ota-update', sequence: String(payload.sequence), bundleVersion: payload.bundleVersion },
      android: { priority: 'normal', ttl: '86400s' }
    }
  };
}
function requestJson(url, body, headers) {
  return new Promise((resolve, reject) => {
    // URLs são construídas internamente; não seguir redirects nem usar token_uri arbitrário.
    const target = new URL(url);
    requireValue(target.protocol === 'https:' && ['oauth2.googleapis.com', 'fcm.googleapis.com'].includes(target.hostname), 'Destino HTTPS não permitido.');
    const request = https.request(target, { method: 'POST', headers: { ...headers, 'Content-Length': Buffer.byteLength(body) }, timeout: 20000 }, response => {
      const chunks = []; let size = 0;
      response.on('data', chunk => {
        size += chunk.length;
        if (size > 65536) request.destroy(new Error('Resposta FCM excedeu o limite.'));
        else chunks.push(chunk);
      });
      response.on('error', () => reject(new Error('Resposta FCM interrompida.')));
      response.on('end', () => {
        if (response.statusCode < 200 || response.statusCode >= 300) return reject(new Error('Serviço Google recusou a requisição: HTTP ' + response.statusCode + '.'));
        try { resolve(JSON.parse(Buffer.concat(chunks).toString('utf8'))); }
        catch { reject(new Error('Resposta Google inválida.')); }
      });
    });
    request.on('timeout', () => request.destroy(new Error('Tempo limite ao comunicar com Google.')));
    request.on('error', () => reject(new Error('Não foi possível concluir a conexão segura com Google.')));
    request.end(body);
  });
}
async function sendUpdate(options, request = requestJson) {
  const credentials = configuration(options.credentials, options.appConfig);
  const key = crypto.createPublicKey({ key: fs.readFileSync(options.publicKey), format: 'der', type: 'spki' });
  const payload = ota.verify(readJson(options.release, 24576), key);
  const jwt = assertion(credentials);
  const grant = new URLSearchParams({ grant_type: 'urn:ietf:params:oauth:grant-type:jwt-bearer', assertion: jwt }).toString();
  const token = await request(TOKEN_URL, grant, { 'Content-Type': 'application/x-www-form-urlencoded' });
  requireValue(typeof token.access_token === 'string' && token.access_token.length > 0 && token.access_token.length <= 8192 && !/[\r\n]/.test(token.access_token), 'OAuth não retornou token válido.');
  const body = JSON.stringify(message(payload, options.validateOnly !== false));
  await request('https://fcm.googleapis.com/v1/projects/' + encodeURIComponent(credentials.project_id) + '/messages:send', body,
    { 'Content-Type': 'application/json', Authorization: 'Bearer ' + token.access_token });
  return { status: options.validateOnly !== false ? 'validated' : 'accepted', sequence: payload.sequence, projectId: credentials.project_id, topic: TOPIC };
}
async function main(argv) {
  const args = {};
  while (argv.length) {
    const flag = argv.shift();
    if (flag === '--send' || flag === '--validate-only') { requireValue(!args.mode, 'Selecione somente --send ou --validate-only.'); args.mode = flag; continue; }
    const value = argv.shift();
    requireValue(['--credentials', '--app-config', '--release', '--public-key'].includes(flag) && value && !value.startsWith('--') && !args[flag], 'Argumentos inválidos.');
    args[flag] = value;
  }
  requireValue(args['--credentials'] && args['--app-config'] && args['--release'], 'Informe --credentials, --app-config e --release. O padrão é validate_only.');
  return sendUpdate({ credentials: args['--credentials'], appConfig: args['--app-config'], release: args['--release'],
    publicKey: args['--public-key'] || path.resolve(__dirname, '../android/app/src/main/assets/ota-public-key.der'), validateOnly: args.mode !== '--send' });
}
module.exports = { TOKEN_URL, SCOPE, TOPIC, configuration, assertion, message, sendUpdate, main };
if (require.main === module) main(process.argv.slice(2)).then(result => console.log(JSON.stringify(result))).catch(error => { console.error(error.message); process.exitCode = 1; });
