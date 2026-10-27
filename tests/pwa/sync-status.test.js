'use strict';
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

module.exports = function ({ test, assert }) {
  const html = fs.readFileSync(process.env.PWA_HTML || path.resolve(__dirname, '../../EasyStock.Api/wwwroot/pwa/index.html'), 'utf8');
  const start = html.indexOf('function updateSync() {');
  const end = html.indexOf("window.addEventListener('online', updateSync);", start);
  assert.ok(start >= 0 && end > start);
  function render({ paired = true, invalid = false, online = true, pending = 0 } = {}) {
    const nodes = new Map();
    const document = { getElementById(id) {
      if (!nodes.has(id)) nodes.set(id, { textContent: '', hidden: false, classList: { toggle() {}, add() {}, remove() {} } });
      return nodes.get(id);
    } };
    const context = { document, navigator: { onLine: online }, pendingSync: pending,
      window: { CDB_CONFIG: { isPilot: true }, cdbSync: { getPairing: () => paired ? { deviceId: 'test' } : null, isPairingInvalid: () => invalid } } };
    vm.runInNewContext(html.slice(start, end) + '\nupdateSync();', context);
    return nodes.get('syncText').textContent;
  }
  test('fila vazia com aparelho vinculado não afirma sincronização sem recibo do servidor', () => {
    assert.match(html, /id="syncText">Conferindo fila de envio</);
    assert.equal(render(), 'Nenhuma alteração na fila de envio');
    assert.doesNotMatch(render(), /sincronizado|confirmado/i);
  });
  test('estado da fila preserva falta de pareamento, conexão inválida, pendências e offline', () => {
    assert.equal(render({ paired: false }), 'Neste aparelho · não conectado');
    assert.equal(render({ invalid: true }), 'Neste aparelho · não conectado');
    assert.equal(render({ pending: 2 }), '2 itens pra sincronizar');
    assert.equal(render({ online: false }), 'Offline');
    assert.equal(render({ online: false, pending: 1 }), '1 pendente offline');
  });
};
