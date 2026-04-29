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
  // Onda 1 — pareamento de devices.
  // Apos pareamento, server retorna { apiKey, empresaId, lojaId, ... }.
  // Persistimos tudo em cdb-pairing pra usar em todo request subsequente.
  const PAIRING_KEY = 'cdb-pairing';

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

  // Helpers de pareamento
  function loadPairing() {
    try { return JSON.parse(localStorage.getItem(PAIRING_KEY) || 'null'); } catch (e) { return null; }
  }
  function savePairing(p) {
    try { localStorage.setItem(PAIRING_KEY, JSON.stringify(p)); } catch (e) {}
  }
  function clearPairing() {
    try { localStorage.removeItem(PAIRING_KEY); } catch (e) {}
    // Reseta flag de invalido — sem pairing, comportamento volta ao
    // legado (request anonimo). App segue offline-first.
    _pairingInvalid = false;
  }
  function getApiKey() {
    const p = loadPairing();
    return (p && p.apiKey) || null;
  }

  function baseHeaders(extra) {
    const h = Object.assign({ 'X-Device-Id': deviceId }, extra || {});
    const apiKey = getApiKey();
    if (apiKey) h['X-Mobile-Api-Key'] = apiKey;
    return h;
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
  // OFFLINE-FIRST: se servidor indisponivel, fila persiste no localStorage
  // e proximo flush (30s ou quando rede voltar) tenta de novo. App continua
  // funcional sem rede em qualquer cenario.
  let flushing = false;
  // Marcador soft de pairing invalidado (revogado/expirado no server).
  // Não deletamos o pairing local — Felipe pode re-parear sem perder fila.
  // Quando true: stop tentando flush ate operador re-parear OU clearPairing.
  let _pairingInvalid = false;
  async function flush() {
    if (flushing) return;
    if (_pairingInvalid) return; // backoff até re-parear
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
      // AbortController com timeout 15s — server pendurado nao trava o app.
      const ctrl = new AbortController();
      const timeoutId = setTimeout(() => ctrl.abort(), 15000);
      let resp;
      try {
        resp = await fetch(url, {
          method: 'POST',
          headers: baseHeaders({ 'Content-Type': 'application/json' }),
          body: JSON.stringify({ deviceId, operatorName, mutations: queue }),
          signal: ctrl.signal
        });
      } finally {
        clearTimeout(timeoutId);
      }

      if (resp.status === 401) {
        // Pairing revogado/expirado. Marca em memoria, para de tentar.
        // Operador vai precisar re-parear pelo Diagnostico.
        _pairingInvalid = true;
        console.warn('Sync rejeitou auth — pairing invalido, re-parear pelo Diagnostico');
        if (window.cdbApp && window.cdbApp.onPairingInvalid) {
          try { window.cdbApp.onPairingInvalid(); } catch (e) {}
        }
        return;
      }
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

      // Onda 5: trata conflicts. Server retorna rejected[] com reason
      // começando com "conflict:" quando outro device sincronizou primeiro.
      // PWA mostra toast + força pull pra refletir versão mais nova.
      if (result.rejected && Array.isArray(result.rejected) && result.rejected.length > 0) {
        const conflicts = result.rejected.filter(r => r.reason && r.reason.indexOf('conflict:') === 0);
        if (conflicts.length > 0) {
          // Remove os conflitados da fila (não vão ser aceitos mesmo)
          const conflictIds = new Set(conflicts.map(c => c.id));
          const remaining = loadQueue().filter(m => !conflictIds.has(m.id));
          saveQueue(remaining);
          // Notifica o app pra exibir UX e logar
          if (window.cdbApp && typeof window.cdbApp.onSyncConflict === 'function') {
            try { window.cdbApp.onSyncConflict(conflicts); } catch (e) {}
          }
          // Force pull pra alinhar com servidor
          setTimeout(pull, 200);
        }
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
  // OFFLINE-FIRST: silencioso quando offline ou pairing invalido.
  async function pull() {
    if (!navigator.onLine) return;
    if (_pairingInvalid) return;
    try {
      const since = localStorage.getItem(LAST_FULL_SYNC_KEY) || '0';
      const url = API_BASE_URL + API_PREFIX + '/sync/pull?since=' + since + '&deviceId=' + encodeURIComponent(deviceId);
      const ctrl = new AbortController();
      const timeoutId = setTimeout(() => ctrl.abort(), 15000);
      let resp;
      try {
        resp = await fetch(url, { headers: baseHeaders(), signal: ctrl.signal });
      } finally {
        clearTimeout(timeoutId);
      }
      if (resp.status === 401) { _pairingInvalid = true; return; }
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
    flush().then(pull).then(fetchAndProcessCommands).then(startRealtime).then(maybeAutoBackup);
  });
  window.addEventListener('offline', stopRealtime);
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
    flush().then(pull).then(fetchAndProcessCommands).then(startRealtime).then(maybeAutoBackup);
    // Flush periódico a cada 30s. Guarda o id pra permitir cleanup
    // (ex: navegação SPA, hot-reload do Capacitor) e evitar timer ghost.
    // Combina flush + commands no mesmo tick — economia de battery.
    flushIntervalId = setInterval(function () {
      flush();
      fetchAndProcessCommands();
    }, 30000);
  }, 500);

  // ---- Version ping (Onda 0): verifica compat + capabilities do servidor ----
  // Resposta vai pro localStorage pra Diagnóstico exibir e pra UI
  // condicional (ex: avisar quando ApiKeyEnforced virar true).
  // Falha silenciosa: se offline ou rede ruim, deixa cache antigo.
  // ---- Backup automatico (Onda 8) ----
  // Coleta todas as chaves cdb-* do localStorage e envia pro servidor.
  // Auto-trigger 1x/dia (rastreado em cdb-last-backup-at). Manual via
  // Diagnostico. Servidor mantem os 7 ultimos por device.
  const LAST_BACKUP_KEY = 'cdb-last-backup-at';
  const BACKUP_INTERVAL_MS = 24 * 60 * 60 * 1000; // 24h

  function _collectSnapshot() {
    const snap = {};
    try {
      for (let i = 0; i < localStorage.length; i++) {
        const k = localStorage.key(i);
        if (k && k.indexOf('cdb-') === 0) {
          // Pula sensiveis: api-key (do pairing) e bt-trail (debug verboso).
          if (k === 'cdb-pairing' || k === 'cdb-bt-trail') continue;
          snap[k] = localStorage.getItem(k);
        }
      }
    } catch (e) {}
    return snap;
  }

  async function uploadBackup(note) {
    if (!navigator.onLine) throw new Error('sem rede');
    if (!getApiKey()) throw new Error('nao pareado');
    const snap = _collectSnapshot();
    const json = JSON.stringify({
      schema: 'cdb-backup-v1',
      capturedAt: Date.now(),
      deviceId,
      data: snap
    });
    const url = API_BASE_URL + API_PREFIX + '/devices/me/backup';
    const ctrl = new AbortController();
    const timeoutId = setTimeout(() => ctrl.abort(), 30000);
    let resp;
    try {
      resp = await fetch(url, {
        method: 'POST',
        headers: baseHeaders({ 'Content-Type': 'application/json' }),
        body: JSON.stringify({
          snapshotJson: json,
          bundleVersion: (window.__BUNDLE_INFO__ && window.__BUNDLE_INFO__.version) || null,
          operatorName: (window.cdbApp && window.cdbApp.getOperator)
            ? window.cdbApp.getOperator()
            : null,
          note: note || 'auto'
        }),
        signal: ctrl.signal
      });
    } finally { clearTimeout(timeoutId); }
    if (resp.status === 401) { _pairingInvalid = true; throw new Error('pareamento invalidado'); }
    if (!resp.ok) throw new Error('servidor recusou: ' + resp.status);
    const data = await resp.json();
    try { localStorage.setItem(LAST_BACKUP_KEY, String(Date.now())); } catch (e) {}
    return data;
  }

  // Tenta backup automatico se passou >24h desde o ultimo. Falha silenciosa.
  async function maybeAutoBackup() {
    if (!navigator.onLine) return;
    if (!getApiKey()) return;
    if (_pairingInvalid) return;
    let last = 0;
    try { last = parseInt(localStorage.getItem(LAST_BACKUP_KEY) || '0', 10) || 0; } catch (e) {}
    if (Date.now() - last < BACKUP_INTERVAL_MS) return;
    try {
      await uploadBackup('auto');
      console.log('[backup] auto OK');
    } catch (e) {
      try { console.warn('[backup] auto falhou:', e && e.message); } catch (_) {}
    }
  }

  // ---- Multi-loja (Onda 6) ----
  // App pareado pode trocar de loja sem re-parear. Empresa fixa (vem
  // do pareamento original); só lojaId muda. Após trocar, força flush
  // + pull pra alinhar dados da loja nova (estoque, pedidos, etc).
  async function listLojasDisponiveis() {
    if (!navigator.onLine) throw new Error('sem rede');
    if (!getApiKey()) throw new Error('nao pareado');
    const url = API_BASE_URL + API_PREFIX + '/devices/me/lojas-disponiveis';
    const ctrl = new AbortController();
    const timeoutId = setTimeout(() => ctrl.abort(), 10000);
    try {
      const resp = await fetch(url, { headers: baseHeaders(), signal: ctrl.signal });
      if (resp.status === 401) { _pairingInvalid = true; throw new Error('pareamento invalidado'); }
      if (!resp.ok) throw new Error('servidor recusou: ' + resp.status);
      return await resp.json();
    } finally { clearTimeout(timeoutId); }
  }

  async function switchLoja(lojaId) {
    if (!navigator.onLine) throw new Error('sem rede');
    if (!getApiKey()) throw new Error('nao pareado');
    if (!lojaId) throw new Error('lojaId obrigatorio');
    const url = API_BASE_URL + API_PREFIX + '/devices/me/switch-loja';
    const ctrl = new AbortController();
    const timeoutId = setTimeout(() => ctrl.abort(), 10000);
    let resp;
    try {
      resp = await fetch(url, {
        method: 'POST',
        headers: baseHeaders({ 'Content-Type': 'application/json' }),
        body: JSON.stringify({ lojaId }),
        signal: ctrl.signal
      });
    } finally { clearTimeout(timeoutId); }
    if (resp.status === 401) { _pairingInvalid = true; throw new Error('pareamento invalidado'); }
    if (!resp.ok) {
      let msg = 'falha ao trocar loja';
      try { const e = await resp.json(); if (e && e.error) msg = e.error; } catch (_) {}
      throw new Error(msg);
    }
    const data = await resp.json();
    // Atualiza pairing local com nova loja
    if (data.changed) {
      const p = loadPairing();
      if (p) {
        p.lojaId = data.lojaId;
        p.label = p.label; // preserva
        savePairing(p);
      }
      // Drena fila atual + pega dados da loja nova
      try { await flush(); } catch (_) {}
      try { await pull(); } catch (_) {}
      // Reinicia SSE pra pegar grupo da nova loja
      try { stopRealtime(); startRealtime(); } catch (_) {}
    }
    return data;
  }

  // ---- Realtime via SSE (Onda 5) ----
  // Conecta no /api/mobile/operation/stream?apiKey=... e escuta eventos
  // server-pushed. Quando outro device da mesma loja sincroniza, recebe
  // evento mutations-applied e faz pull imediato (sem esperar 30s).
  //
  // FAIL-SAFE: se servidor offline / pairing inválido / browser sem
  // EventSource, app continua funcionando com polling 30s normal.
  // Reconnect automatico do EventSource em caso de drop.
  let _sseSource = null;
  function startRealtime() {
    if (_sseSource) return; // já conectado
    if (!navigator.onLine) return;
    if (typeof EventSource === 'undefined') return; // browser sem suporte
    const apiKey = getApiKey();
    if (!apiKey) return; // só pareados
    try {
      const url = API_BASE_URL + API_PREFIX + '/operation/stream?apiKey=' + encodeURIComponent(apiKey);
      _sseSource = new EventSource(url);
      _sseSource.onmessage = function (ev) {
        try {
          const data = JSON.parse(ev.data);
          if (data && data.type === 'mutations-applied') {
            // Outro device sincronizou — puxa atualizações.
            console.log('[realtime] mutations-applied de', data.originDeviceId);
            pull();
          } else if (data && data.type === 'command-queued') {
            console.log('[realtime] command-queued', data.commandType);
            fetchAndProcessCommands();
          }
        } catch (e) {}
      };
      _sseSource.onerror = function () {
        // EventSource reconecta sozinho. Só loga; polling 30s segue como fallback.
        try { console.warn('[realtime] erro SSE — fallback pra polling'); } catch (_) {}
      };
    } catch (e) {
      try { console.warn('[realtime] startRealtime falhou:', e && e.message); } catch (_) {}
      _sseSource = null;
    }
  }
  function stopRealtime() {
    if (_sseSource) {
      try { _sseSource.close(); } catch (e) {}
      _sseSource = null;
    }
  }

  // ---- Comandos remotos (Onda 4) ----
  // Servidor pode enfileirar comandos pra este device (flush_now, pull_now,
  // reload, message). Processamos sempre que online + pareado, depois do
  // ciclo flush+pull. Falha silenciosa pra não interferir offline-first.
  async function fetchAndProcessCommands() {
    if (!navigator.onLine) return;
    if (_pairingInvalid) return;
    if (!getApiKey()) return; // só pareados recebem comandos
    try {
      const url = API_BASE_URL + API_PREFIX + '/operation/pending-commands';
      const ctrl = new AbortController();
      const timeoutId = setTimeout(() => ctrl.abort(), 10000);
      let resp;
      try {
        resp = await fetch(url, { headers: baseHeaders(), signal: ctrl.signal });
      } finally {
        clearTimeout(timeoutId);
      }
      if (resp.status === 401) { _pairingInvalid = true; return; }
      if (!resp.ok) return;
      const cmds = await resp.json();
      if (!Array.isArray(cmds) || cmds.length === 0) return;
      cmds.forEach(executeRemoteCommand);
    } catch (e) {
      try { console.warn('fetchCommands falhou:', e && e.message); } catch (_) {}
    }
  }

  function executeRemoteCommand(cmd) {
    if (!cmd || !cmd.commandType) return;
    const type = String(cmd.commandType).toLowerCase();
    try {
      if (type === 'flush_now') {
        flush();
      } else if (type === 'pull_now') {
        pull();
      } else if (type === 'reload') {
        // Flush antes de reload pra não perder fila local
        flush().finally(() => setTimeout(() => location.reload(), 800));
      } else if (type === 'message') {
        let text = '(mensagem do gestor)';
        try {
          const p = cmd.payloadJson ? JSON.parse(cmd.payloadJson) : null;
          if (p && p.text) text = String(p.text).slice(0, 200);
        } catch (_) {}
        if (window.cdbApp && typeof window.cdbApp.showToast === 'function') {
          try { window.cdbApp.showToast(text); } catch (_) {}
        } else if (typeof window.showToast === 'function') {
          try { window.showToast(text); } catch (_) {}
        }
      }
    } catch (e) {
      console.warn('Comando remoto falhou:', type, e && e.message);
    }
  }

  async function pingVersion() {
    if (!navigator.onLine) return null;
    try {
      const url = API_BASE_URL + API_PREFIX + '/version';
      const ctrl = new AbortController();
      const timeoutId = setTimeout(() => ctrl.abort(), 8000);
      let resp;
      try {
        resp = await fetch(url, { headers: baseHeaders(), signal: ctrl.signal });
      } finally {
        clearTimeout(timeoutId);
      }
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
      // Sem rede / timeout / DNS / TLS — silencioso. PWA continua offline.
      try { console.warn('pingVersion falhou:', e && e.message); } catch (_) {}
      return null;
    }
  }

  // ---- Pareamento (Onda 1) ----
  // Troca codigo de 6 digitos por apiKey + contexto (empresa/loja).
  // Ao pareamento bem-sucedido, persiste em cdb-pairing e proximas chamadas
  // ja saem com X-Mobile-Api-Key automaticamente.
  async function pairWithCode(code, label) {
    if (!navigator.onLine) throw new Error('sem rede');
    const url = API_BASE_URL + API_PREFIX + '/devices/pair';
    const ctrl = new AbortController();
    const timeoutId = setTimeout(() => ctrl.abort(), 12000);
    let resp;
    try {
      resp = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-Device-Id': deviceId },
        body: JSON.stringify({
          pairingCode: String(code || '').trim(),
          deviceId,
          label: label || null
        }),
        signal: ctrl.signal
      });
    } catch (e) {
      throw new Error('servidor inacessivel');
    } finally {
      clearTimeout(timeoutId);
    }
    if (!resp.ok) {
      let msg = 'codigo invalido';
      try { const e = await resp.json(); if (e && e.error) msg = e.error; } catch (_) {}
      throw new Error(msg);
    }
    const data = await resp.json();
    savePairing({
      apiKey: data.apiKey,
      empresaId: data.empresaId,
      lojaId: data.lojaId,
      label: data.label,
      defaultOperatorName: data.defaultOperatorName,
      pairedAt: data.pairedAt,
      deviceId: data.deviceId
    });
    // Pareou com sucesso — destrava flush/pull caso estavam bloqueados
    // por pairing invalidado em sessao anterior.
    _pairingInvalid = false;
    // Re-pinga version pra atualizar info no diagnostico
    try { await pingVersion(); } catch (_) {}
    // Tenta drenar fila imediatamente — pode ter mutations acumuladas
    // do periodo offline / sem pairing.
    try { flush(); } catch (_) {}
    return data;
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

  // Expõe utilitários pra debug + integração com Diagnóstico
  window.cdbSync = {
    flush, pull, stop,
    pingVersion, uploadErrorLog,
    pairWithCode,
    getPairing: loadPairing,
    clearPairing,
    isPairingInvalid: () => _pairingInvalid,
    fetchCommands: fetchAndProcessCommands,
    startRealtime, stopRealtime,
    realtimeConnected: () => _sseSource && _sseSource.readyState === 1,
    listLojasDisponiveis, switchLoja,
    uploadBackup, maybeAutoBackup,
    lastBackupAt: () => parseInt(localStorage.getItem(LAST_BACKUP_KEY) || '0', 10) || 0,
    queueSize: () => loadQueue().length,
    clearQueue: () => { saveQueue([]); updatePendingCount(); },
    deviceId,
    apiBaseUrl: API_BASE_URL
  };
})();
