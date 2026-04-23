// Service Worker - Casa da Baba PWA
// Estratégia: cache-first para estáticos, network-only para API.
// Offline-first: tudo necessário para o app rodar fica pre-cacheado no install.

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

self.addEventListener('fetch', (event) => {
  // Só intercepta GET — POSTs/PUTs/DELETEs (ex: /api/mobile/sync) passam direto.
  if (event.request.method !== 'GET') return;

  const url = new URL(event.request.url);

  // Bypass total para requests da API (sync de dados — precisa estar online).
  if (url.pathname.includes('/api/')) {
    return;
  }

  // Google Fonts: deixa o browser cachear nativamente (cross-origin).
  if (url.origin.includes('fonts.googleapis.com') || url.origin.includes('fonts.gstatic.com')) {
    return;
  }

  // Cache-first para estáticos do mesmo origin (e requests do PWA).
  event.respondWith(
    caches.match(event.request).then((cached) => {
      if (cached) return cached;
      return fetch(event.request).then((resp) => {
        // Cacheia somente respostas OK do mesmo origin (evita cachear erros / cross-origin opacos).
        if (resp.ok && url.origin === self.location.origin) {
          const copy = resp.clone();
          caches.open(CACHE_VERSION).then((c) => c.put(event.request, copy));
        }
        return resp;
      }).catch(() => {
        // Offline sem cache: se é navegação (html), serve index.html; senão, undefined (erro normal).
        if (event.request.mode === 'navigate') {
          return caches.match('./index.html');
        }
      });
    })
  );
});
