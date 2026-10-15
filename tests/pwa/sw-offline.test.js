// Testes — #1509 service worker offline
//
// A pagina carrega `sync.js?v=...` (e queue-store, photo-store, upload-photos), mas o
// pre-cache guardava so './sync.js' e o match exigia a query igual. Sem rede, o caixa
// abria sem a fila de sync. Online, a versao nova continua vindo da rede (OTA).
'use strict';

const fs = require('fs');
const path = require('path');
const vm = require('vm');

module.exports = function ({ test, assert }) {
  const PWA_DIR = path.resolve(__dirname, '../../EasyStock.Api/wwwroot/pwa');
  const SW_PATH = process.env.PWA_SW || path.join(PWA_DIR, 'sw.js');

  // Carrega o sw.js com caches/fetch falsos e devolve o handler de fetch.
  function carregarSw({ cacheado, online }) {
    const handlers = {};
    const origin = 'https://api.exemplo.test';
    const semQuery = (u) => String(u).split('?')[0];
    const caches = {
      match: async (req, opts) => {
        const u = typeof req === 'string' ? new URL(req, origin + '/pwa/').href : req.url;
        const achado = cacheado.find(c => (opts && opts.ignoreSearch) ? semQuery(c) === semQuery(u) : c === u);
        return achado ? { fonte: 'cache', url: achado } : undefined;
      },
      open: async () => ({ put: async () => {}, addAll: async () => {} }),
      keys: async () => [],
      delete: async () => true
    };
    const self = {
      location: { origin },
      registration: {},
      clients: { claim: async () => {} },
      skipWaiting() {},
      addEventListener(tipo, fn) { handlers[tipo] = fn; }
    };
    const ctx = {
      self, caches, URL, Headers: class {}, Response: class {}, console, setTimeout, clearTimeout,
      fetch: async (req) => {
        if (!online) throw new TypeError('Failed to fetch');
        return { ok: true, clone() { return this; }, fonte: 'rede', url: req.url };
      }
    };
    vm.createContext(ctx);
    const fonte = fs.readFileSync(SW_PATH, 'utf8');
    vm.runInContext(fonte, ctx, { filename: 'sw.js' });
    const assets = vm.runInContext('STATIC_ASSETS', ctx);
    async function buscar(url) {
      let resposta;
      handlers.fetch({
        request: { method: 'GET', url, mode: 'no-cors' },
        respondWith(p) { resposta = p; }
      });
      return resposta;
    }
    return { buscar, assets, origin };
  }

  test('#1509 sw: todo script local do index.html esta no pre-cache', () => {
    const html = fs.readFileSync(path.join(PWA_DIR, 'index.html'), 'utf8');
    const { assets } = carregarSw({ cacheado: [], online: true });
    const re = /<script\b[^>]*\bsrc=["']([^"']+)["']/gi;
    const faltando = [];
    let m;
    while ((m = re.exec(html)) !== null) {
      const src = m[1].split('?')[0];
      if (/^(?:https?:)?\/\//i.test(src) || src.startsWith('/')) continue;
      const rel = './' + src.replace(/^\.\//, '');
      if (!assets.includes(rel)) faltando.push(rel);
    }
    assert.deepStrictEqual(faltando, [], 'scripts fora do pre-cache: ' + faltando.join(', '));
  });

  test('#1509 sw: offline, sync.js?v=... cai no ./sync.js pre-carregado', async () => {
    const origin = 'https://api.exemplo.test';
    const { buscar } = carregarSw({ cacheado: [origin + '/pwa/sync.js'], online: false });
    const resp = await buscar(origin + '/pwa/sync.js?v=20260514b');
    assert.ok(resp && resp.fonte === 'cache', 'sem rede, serve a copia do pre-cache');
  });

  test('#1509 sw: online, versao nova vem da rede (OTA nao serve script velho)', async () => {
    const origin = 'https://api.exemplo.test';
    const { buscar } = carregarSw({ cacheado: [origin + '/pwa/sync.js?v=velha'], online: true });
    const resp = await buscar(origin + '/pwa/sync.js?v=nova');
    assert.ok(resp && resp.fonte === 'rede', 'com rede, a query nova busca na rede');
  });
};
