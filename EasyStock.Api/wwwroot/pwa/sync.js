// Casa da Baba - Sync Layer
// Espelha o estado local pro backend via REST. Tolera offline.
//
// Configuração:
//  - API_BASE_URL: base da API. Padrão: mesmo host que serve o PWA.
//    Para rodar num APK empacotado apontando pra backend remoto,
//    setar window.CDB_CONFIG = { apiBaseUrl: 'https://easystock.exemplo.com' }
//    ANTES de carregar este script (via config.js ou capacitor.config).
//
// Offline-first: toda mutação vai pra uma fila persistente em localStorage.
// Fila é drenada quando a rede volta ou a cada 30s. O app funciona 100% sem
// rede — sync é oportunístico, nunca bloqueante.

(function () {
  'use strict';

  const CFG = (window.CDB_CONFIG || {});
  const API_BASE_URL = CFG.apiBaseUrl ?? '';  // vazio = mesmo host
  const API_PREFIX = '/api/mobile';
  const DEVICE_ID_KEY = 'cdb-device-id';
  const QUEUE_KEY = 'cdb-sync-queue';
  const LAST_FULL_SYNC_KEY = 'cdb-last-sync';

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

  function baseHeaders(extra) {
    return Object.assign({ 'X-Device-Id': deviceId }, extra || {});
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

    function diffCollection(coll, prevColl, typeName, getUpdatedAt) {
      const prevById = new Map((prevColl || []).map(x => [x.id, x]));
      coll.forEach(item => {
        const p = prevById.get(item.id);
        if (!p) {
          muts.push({ type: typeName + '.upsert', payload: item });
        } else if (getUpdatedAt) {
          const a = getUpdatedAt(p);
          const b = getUpdatedAt(item);
          if (a !== b || JSON.stringify(p) !== JSON.stringify(item)) {
            muts.push({ type: typeName + '.upsert', payload: item });
          }
        } else if (JSON.stringify(p) !== JSON.stringify(item)) {
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
      // Operador lido dinamicamente a cada sync — se o usuário trocar o nome
      // no header, as próximas sincronizações já carregam o nome atualizado.
      const operatorName = (window.cdbApp && window.cdbApp.getOperator)
        ? window.cdbApp.getOperator()
        : (localStorage.getItem('cdb-operator-name') || null);
      const resp = await fetch(url, {
        method: 'POST',
        headers: baseHeaders({ 'Content-Type': 'application/json' }),
        body: JSON.stringify({ deviceId, operatorName, mutations: queue })
      });

      if (!resp.ok) {
        console.warn('Sync falhou, status', resp.status);
        return;
      }

      const result = await resp.json().catch(() => ({}));
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

  // ---- Pull: traz mudanças do servidor (outros devices) ----
  async function pull() {
    if (!navigator.onLine) return;
    try {
      const since = localStorage.getItem(LAST_FULL_SYNC_KEY) || '0';
      const url = API_BASE_URL + API_PREFIX + '/sync/pull?since=' + since + '&deviceId=' + encodeURIComponent(deviceId);
      const resp = await fetch(url, { headers: baseHeaders() });
      if (!resp.ok) return;
      const data = await resp.json();
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
      console.log('Mudanças do servidor aplicadas, recarregando...');
      // F6 (estabilizacao): tenta flushar mutacoes locais pendentes ANTES
      // do reload — se o operador acabou de salvar algo offline e nesse
      // momento chegou um pull do servidor, sem isso a mutacao local
      // poderia ficar so na fila e o reload mascararia o estado.
      // flush() ja é idempotente (flushing flag) e respeita online.
      flush().finally(() => setTimeout(() => location.reload(), 500));
    }
  }

  // ---- Hook de persistência: chamado pelo app a cada save ----
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

  // ---- Inicialização ----
  let flushIntervalId = null;
  function stop() {
    if (flushIntervalId) { clearInterval(flushIntervalId); flushIntervalId = null; }
  }
  // Cleanup no unload — em Capacitor o pagehide dispara antes de descarregar
  // a webview; no browser puro tambem cobre fechamento de aba.
  window.addEventListener('pagehide', stop);

  setTimeout(() => {
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
    flush().then(pull);
    // Flush periódico a cada 30s. Guarda o id pra permitir cleanup
    // (ex: navegação SPA, hot-reload do Capacitor) e evitar timer ghost.
    flushIntervalId = setInterval(flush, 30000);
  }, 500);

  // ---- Version ping (Onda 0): verifica compat + capabilities do servidor ----
  // Resposta vai pro localStorage pra Diagnóstico exibir e pra UI
  // condicional (ex: avisar quando ApiKeyEnforced virar true).
  // Falha silenciosa: se offline ou rede ruim, deixa cache antigo.
  async function pingVersion() {
    if (!navigator.onLine) return null;
    try {
      const url = API_BASE_URL + API_PREFIX + '/version';
      const resp = await fetch(url, { headers: baseHeaders() });
      if (!resp.ok) return null;
      const data = await resp.json();
      try {
        localStorage.setItem('cdb-server-version', JSON.stringify({
          fetchedAt: Date.now(),
          ...data
        }));
      } catch (e) {}
      return data;
    } catch (e) {
      console.warn('pingVersion falhou:', e.message);
      return null;
    }
  }

  // ---- Upload de error log (Onda 0): opt-in via Ajustes/Diagnóstico ----
  // Manda o cdb-error-log pro endpoint /diagnostics/errors. Servidor loga
  // via Serilog (sem migration). Não é automático — operador clica.
  async function uploadErrorLog(errors, meta) {
    if (!navigator.onLine) {
      throw new Error('sem rede');
    }
    if (!Array.isArray(errors) || errors.length === 0) {
      throw new Error('sem erros pra enviar');
    }
    const url = API_BASE_URL + API_PREFIX + '/diagnostics/errors';
    const body = {
      deviceId,
      operatorName: (meta && meta.operatorName) || null,
      bundleVersion: (meta && meta.bundleVersion) || null,
      errors: errors.map(e => ({
        timestamp: e.t || Date.now(),
        context: e.ctx || null,
        message: e.msg || null,
        stack: e.stack || null,
        screen: e.screen || null,
        operator: e.operator || null
      }))
    };
    const resp = await fetch(url, {
      method: 'POST',
      headers: baseHeaders({ 'Content-Type': 'application/json' }),
      body: JSON.stringify(body)
    });
    if (!resp.ok) throw new Error('servidor recusou: ' + resp.status);
    return await resp.json().catch(() => ({}));
  }

  // Ping inicial — não bloqueia, dispara em paralelo com primeiro flush.
  setTimeout(pingVersion, 700);

  // Expõe utilitários pra debug
  window.cdbSync = {
    flush, pull, stop,
    pingVersion, uploadErrorLog,
    queueSize: () => loadQueue().length,
    clearQueue: () => { saveQueue([]); updatePendingCount(); },
    deviceId,
    apiBaseUrl: API_BASE_URL
  };
})();
