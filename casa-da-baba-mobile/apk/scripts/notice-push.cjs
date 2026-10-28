'use strict';
// Avisos públicos para instalações que habilitaram notificações. Sem dados de clientes.
const fs = require('node:fs');
const push = require('./ota-push.cjs');
const TOPIC = 'casa-da-baba-piloto-notices-v1';
function message(notice, validateOnly = true) {
  const valid = notice && Object.keys(notice).sort().join(',') === 'body,id,screen,title'
    && typeof notice.id === 'string' && /^[A-Za-z0-9_-]{1,64}$/.test(notice.id)
    && ['home', 'migration-guide'].includes(notice.screen)
    && typeof notice.title === 'string' && notice.title.trim().length > 0 && notice.title.length <= 100
    && typeof notice.body === 'string' && notice.body.trim().length > 0 && notice.body.length <= 1200;
  if (!valid) throw new Error('Aviso inválido: use id, title, body e screen (home ou migration-guide).');
  const result = { validate_only: validateOnly, message: { topic: TOPIC,
    notification: { title: notice.title, body: notice.body },
    data: { kind: 'app-notice', noticeId: notice.id, screen: notice.screen },
    android: { priority: 'high', ttl: '86400s', notification: {
      channel_id: 'cdb-operacao', icon: 'ic_stat_notice', tag: notice.id,
      click_action: 'com.casadababa.app.OPEN_NOTICE', default_sound: true
    } }
  } };
  if (Buffer.byteLength(JSON.stringify(result.message)) > 1800) throw new Error('Aviso excede o limite de bytes.');
  return result;
}
async function send(options, request = push.requestJson) {
  const payload = message(options.notice, options.validateOnly !== false);
  const credentials = push.configuration(options.credentials, options.appConfig);
  const grant = new URLSearchParams({ grant_type: 'urn:ietf:params:oauth:grant-type:jwt-bearer', assertion: push.assertion(credentials) }).toString();
  const token = await request(push.TOKEN_URL, grant, { 'Content-Type': 'application/x-www-form-urlencoded' });
  if (typeof token.access_token !== 'string' || !token.access_token || token.access_token.length > 8192 || /[\r\n]/.test(token.access_token)) throw new Error('OAuth não retornou token válido.');
  const response = await request('https://fcm.googleapis.com/v1/projects/' + encodeURIComponent(credentials.project_id) + '/messages:send', JSON.stringify(payload),
    { 'Content-Type': 'application/json', Authorization: 'Bearer ' + token.access_token });
  if (!response || typeof response.name !== 'string' || !response.name) throw new Error('FCM não confirmou o aceite do aviso.');
  return { status: payload.validate_only ? 'validated' : 'accepted', noticeId: options.notice.id, topic: TOPIC, projectId: credentials.project_id };
}
async function main(argv) {
  const args = {};
  while (argv.length) {
    const flag = argv.shift();
    if (flag === '--send' || flag === '--validate-only') {
      if (args.mode) throw new Error('Escolha somente --send ou --validate-only.'); args.mode = flag; continue;
    }
    const value = argv.shift();
    if (!['--credentials', '--app-config', '--notice'].includes(flag) || !value || value.startsWith('--') || args[flag]) throw new Error('Argumentos inválidos.');
    args[flag] = value;
  }
  if (!args['--credentials'] || !args['--app-config'] || !args['--notice']) throw new Error('Informe --credentials, --app-config e --notice. Padrão: apenas validar.');
  if (fs.statSync(args['--notice']).size > 8192) throw new Error('Arquivo de aviso excede o limite.');
  return send({ credentials: args['--credentials'], appConfig: args['--app-config'], notice: JSON.parse(fs.readFileSync(args['--notice'], 'utf8')), validateOnly: args.mode !== '--send' });
}
module.exports = { TOPIC, message, send, main };
if (require.main === module) main(process.argv.slice(2)).then(result => console.log(JSON.stringify(result))).catch(error => { console.error(error.message); process.exitCode = 1; });
