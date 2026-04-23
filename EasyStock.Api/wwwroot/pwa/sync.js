// Casa da Baba - Sync Layer
// Espelha o estado local pro backend via REST. Tolera offline.
//
// Configuracao: ajuste API_BASE_URL abaixo.
// - Mesmo host servindo o PWA: deixe '' (relativo)
// - Host separado (dev): 'http://localhost:5280'
// - Producao: 'https://easystock.seu-dominio.com'
//
// Auth: o servidor EasyStock exige header X-Mobile-Api-Key (configurado em
// Mobile:ApiKey do appsettings). Na primeira request que der 401, o app
// pede a chave via prompt e salva em localStorage.

(function () {
  'use strict';

  const API_BASE_URL = '';              // vazio = mesmo host
  const API_PREFIX = '/api/mobile';
  const DEVICE_ID_KEY = 'cdb-device-id';
  const QUEUE_KEY = 'cdb-sync-queue';
  const LAST_FULL_SYNC_KEY = 'cdb-last-sync';
  const API_KEY_STORAGE = 'cdb-mobile-api-key';

  // ---- Device ID (primeiro boot gera um UUID, persiste) ----
  function getDeviceId() {
    let id = localStorage.getItem(DEVICE_ID_KEY);
    if (!id) {
      id = 'dev-' + Math.random().toString(36).substr(2, 9) + '-' + Date.now().toString(36);
      localStorage.setItem(DEVICE_ID_KEY, id);
    }
    return id;
  }
  const deviceId = getDeviceId();

  // ---- API key (exigida pelo backend via header X-Mobile-Api-Key) ----
  function getApiKey() {
    let k = localStorage.getItem(API_KEY_STORAGE);
    if (!k) {
      k = prompt('Chave de API do servidor (configure uma vez — informe ao Felipe):');
      if (k) {
        k = k.trim();
        if (k) localStorage.setItem(API_KEY_STORAGE, k);
      }
    }
    return k || '';
  }

  function authHeaders(extra) {
    const h = Object.assign({ 'X-Device-Id': deviceId, 'X-Mobile-Api-Key': getApiKey() }, extra || {});
    return h;
  }

  /** Em 401 (chave inválida), limpa pra forçar novo prompt no próximo request. */
  function handleUnauthorized(resp) {
    if (resp && resp.status === 401) {
      localStorage.removeItem(API_KEY_STORAGE);
      console.warn('API key inválida — será solicitada novamente no próximo sync.');
      return true;
    }
    return false;
  }

  // ---- Fila persistente ----
  function loadQueue() {
    try { return JSON.parse(localStorage.getItem(QUEUE_KEY) || '[]'); } catch (e) { return []; }
  }
  function saveQueue(q) {
    try { localStorage.setItem(QUEUE_KEY, JSON.stringify(q)); } catch (e) {}
  }

  // Snapshot do estado anterior pra calcular delta
  let lastSnapshot = null;

  // ---- Calcula diff entre estados e gera mutations ----
  function computeMutations(prev, curr) {
    const muts = [];

    // Helpers: comparar colecoes por id + updatedAt/lastOrder
    function diffCollection(coll, prevColl, typeName, getUpdatedAt) {
      const prevById = new Map((prevColl || []).map(x => [x.id, x]));
      coll.forEach(item => {
        const prev = prevById.get(item.id);
        if (!prev) {
          muts.push({ type: typeName + '.upsert', payload: item });
        } else if (getUpdatedAt) {
          const a = getUpdatedAt(prev);
          const b = getUpdatedAt(item);
          if (a !== b || JSON.stringify(prev) !== JSON.stringify(item)) {
            muts.push({ type: typeName + '.upsert', payload: item });
          }
        } else if (JSON.stringify(prev) !== JSON.stringify(item)) {
          muts.push({ type: typeName + '.upsert', payload: item });
        }
      });
    }

    diffCollection(curr.products.map(({count, photo, ...r}) => r), prev && prev.products, 'product');
    diffCollection(curr.clients, prev && prev.clients, 'client', c => c.lastOrder);
    diffCollection(curr.orders, prev && prev.orders, 'order', o => o.updatedAt);
    diffCollection(curr.cashEntries, prev && prev.cashEntries, 'cashEntry');
    diffCollection(curr.batches, prev && prev.batches, 'batch');

    return muts;
  }

  // ---- Enfileira mutations ----
  function enqueue(mutations) {
    if (!mutations.length) return;
    const queue = loadQueue();
    mutations.forEach(m => {
      queue.push({
        id: 'm-' + Date.now() + '-' + Math.random().toString(36).substr(2, 5),
        deviceId,
        type: m.type,
        payload: m.payload,
        ts: Date.now()
      });
    });
    saveQueue(queue);
    updatePendingCount();
  }

  function updatePendingCount() {
    const q = loadQueue();
    if (window.cdbApp) window.cdbApp.setPendingSync(q.length);
  }

  // ---- Flush: tenta enviar tudo ao backend ----
  let flushing = false;
  async function flush() {
    if (flushing) return;
    const queue = loadQueue();
    if (queue.length === 0) {
      if (window.cdbApp) window.cdbApp.markAllSynced();
      return;
    }
    if (!navigator.onLine) return;

    flushing = true;
    try {
      const url = API_BASE_URL + API_PREFIX + '/sync';
      const resp = await fetch(url, {
        method: 'POST',
        headers: authHeaders({ 'Content-Type': 'application/json' }),
        body: JSON.stringify({ deviceId, mutations: queue })
      });

      if (handleUnauthorized(resp)) return;

      if (!resp.ok) {
        console.warn('Sync falhou, status', resp.status);
        return;
      }

      const result = await resp.json().catch(() => ({}));
      // Servidor pode devolver ids aceitos; por padrao limpa tudo
      if (result.acceptedIds && Array.isArray(result.acceptedIds)) {
        const accepted = new Set(result.acceptedIds);
        const remaining = queue.filter(m => !accepted.has(m.id));
        saveQueue(remaining);
      } else {
        saveQueue([]);
      }
      localStorage.setItem(LAST_FULL_SYNC_KEY, String(Date.now()));
      updatePendingCount();
    } catch (e) {
      console.warn('Erro no sync:', e.message);
    } finally {
      flushing = false;
    }
  }

  // ---- Pull: traz mudancas do servidor (outros devices) ----
  async function pull() {
    if (!navigator.onLine) return;
    try {
      const since = localStorage.getItem(LAST_FULL_SYNC_KEY) || '0';
      const url = API_BASE_URL + API_PREFIX + '/sync/pull?since=' + since + '&deviceId=' + deviceId;
      const resp = await fetch(url, { headers: authHeaders() });
      if (handleUnauthorized(resp)) return;
      if (!resp.ok) return;
      const data = await resp.json();
      // Aplica mudancas no state (sobrescreve por id)
      if (data && data.mutations && Array.isArray(data.mutations)) {
        applyServerMutations(data.mutations);
      }
    } catch (e) {
      console.warn('Pull falhou:', e.message);
    }
  }

  function applyServerMutations(mutations) {
    if (!mutations.length || !window.cdbApp) return;
    const state = window.cdbApp.getState();
    let changed = false;
    mutations.forEach(m => {
      const [type, op] = m.type.split('.');
      const coll = {
        product: state.products,
        client: state.clients,
        order: state.orders,
        cashEntry: state.cashEntries,
        batch: state.batches
      }[type];
      if (!coll) return;
      const key = {
        product: 'cdb-products',
        client: 'cdb-clients',
        order: 'cdb-orders',
        cashEntry: 'cdb-cash',
        batch: 'cdb-batches'
      }[type];
      const idx = coll.findIndex(x => x.id === m.payload.id);
      if (op === 'upsert') {
        if (idx >= 0) coll[idx] = m.payload;
        else coll.push(m.payload);
        changed = true;
      } else if (op === 'delete') {
        if (idx >= 0) coll.splice(idx, 1);
        changed = true;
      }
      try { localStorage.setItem(key, JSON.stringify(coll)); } catch (e) {}
    });
    if (changed) {
      // Recarrega a tela pra refletir as mudancas
      // (solucao simples; alternativa seria re-renderizar parcialmente)
      console.log('Mudancas do servidor aplicadas, recarregando...');
      setTimeout(() => location.reload(), 500);
    }
  }

  // ---- Hook de persistencia: chamado pelo app toda vez que salva ----
  window.cdbOnPersist = function (state) {
    const muts = computeMutations(lastSnapshot, state);
    lastSnapshot = JSON.parse(JSON.stringify({
      products: state.products.map(({count, photo, ...r}) => r),
      clients: state.clients,
      orders: state.orders,
      cashEntries: state.cashEntries,
      batches: state.batches
    }));
    if (muts.length) {
      enqueue(muts);
      // Tenta sincronizar na hora (se online)
      flush();
    }
  };

  // ---- Eventos de conectividade ----
  window.addEventListener('online', () => {
    console.log('Voltei online, sincronizando...');
    flush().then(pull);
  });
  window.addEventListener('offline', () => console.log('Offline, tudo vai pra fila.'));

  // ---- Inicializacao ----
  setTimeout(() => {
    // Snapshot inicial do estado pra calcular deltas depois
    if (window.cdbApp) {
      const s = window.cdbApp.getState();
      lastSnapshot = JSON.parse(JSON.stringify({
        products: s.products.map(({count, photo, ...r}) => r),
        clients: s.clients,
        orders: s.orders,
        cashEntries: s.cashEntries,
        batches: s.batches
      }));
    }
    updatePendingCount();
    // Tenta pull inicial e flush se tiver coisa pendente
    if (API_BASE_URL !== null) {
      flush().then(pull);
    }
    // Flush periodico a cada 30s
    setInterval(flush, 30000);
  }, 500);

  // Expoe utilitarios pra debug
  window.cdbSync = {
    flush, pull, queueSize: () => loadQueue().length,
    clearQueue: () => { saveQueue([]); updatePendingCount(); },
    setApiKey: (k) => { if (k) localStorage.setItem(API_KEY_STORAGE, k.trim()); },
    clearApiKey: () => localStorage.removeItem(API_KEY_STORAGE),
    deviceId
  };
})();
