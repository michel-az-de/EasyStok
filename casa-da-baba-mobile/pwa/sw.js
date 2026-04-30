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

const CACHE_VERSION = 'cdb-v2';
const STATIC_ASSETS = [
  './',
  './index.html',
  './manifest.json',
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

self.addEventListener('fetch', (event) => {
  if (event.request.method !== 'GET') return;
  const url = new URL(event.request.url);

  // API: bypass (sync precisa de rede)
  if (url.pathname.includes('/api/')) return;

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
