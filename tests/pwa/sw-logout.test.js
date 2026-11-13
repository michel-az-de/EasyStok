// Testes — #2369 cache de render no logout
//
// O render da etiqueta é autenticado e fica até 1 h no cache do service worker. A URL não carrega o usuário,
// então o LOGOUT_CLEAR precisa apagar o cache de render inteiro; senão o próximo usuário do aparelho recebe o
// render do anterior. E quem desconecta o aparelho precisa mandar a mensagem.
'use strict';

const fs = require('fs');
const path = require('path');
const vm = require('vm');

module.exports = function ({ test, assert }) {
  const PWA_DIR = path.resolve(__dirname, '../../EasyStock.Api/wwwroot/pwa');
  const SW_PATH = process.env.PWA_SW || path.join(PWA_DIR, 'sw.js');

  // Carrega o sw.js com um CacheStorage em memória e devolve o handler de mensagem.
  function carregarSw(entradas) {
    const handlers = {};
    const store = new Map(Object.entries(entradas).map(([nome, urls]) => [nome, new Set(urls)]));
    const caches = {
      open: async (nome) => {
        if (!store.has(nome)) store.set(nome, new Set());
        const c = store.get(nome);
        return {
          delete: async (u) => c.delete(u),
          keys: async () => [...c].map(url => ({ url })),
          put: async (u) => { c.add(typeof u === 'string' ? u : u.url); },
          addAll: async () => {}
        };
      },
      keys: async () => [...store.keys()],
      delete: async (nome) => store.delete(nome),
      match: async () => undefined
    };
    const self = {
      location: { origin: 'https://api.exemplo.test' },
      registration: {},
      clients: { claim: async () => {} },
      skipWaiting() {},
      addEventListener(tipo, fn) { handlers[tipo] = fn; }
    };
    const ctx = { self, caches, URL, Headers: class {}, Response: class {}, console, setTimeout, clearTimeout, fetch: async () => ({}) };
    vm.createContext(ctx);
    vm.runInContext(fs.readFileSync(SW_PATH, 'utf8'), ctx, { filename: 'sw.js' });
    const nomes = vm.runInContext('({ render: ETQ_RENDER_CACHE, estatico: CACHE_VERSION })', ctx);

    async function enviar(data) {
      const esperas = [];
      handlers.message({ data, waitUntil(p) { esperas.push(p); } });
      await Promise.all(esperas);
    }
    return { enviar, store, nomes };
  }

  test('#2369 sw: LOGOUT_CLEAR apaga todo render de etiqueta, mesmo sem userId na URL', async () => {
    const sw = carregarSw({});
    const render = sw.nomes.render;
    sw.store.set(render, new Set([
      'https://api.exemplo.test/api/etiquetas/render/abc?empresaId=e1&formato=png',
      'https://api.exemplo.test/api/etiquetas/render/def?empresaId=e1'
    ]));
    sw.store.set(sw.nomes.estatico, new Set(['https://api.exemplo.test/pwa/index.html']));

    await sw.enviar({ type: 'LOGOUT_CLEAR' });

    const sobrou = sw.store.has(render) ? [...sw.store.get(render)] : [];
    assert.deepStrictEqual(sobrou, [], 'o render do usuário anterior não pode ficar para o próximo');
    assert.ok(sw.store.has(sw.nomes.estatico), 'os arquivos do app continuam em cache para abrir offline');
  });

  test('#2369 pwa: desconectar o aparelho envia LOGOUT_CLEAR ao service worker', () => {
    const html = fs.readFileSync(path.join(PWA_DIR, 'index.html'), 'utf8');
    const inicio = html.indexOf('function unpairDevice(');
    assert.ok(inicio >= 0, 'unpairDevice existe');
    const corpo = html.slice(inicio, html.indexOf('\n}', inicio));
    assert.match(corpo, /postMessage\(\{\s*type:\s*'LOGOUT_CLEAR'\s*\}\)/);
  });
};
