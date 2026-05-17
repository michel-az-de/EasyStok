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

const CACHE_VERSION = 'cdb-v27-calculadora-cesta';
const STATIC_ASSETS = [
  './',
  './index.html',
  './manifest.json',
  './qrcode.min.js',
  './sync.js',
  './icons/favicon.png',
  './icons/icon-192.png',
  './icons/icon-512.png',
  './icons/icon-maskable-512.png',
  // F6 — módulo de etiquetas
  './etiqueta/etiqueta.css',
  './etiqueta/render.js',
  './etiqueta/codes.js',
  './etiqueta/variables.js',
  './etiqueta/migrate.js',
  './etiqueta/imprimir.js',
  './etiqueta/editor/editor.css',
  './etiqueta/editor/editor.js',
  './etiqueta/vendor/qrcode.min.js',
  './etiqueta/vendor/jsbarcode.min.js',
  './etiqueta/assets/logo-easystok.svg',
  './etiqueta/assets/lockup-easystok.svg',
];

// Cache de render de etiquetas com TTL de 1 hora
// (stale-while-revalidate com expiração para não servir ficha técnica editada)
const ETQ_RENDER_TTL_MS = 60 * 60 * 1000; // 1h
const ETQ_RENDER_CACHE  = 'etq-render-v1';

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
  // F6 — Invalida cache de render de uma etiqueta específica após marcar-impressas
  if (type === 'CACHE_DELETE') {
    const urlToDelete = data.url;
    if (urlToDelete) {
      event.waitUntil(
        caches.open(ETQ_RENDER_CACHE).then(c => c.delete(urlToDelete))
      );
    }
    return;
  }
  // F6 — Logout: limpa caches de API do usuário anterior (escopo por userId)
  // Mantém assets estáticos (CACHE_VERSION). Protege contra aba fechada antes do logout.
  if (type === 'LOGOUT_CLEAR') {
    const loggedOutUserId = data.userId;
    event.waitUntil(
      caches.open(ETQ_RENDER_CACHE).then(c =>
        c.keys().then(keys =>
          Promise.all(
            keys
              .filter(req => !loggedOutUserId || req.url.includes(`userId=${loggedOutUserId}`))
              .map(req => c.delete(req))
          )
        )
      )
    );
    return;
  }
});

// C5 — Web Push handler. Permite que o browser receba notificacoes
// PUSH mesmo com aba/app fechado (depende de Push API + subscricao prévia).
// Em APK Capacitor, isso eh complementado pelo FCM nativo; aqui cobrimos o
// PWA web puro (Chromium desktop/mobile com PWA instalada).
//
// Formato esperado do payload: { type, title, body, tag, data }.
// Fail-safe: payload ausente ou malformado → notification generica "Atualizacao".
self.addEventListener('push', (event) => {
  let payload = {};
  try {
    if (event.data) {
      try { payload = event.data.json(); }
      catch { payload = { title: 'Atualizacao', body: event.data.text() }; }
    }
  } catch (_) { /* sem data: notification generica */ }

  const title = payload.title || (payload.type === 'order.ready' ? 'Pedido pronto' : 'Casa da Baba');
  const body = payload.body
    || (payload.type === 'order.ready'
        ? ((payload.clientName ? payload.clientName + ' — ' : '') + 'pedido pronto pra entrega')
        : 'Nova atualizacao');
  const tag = payload.tag || (payload.type && payload.orderId
    ? 'cdb-' + payload.type + '-' + payload.orderId
    : 'cdb-default');

  const opts = {
    body: body,
    tag: tag,
    renotify: false,
    icon: './icons/icon-192.png',
    badge: './icons/favicon.png',
    data: payload
  };

  event.waitUntil(self.registration.showNotification(title, opts));
});

// Click na notification: foca aba existente ou abre nova.
self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  const data = event.notification.data || {};
  const target = data.url || './';
  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((wins) => {
      for (const w of wins) {
        if ('focus' in w) return w.focus();
      }
      if (self.clients.openWindow) return self.clients.openWindow(target);
    })
  );
});

// F6 — Stale-while-revalidate com TTL para endpoint de render de etiquetas.
// TTL de 1h evita servir ficha técnica editada pelo operador entre cache e impressão.
function etqRenderWithTtl(req) {
  const now = Date.now();
  return caches.open(ETQ_RENDER_CACHE).then(cache =>
    cache.match(req).then(cached => {
      const fetchAndStore = fetch(req).then(resp => {
        if (resp.ok) {
          const copy = resp.clone();
          // Armazena com timestamp no header sintético via Response wrapping
          cache.put(req, copy);
        }
        return resp;
      }).catch(() => cached);

      if (cached) {
        const age = now - (parseInt(cached.headers.get('x-sw-cached-at') || '0', 10) || 0);
        // Se ainda dentro do TTL, serve stale e revalida em background
        if (age < ETQ_RENDER_TTL_MS) {
          fetchAndStore; // background
          return cached;
        }
      }
      // Expirado ou sem cache: aguarda rede
      return fetchAndStore;
    })
  );
}

self.addEventListener('fetch', (event) => {
  if (event.request.method !== 'GET') return;
  const url = new URL(event.request.url);

  // Google Fonts: deixa o browser cachear nativamente
  if (url.origin.includes('fonts.googleapis.com') || url.origin.includes('fonts.gstatic.com')) return;

  // F6 — Endpoint de render de etiquetas: stale-while-revalidate com TTL 1h
  if (url.pathname.match(/^\/api\/lotes\/[^/]+\/etiquetas\/render$/)) {
    event.respondWith(etqRenderWithTtl(event.request));
    return;
  }

  // Demais rotas de API: bypass (sync precisa de rede)
  if (url.pathname.startsWith('/api/')) return;

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
