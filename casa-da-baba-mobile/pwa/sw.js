// Service Worker - Casa da Baba PWA
// Estrategia: cache-first para estaticos, network-only para API.

const CACHE_VERSION = 'cdb-v1';
const STATIC_ASSETS = [
  './',
  './index.html',
  './manifest.json',
  './sync.js',
  './icons/icon-192.png',
  './icons/icon-512.png'
];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE_VERSION).then((cache) => cache.addAll(STATIC_ASSETS))
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
  const url = new URL(event.request.url);

  // Bypass total pra requests de API (sincronizacao de dados)
  if (url.pathname.includes('/api/mobile/') || url.pathname.includes('/api/')) {
    return; // deixa a rede cuidar, sem cache
  }

  // Bypass pras fontes do Google (cacheadas pelo browser)
  if (url.origin.includes('fonts.googleapis.com') || url.origin.includes('fonts.gstatic.com')) {
    return;
  }

  // Cache-first pros estaticos
  if (event.request.method === 'GET') {
    event.respondWith(
      caches.match(event.request).then((cached) => {
        if (cached) return cached;
        return fetch(event.request).then((resp) => {
          // Cacheia o que for do mesmo origin e deu 200
          if (resp.ok && url.origin === self.location.origin) {
            const copy = resp.clone();
            caches.open(CACHE_VERSION).then((c) => c.put(event.request, copy));
          }
          return resp;
        }).catch(() => {
          // Fallback offline: volta pra index.html se for navegacao
          if (event.request.mode === 'navigate') {
            return caches.match('./index.html');
          }
        });
      })
    );
  }
});
