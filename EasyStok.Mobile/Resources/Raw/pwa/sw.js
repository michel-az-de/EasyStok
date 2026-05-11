// Service Worker - Casa da Baba PWA
//
// Estrategia para suportar AUTO-UPDATE silencioso:
// - index.html / navegacao: NETWORK-FIRST com timeout 1.5s
//   (garante que update novo chega rapido se a rede estiver OK; cai pro cache se offline)
// - Resto dos estaticos (CSS, JS, icones): STALE-WHILE-REVALIDATE
//   (serve do cache rapido, atualiza em background)
// - /api/*: bypass total (sync precisa estar online sempre)
//
// CACHE_VERSION e substituida pelo CI a cada deploy (cdb-<sha>) — isso garante
// que o activate descarte caches antigos, forcando o conteudo cacheado a ser
// re-baixado apos cada release.
//
// Onda 9 — OTA controlado pelo servidor:
// O backend reporta esse mesmo CACHE_VERSION em /api/mobile/version > Ota.PwaCacheVersion
// (lido de wwwroot/pwa/sw.js pelo PwaVersionProvider). O sync.js compara o valor
// recebido com cdb-pwa-installed-version local; quando diferente, pede update.
// Web Admin pode forçar isso via comando remoto pwa_update (ver sync.js).

const CACHE_VERSION = 'cdb-v3-20260511b';
const STATIC_ASSETS = [
  './',
  './index.html',
  './manifest.json',
  './qrcode.min.js',
  './sync.js',
  './icons/favicon.png',
  './icons/icon-192.png',
  './icons/icon-512.png',
  './icons/icon-maskable-512.png'
];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE_VERSION)
      .then((cache) => cache.addAll(STATIC_ASSETS))
      .then(() => self.skipWaiting())
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys().then((keys) =>
      Promise.all(keys.filter(k => k !== CACHE_VERSION).map(k => caches.delete(k)))
    ).then(() => self.clients.claim())
  );
});

function networkFirstWithTimeout(req, ms) {
  return new Promise((resolve) => {
    let settled = false;
    const timer = setTimeout(() => {
      if (settled) return;
      caches.match(req).then(c => {
        if (settled) return;
        if (c) { settled = true; resolve(c); }
      });
    }, ms);
    fetch(req).then((resp) => {
      if (settled) return;
      settled = true; clearTimeout(timer);
      if (resp.ok && new URL(req.url).origin === self.location.origin) {
        const copy = resp.clone();
        caches.open(CACHE_VERSION).then(c => c.put(req, copy));
      }
      resolve(resp);
    }).catch(() => {
      if (settled) return;
      settled = true; clearTimeout(timer);
      caches.match(req).then(c => resolve(c || caches.match('./index.html')));
    });
  });
}

function staleWhileRevalidate(req, url) {
  return caches.match(req).then((cached) => {
    const network = fetch(req).then((resp) => {
      if (resp.ok && url.origin === self.location.origin) {
        const copy = resp.clone();
        caches.open(CACHE_VERSION).then(c => c.put(req, copy));
      }
      return resp;
    }).catch(() => undefined);
    return cached || network;
  });
}

// Onda 9 — Canal de controle SW <- página.
// O sync.js manda mensagens pra forçar update ou limpeza de cache, viabilizando
// "atualização pelo web" sem o operador precisar fechar/reabrir o app.
//   - SKIP_WAITING : ativa imediatamente o novo SW que está em waiting.
//   - CHECK_UPDATE : pede ao browser que refaça fetch do sw.js.
//   - CLEAR_CACHE  : apaga TODOS os caches mantidos por este SW.
self.addEventListener('message', (event) => {
  const data = event.data || {};
  const type = data.type;
  if (type === 'SKIP_WAITING') {
    self.skipWaiting();
    return;
  }
  if (type === 'CHECK_UPDATE') {
    if (self.registration && self.registration.update) {
      event.waitUntil(self.registration.update().catch(() => {}));
    }
    return;
  }
  if (type === 'CLEAR_CACHE') {
    event.waitUntil(
      caches.keys()
        .then((keys) => Promise.all(keys.map((k) => caches.delete(k))))
        .then(() => {
          if (event.source && event.source.postMessage) {
            try { event.source.postMessage({ type: 'CACHE_CLEARED' }); } catch (e) {}
          }
        })
    );
    return;
  }
});

self.addEventListener('fetch', (event) => {
  if (event.request.method !== 'GET') return;
  const url = new URL(event.request.url);

  // API: bypass (sync precisa de rede)
  if (url.pathname.startsWith('/api/')) return;

  // Google Fonts: deixa o browser cachear nativamente
  if (url.origin.includes('fonts.googleapis.com') || url.origin.includes('fonts.gstatic.com')) return;

  const isHtml = event.request.mode === 'navigate'
              || url.pathname === '/'
              || url.pathname.endsWith('/')
              || url.pathname.endsWith('index.html');

  if (isHtml) {
    event.respondWith(networkFirstWithTimeout(event.request, 1500));
  } else {
    event.respondWith(staleWhileRevalidate(event.request, url));
  }
});
