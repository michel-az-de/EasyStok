// Casa da Baba - Sync Layer
// Espelha o estado local pro backend via REST. Tolera offline.
//
// Configuração:
//  - API_BASE_URL: base da API. Padrão: mesmo host que serve o PWA.
//    Para rodar num APK empacotado apontando pra backend remoto,
//    setar window.CDB_CONFIG = { apiBaseUrl: 'https://easystock.exemplo.com' }
//    ANTES de carregar este script (via config.js ou capacitor.config).
//
// Offline-first: toda mutação vai pra uma fila persistente (IndexedDB primário,
// localStorage fallback). Fila é drenada quando a rede volta ou a cada 30s.
// O app funciona 100% sem rede — sync é oportunístico, nunca bloqueante.
// F10-C-2: queue-store.js provê IDB wrapper com migração dual-read.

(function () {
  'use strict';

  const CFG = (window.CDB_CONFIG || {});
  const API_BASE_URL = CFG.apiBaseUrl ?? '';  // vazio = mesmo host
  const API_PREFIX = '/api/mobile';
  const DEVICE_ID_KEY = 'cdb-device-id';
  const QUEUE_KEY = 'cdb-sync-queue';
  const LAST_FULL_SYNC_KEY = 'cdb-last-sync';
  // B4: versao do formato de mutations enfileiradas. Independente de
  // PWA_REQUIRED_API_VERSION (que aponta pro schema do server). Quando este
  // valor bumpar, mutations enfileiradas em formato anterior devem passar
  // por mutationMigrators antes de retentativa. Server sinaliza necessidade
  // via rejected.reason = 'migrate:N' (N = versao alvo).
  const PWA_MUTATION_SCHEMA_VERSION = 1;

  // ───────────────────────────────────────────────────────────────────────
  // CONTINGÊNCIA — Trace estruturado + auto-diagnóstico
  // ───────────────────────────────────────────────────────────────────────
  // Cada momento crítico (boot, auto-pair, pull, flush, ticket-auto) registra
  // em `cdb-sync-trace` (round-buffer 200 eventos). Operador exporta via
  // Diagnóstico → "Copiar trace" e cola direto em chat/ticket. Evita ficar
  // dependendo de `fly logs` ou Render dashboard pra diagnosticar problema
  // de instalação. Cada entry: { t (ms), cat, msg, data?, ok?: bool }.
  const TRACE_KEY = 'cdb-sync-trace';
  const TRACE_MAX = 200;
  function _trace(cat, msg, data) {
    try {
      const entry = { t: Date.now(), cat: String(cat || '').slice(0, 40),
                      msg: String(msg || '').slice(0, 240) };
      if (data !== undefined && data !== null) {
        try { entry.data = JSON.parse(JSON.stringify(data)); }
        catch (_) { entry.data = String(data).slice(0, 400); }
      }
      const arr = JSON.parse(localStorage.getItem(TRACE_KEY) || '[]');
      arr.push(entry);
      if (arr.length > TRACE_MAX) arr.splice(0, arr.length - TRACE_MAX);
      localStorage.setItem(TRACE_KEY, JSON.stringify(arr));
    } catch (_) { /* localStorage cheio ou bug: ignora — trace é best-effort */ }
    try { console.log('[cdb-trace]', cat, msg, data || ''); } catch (_) {}
  }
  function getTrace() {
    try { return JSON.parse(localStorage.getItem(TRACE_KEY) || '[]'); }
    catch (_) { return []; }
  }
  function clearTrace() {
    try { localStorage.removeItem(TRACE_KEY); } catch (_) {}
  }
  function exportTraceText() {
    const arr = getTrace();
    const cfg = window.CDB_CONFIG || {};
    const pair = (function () {
      try { return JSON.parse(localStorage.getItem('cdb-pairing') || 'null'); }
      catch (_) { return null; }
    })();
    const header = [
      '=== CDB SYNC TRACE ===',
      'exportado: ' + new Date().toISOString(),
      'deviceId: ' + (localStorage.getItem(DEVICE_ID_KEY) || '(none)'),
      'apiBaseUrl: ' + (cfg.apiBaseUrl || '(mesmo host)'),
      'configLoaded: ' + (typeof window.CDB_CONFIG !== 'undefined'),
      'hasProvisioning: ' + (!!cfg.forcedProvisioningSecret),
      'hasPairingCode: ' + (!!cfg.forcedPairingCode),
      'paired: ' + (!!pair),
      'lastSync: ' + (localStorage.getItem(LAST_FULL_SYNC_KEY) || '(none)'),
      'queueLen: ' + (function () { try { return JSON.parse(localStorage.getItem(QUEUE_KEY) || '[]').length; } catch (_) { return -1; } })(),
      'userAgent: ' + (navigator.userAgent || '').slice(0, 200),
      ''
    ].join('\n');
    const lines = arr.map(e => {
      const ts = new Date(e.t).toISOString();
      const dataStr = e.data ? ' ' + JSON.stringify(e.data) : '';
      return '[' + ts + '] [' + e.cat + '] ' + e.msg + dataStr;
    }).join('\n');
    return header + (lines || '(trace vazio)');
  }

  // Pré-check no boot: detecta falhas catastróficas do empacotamento
  // (config.js não carregou, secret ausente em APK que deveria ter etc) e
  // registra com sinal explícito antes de qualquer tentativa de pareamento.
  (function _preCheck() {
    _trace('boot', 'sync.js iniciando', {
      hasConfig: typeof window.CDB_CONFIG !== 'undefined',
      apiBaseUrl: CFG.apiBaseUrl || '(mesmo host)',
      hasProvisioning: !!CFG.forcedProvisioningSecret,
      hasPairingCode: !!CFG.forcedPairingCode
    });
    if (typeof window.CDB_CONFIG === 'undefined') {
      _trace('boot', 'CRITICO: window.CDB_CONFIG undefined — config.js nao carregou. APK pode estar corrompido ou index.html sem <script src="config.js">.', null);
    } else if (!CFG.apiBaseUrl) {
      _trace('boot', 'AVISO: apiBaseUrl vazio — sync vai usar mesmo host. Em APK isto significa http://localhost (nao tem backend).', null);
    }
  })();

  // D2: detecta WebView sem Service Worker. Acontece em Android WebView
  // antigo, em modo privacy/incognito de alguns browsers, ou em ambientes
  // de teste (Node VM). Modo degradado: cache offline via SW ausente, mas
  // o sync.js continua funcional (fila persiste em localStorage/IDB).
  // Heartbeat reporta degraded=true pra Diagnostico alertar operador a
  // recarregar via HTTPS / atualizar WebView.
  const _swSupported = (function () {
    try {
      return typeof navigator !== 'undefined'
        && typeof navigator.serviceWorker !== 'undefined'
        && typeof navigator.serviceWorker.getRegistration === 'function';
    } catch (_) { return false; }
  })();
  if (!_swSupported) {
    try { console.warn('[degraded] Service Worker indisponivel — cache offline ausente, sync funciona normalmente'); } catch (_) {}
  }
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
    // INVARIANTE: clearPairing remove APENAS PAIRING_KEY (cdb-pairing).
    // NAO toca em cdb-sync-queue (fila offline), cdb-device-id (identidade
    // do device), cdb-products / cdb-orders / cdb-batches / cdb-cash etc
    // (dados de negocio), cdb-last-* (timestamps), cdb-pwa-installed-version
    // (versao OTA), nem qualquer outra chave cdb-*.
    //
    // Re-pareamento mantem fila intacta: mutations enfileiradas durante o
    // periodo offline sobem assim que pairWithCode salvar nova apiKey e
    // disparar flush automatico. Apagar a fila aqui = perder vendas.
    // Mudancas neste invariante DEVEM atualizar tests/pairing.test.js
    // ("clearPairing preserva fila").
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

  // ---- B3: ServerTimeOffset + monotonic ts ----
  // PROBLEMA: enqueue carimba mutations com Date.now() local. Se o operador
  // adulterar o relogio do device (ou bateria descarrega e o RTC reseta),
  // mutations carimbadas pra TRAS podem perder em conflict-resolution
  // last-write-wins. Drama silencioso: edicao recente foi "reescrita" por
  // mutation com ts no passado.
  //
  // MITIGACAO: a cada response 2xx do servidor, capturamos o header Date e
  // calculamos offset (serverTs - localTs). Usamos esse offset em todas as
  // futuras chamadas a nowSafe(). Adicionalmente, garantimos monotonia:
  // nowSafe() nunca retorna valor < _lastSeenServerTs + 1, mesmo se o relogio
  // voltou. Isto blinda contra adulteracao e clock drift.
  //
  // Persistencia: offset e last-seen-server-ts ficam em localStorage pra
  // sobreviver reload (sem isso, primeira mutation pos-boot seria carimbada
  // com Date.now() puro ate o primeiro fetch responder).
  const SERVER_OFFSET_KEY = 'cdb-server-time-offset-ms';
  const LAST_SEEN_SERVER_TS_KEY = 'cdb-last-seen-server-ts';
  let _serverTimeOffsetMs = (function () {
    try { return parseInt(localStorage.getItem(SERVER_OFFSET_KEY) || '0', 10) || 0; } catch (_) { return 0; }
  })();
  let _lastSeenServerTs = (function () {
    try { return parseInt(localStorage.getItem(LAST_SEEN_SERVER_TS_KEY) || '0', 10) || 0; } catch (_) { return 0; }
  })();

  function updateServerTimeFromResponse(resp) {
    try {
      if (!resp || !resp.headers || typeof resp.headers.get !== 'function') return;
      const dateHeader = resp.headers.get('Date');
      if (!dateHeader) return;
      const serverTs = Date.parse(dateHeader);
      if (!isFinite(serverTs)) return;
      _serverTimeOffsetMs = serverTs - Date.now();
      if (serverTs > _lastSeenServerTs) _lastSeenServerTs = serverTs;
      try {
        localStorage.setItem(SERVER_OFFSET_KEY, String(_serverTimeOffsetMs));
        localStorage.setItem(LAST_SEEN_SERVER_TS_KEY, String(_lastSeenServerTs));
      } catch (_) {}
    } catch (_) {}
  }

  function nowSafe() {
    const candidate = Date.now() + _serverTimeOffsetMs;
    // Monotonia: relogio voltou ou nao temos ack do server ainda? Garante
    // que ts cresce sempre. +1 evita empate exato com ultima leitura.
    if (candidate <= _lastSeenServerTs) {
      _lastSeenServerTs = _lastSeenServerTs + 1;
      return _lastSeenServerTs;
    }
    _lastSeenServerTs = candidate;
    return candidate;
  }

  // ---- Fila persistente ----
  // F10-C-2: bridge sincrona pra cdbQueueStore (IDB async).
  // _queueCache mantém cópia em memória pra chamadas sync (loadQueue/saveQueue)
  // — IDB é atualizado em background. Boot chama _initQueueStore() pra
  // popular o cache. Fallback: localStorage direto se IDB indisponível.
  let _queueCache = null; // null = não inicializado; [] = vazio
  let _queueStoreReady = false;

  async function _initQueueStore() {
    try {
      if (typeof window.cdbQueueStore !== 'undefined') {
        const result = await window.cdbQueueStore.init();
        _queueStoreReady = true;
        _queueCache = await window.cdbQueueStore.loadAll();
        _trace('boot', 'queue-store inicializado', { storage: result.storage, items: _queueCache.length, degraded: result.degraded });
        return result;
      }
    } catch (e) {
      _trace('boot', 'queue-store init falhou, usando localStorage', { error: e && e.message });
    }
    // Fallback: carregar de localStorage
    _queueCache = _loadQueueLS();
    _queueStoreReady = true;
    return { storage: 'localStorage', degraded: true };
  }

  function _loadQueueLS() {
    try { return JSON.parse(localStorage.getItem(QUEUE_KEY) || '[]'); } catch (e) { return []; }
  }

  function loadQueue() {
    if (_queueCache !== null) return _queueCache;
    return _loadQueueLS();
  }

  function saveQueue(q) {
    _queueCache = q;
    // Persist to IDB (async, fire-and-forget) + localStorage (sync fallback)
    try {
      localStorage.setItem(QUEUE_KEY, JSON.stringify(q));
    } catch (e) {
      if (e && (e.name === 'QuotaExceededError' || e.code === 22)) {
        console.error('[sync] localStorage quota excedido — fila nao salva, conecte e sincronize!');
        try {
          if (window.cdbApp && typeof window.cdbApp.showToast === 'function') {
            window.cdbApp.showToast('Memória local cheia. Conecte à internet para sincronizar.', 'error');
          }
          if (navigator.onLine) setTimeout(flush, 500);
        } catch (_) {}
      }
    }
    // Async persist to IDB
    if (_queueStoreReady && typeof window.cdbQueueStore !== 'undefined' && !window.cdbQueueStore.degraded) {
      window.cdbQueueStore.saveAll(q).catch(function (e) {
        console.warn('[sync] IDB saveAll falhou:', e && e.message);
      });
    }
  }

  // Snapshot do estado anterior pra calcular delta
  let lastSnapshot = null;

  // ---- A6: strip de bytes pesados antes de enqueue ----
  // Batches carregam fotos do lote (batchPhoto) e fotos por item (items[].photo).
  // Quando o operador tira foto pela camera do device, o data-URL base64 fica
  // de 1MB a 5MB por foto. Uma producao com 8 fotos = 30MB facil, e a fila
  // cdb-sync-queue mora em localStorage (cap 5MB total). Resultado: quota
  // estoura, saveQueue cai no catch, mutations perdidas em silencio.
  //
  // Estrategia: strip os bytes ANTES de enfileirar, mantendo apenas um hash
  // (FNV-1a + tamanho) como referencia. O estado local (cdb-batches) preserva
  // as fotos completas — operador segue vendo as imagens no app. So a copia
  // que vai pra fila e' enxuta. Quando o photo-store IDB (A7) + endpoint
  // POST /batches/{id}/photos (A8) entrarem, o upload separado usa o hash
  // pra endereçar a foto no IDB local e mandar binario.
  //
  // TODO A7+A8: implementar photo-store.js IDB + endpoint upload. Enquanto
  // isso, fotos NAO sincronizam entre devices — apenas no device que tirou.
  // Aceitavel temporariamente: melhor que perder a fila inteira por quota.
  function simpleHash(s) {
    // FNV-1a 32-bit. Barato e suficiente pra identificar a foto.
    // Nao e cripto, e nao precisa ser.
    let h = 0x811c9dc5;
    for (let i = 0; i < s.length; i++) {
      h ^= s.charCodeAt(i);
      h = (h * 0x01000193) >>> 0;
    }
    return 'fnv1a:' + h.toString(16) + ':' + s.length;
  }

  function isHeavyDataUrl(v) {
    // Trata como pesado qualquer data-URL acima de 4KB (cobre fotos reais
    // e ignora pequenos icones SVG / placeholders inline).
    return typeof v === 'string' && v.length > 4096 && v.indexOf('data:') === 0;
  }

  function stripBatchPhotoBytes(batch) {
    if (!batch || typeof batch !== 'object') return batch;
    // Deep clone pra nao mutar o estado local — operador segue vendo as fotos.
    const clone = JSON.parse(JSON.stringify(batch));
    if (isHeavyDataUrl(clone.batchPhoto)) {
      const hash = simpleHash(clone.batchPhoto);
      clone.batchPhotoHash = hash;
      // F10-C-7: persiste foto no photo-store IDB antes de descartar bytes.
      _persistPhotoAsync(hash, clone.batchPhoto, batch.id || '', 'batchPhoto');
      delete clone.batchPhoto;
    }
    if (Array.isArray(clone.items)) {
      clone.items = clone.items.map((item, idx) => {
        if (item && isHeavyDataUrl(item.photo)) {
          const out = Object.assign({}, item);
          const hash = simpleHash(item.photo);
          out.photoHash = hash;
          // F10-C-7: persiste foto do item no photo-store IDB.
          _persistPhotoAsync(hash, item.photo, batch.id || '', 'item:' + idx);
          delete out.photo;
          return out;
        }
        return item;
      });
    }
    return clone;
  }

  // F10-C-7: fire-and-forget persist de foto no IDB photo-store.
  // Falha silenciosa — se IDB indisponivel, foto se perde (mesmo
  // comportamento de antes do F10-C-7, mas agora com tentativa de salvar).
  function _persistPhotoAsync(hash, dataUrl, batchId, field) {
    try {
      if (window.cdbPhotoStore && window.cdbPhotoStore.ready) {
        window.cdbPhotoStore.save(hash, dataUrl, batchId, field).catch(function (e) {
          console.warn('[sync] photo-store save failed:', hash, e && e.message);
        });
      }
    } catch (e) {}
  }

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
    // A6: batches stripadas de bytes pesados (fotos base64) antes do diff.
    // O prev snapshot ja foi guardado stripado em cdbOnPersist, entao a
    // comparacao bate. Diff sem strip causava enfileiramento de payloads
    // multi-MB que estouravam localStorage.
    diffCollection(curr.batches.map(stripBatchPhotoBytes), prev && prev.batches, 'batch');

    return muts;
  }

  // ---- F10-C-1: gerador de UUID v4 estavel pra mutation IDs ----
  // crypto.randomUUID e' garantido em Chrome 92+/Safari 15.4+/Firefox 95+.
  // Capacitor 6 minimum Chrome 100. Fallback formata bytes de
  // crypto.getRandomValues pra v4 — mesma fonte de entropia,
  // criptograficamente seguro, zero colisao em volumes realistas
  // (122 bits aleatorios).
  function generateMutationUuid() {
    try {
      if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
        return crypto.randomUUID();
      }
    } catch (_) {}
    // Fallback v4: 16 bytes random, set version 4 + variant 10xx
    try {
      const buf = new Uint8Array(16);
      crypto.getRandomValues(buf);
      buf[6] = (buf[6] & 0x0f) | 0x40; // version 4
      buf[8] = (buf[8] & 0x3f) | 0x80; // variant 10
      const hex = Array.from(buf, b => b.toString(16).padStart(2, '0')).join('');
      return hex.substring(0,8) + '-' + hex.substring(8,12) + '-' +
             hex.substring(12,16) + '-' + hex.substring(16,20) + '-' +
             hex.substring(20);
    } catch (_) {
      // Ultimo fallback (caso crypto.getRandomValues nao exista): timestamp
      // + Math.random. Inferior, mas operacao continua funcionando.
      return 'fb-' + Date.now() + '-' +
             Math.random().toString(36).substr(2, 8) +
             Math.random().toString(36).substr(2, 8);
    }
  }

  // ---- Enfileira mutations ----
  function enqueue(mutations) {
    if (!mutations.length) return;
    // F10-C-5: bloqueia enqueues nao-criticas durante OTA.
    if (_mutationsBlocked) {
      mutations = mutations.filter(function (m) {
        return CRITICAL_MUTATION_TYPES[m.type] === true;
      });
      if (!mutations.length) return;
    }
    let queue = loadQueue();
    mutations.forEach(m => {
      // Dedup local: se ja tem mutation pendente pro mesmo (type, payload.id),
      // descarta as antigas — servidor faz upsert idempotente last-write-wins
      // entao mandar 5 versoes seguidas do mesmo pedido (aguardando -> preparando
      // -> pronto) gasta payload/bateria sem ganho. Mutations sem payload.id
      // (caso degenerado) passam direto, mantendo comportamento anterior.
      if (m.payload && m.payload.id) {
        queue = queue.filter(q =>
          !(q.type === m.type && q.payload && q.payload.id === m.payload.id));
      }
      queue.push({
        // F10-C-1: UUID v4 via crypto.randomUUID (Chrome 92+, garantido no
        // Capacitor 6 com Chrome 100+). Fallback usa crypto.getRandomValues
        // formatado pra v4 — mesma fonte de entropia, sem Math.random fraco.
        // Prefixo 'mut_' facilita filtro em logs e diferencia de UUIDs do server.
        id: 'mut_' + generateMutationUuid(),
        deviceId,
        type: m.type,
        payload: m.payload,
        // B3: nowSafe() = Date.now() + serverTimeOffset, com garantia
        // monotonica vs _lastSeenServerTs. Protege contra adulteracao de
        // relogio do device — last-write-wins fica honesto.
        ts: nowSafe(),
        // B4: schemaVersion em cada item permite que o servidor sinalize
        // "migra pra vN antes de aplicar" via rejected:migrate:N. Sem isto,
        // bump do MobileSchemaVersion no server tornava mutations enfileiradas
        // em formato antigo lixo silencioso (server rejeita com generic 4xx).
        schemaVersion: PWA_MUTATION_SCHEMA_VERSION
      });
    });
    saveQueue(queue);
    updatePendingCount();
  }

  function updatePendingCount() {
    const q = loadQueue();
    if (window.cdbApp) window.cdbApp.setPendingSync(q.length);
  }

  // ---- B4: schema migration de mutations ----
  // Registry de transformers: { targetVersion: (mutation) => migratedMutation }.
  // Inicialmente vazio porque PWA_MUTATION_SCHEMA_VERSION=1 e' o primeiro.
  // Quando bumpar pra 2 (ex: renomeacao de campo em order.upsert), registre
  // mutationMigrators[2] = m => { ...m, payload: rename(m.payload), schemaVersion: 2 };
  //
  // Migrators sao deterministicos e idempotentes — chamar duas vezes da o mesmo
  // resultado. Server sinaliza versao alvo via rejected.reason='migrate:N'.
  const mutationMigrators = {};

  function applyMutationMigrationsAndRequeue(rejectedItems) {
    const queue = loadQueue();
    const byId = new Map(queue.map(m => [m.id, m]));
    let migratedCount = 0, droppedCount = 0;
    const toDrop = new Set();
    const toMigrate = [];

    for (const r of rejectedItems) {
      const orig = byId.get(r.mutationId);
      if (!orig) { droppedCount++; continue; } // ja saiu da fila
      const target = parseInt(String(r.reason).split(':')[1] || '0', 10);
      const migrator = mutationMigrators[target];
      if (!migrator) {
        try { console.warn('[migrate] sem migrator pra v' + target + ' — descartando mutation', r.mutationId); } catch (_) {}
        toDrop.add(r.mutationId);
        droppedCount++;
        continue;
      }
      try {
        const migrated = migrator(orig);
        if (migrated && migrated.id && migrated.type) {
          toDrop.add(r.mutationId);   // remove versao antiga
          toMigrate.push(migrated);   // re-enfileira versao nova
          migratedCount++;
        } else {
          toDrop.add(r.mutationId);
          droppedCount++;
        }
      } catch (e) {
        try { console.warn('[migrate] migrator v' + target + ' falhou:', e && e.message); } catch (_) {}
        toDrop.add(r.mutationId);
        droppedCount++;
      }
    }

    if (toDrop.size > 0 || toMigrate.length > 0) {
      const remaining = queue.filter(m => !toDrop.has(m.id)).concat(toMigrate);
      saveQueue(remaining);
      try { console.log('[migrate] migrated=' + migratedCount + ' dropped=' + droppedCount); } catch (_) {}
    }
  }

  // ---- Flush: tenta enviar tudo ao backend ----
  // OFFLINE-FIRST: se servidor indisponivel, fila persiste no localStorage
  // e proximo flush (30s ou quando rede voltar) tenta de novo. App continua
  // funcional sem rede em qualquer cenario.
  // F10-C-1: promise-based mutex substitui o `flushing` boolean.
  // Race condition antiga: Android suspende WebView entre `flushing = true`
  // e o POST; outro tick (visibilitychange/setInterval) entra, le `flushing`
  // ja como false porque `finally` rodou apos resume — 2 POSTs simultaneos
  // com a mesma fila. Resultado: server processa 2x (idempotente por upsert
  // mas gasta CPU+log, e em conflict-flow podia rejeitar a 2a wave inteira).
  //
  // Promise-based: chamadas concorrentes recebem a MESMA promise pendente.
  // Re-entry vira merge automatico; ninguem perde resultado. Callers que
  // ignoram retorno (fire-and-forget) seguem funcionando — promise so e'
  // referenciada se quiserem await.
  let flushPromise = null;
  // Marcador soft de pairing invalidado (revogado/expirado no server).
  // Não deletamos o pairing local — Felipe pode re-parear sem perder fila.
  // Quando true: stop tentando flush ate operador re-parear OU clearPairing.
  let _pairingInvalid = false;
  // Backoff exponencial em erro transitorio (network/5xx/timeout). Reseta em
  // flush bem sucedido OU quando rede volta (handleOnline). Sem backoff, um
  // server caido por 1h causaria ~120 reqs/h por device — com backoff cai
  // pra ~6 reqs/h apos 5 falhas consecutivas.
  let _consecutiveFlushFailures = 0;
  const FLUSH_DELAY_NORMAL_MS = 30000;   // 30s — delay padrao
  const FLUSH_DELAY_CAP_MS = 600000;     // 10min — cap maximo do backoff
  async function flush() {
    // F10-C-1: re-entry retorna a MESMA promise pendente. Sem race.
    if (flushPromise) return flushPromise;
    flushPromise = _flushInner().finally(() => { flushPromise = null; });
    return flushPromise;
  }

  async function _flushInner() {
    if (_pairingInvalid) return 'auth'; // backoff até re-parear
    const queue = loadQueue();
    if (queue.length === 0) {
      if (window.cdbApp) window.cdbApp.markAllSynced();
      return 'empty';
    }
    if (!navigator.onLine) return 'offline';

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

      // B3: captura header Date pra calibrar serverTimeOffset (todas as
      // respostas, mesmo erros — server seguramente carimba Date).
      updateServerTimeFromResponse(resp);

      if (resp.status === 401) {
        // Pairing revogado/expirado. Marca em memoria, para de tentar.
        // Operador vai precisar re-parear pelo Diagnostico.
        _pairingInvalid = true;
        console.warn('Sync rejeitou auth — pairing invalido, re-parear pelo Diagnostico');
        if (window.cdbApp && window.cdbApp.onPairingInvalid) {
          try { window.cdbApp.onPairingInvalid(); } catch (e) {}
        }
        return 'auth';
      }
      if (!resp.ok) {
        console.warn('Sync falhou, status', resp.status);
        _consecutiveFlushFailures = Math.min(_consecutiveFlushFailures + 1, 5);
        if (_consecutiveFlushFailures >= 5) {
          openTicketAuto(
            'Sincronização falhando consecutivamente',
            'Device ' + deviceId + ' acumulou 5+ falhas de flush. Ultimo HTTP status: ' + resp.status,
            'Incidente', 'flush-fail'
          );
        }
        return 'retry';
      }

      const result = await resp.json().catch(() => ({}));
      let acceptedCount = 0;
      if (result.acceptedIds && Array.isArray(result.acceptedIds)) {
        const accepted = new Set(result.acceptedIds);
        acceptedCount = accepted.size;
        // Recarrega a fila do localStorage em vez de usar o snapshot inicial:
        // mutações enfileiradas DURANTE a fetch não são perdidas.
        const currentQueue = loadQueue();
        const remaining = currentQueue.filter(m => !accepted.has(m.id));
        saveQueue(remaining);
      }
      // Se acceptedIds ausente, mantém a fila intacta (melhor reenviar do que perder).
      // F4 — feedback visual: avisa o app pra mostrar toast com qtd sincronizada.
      if (acceptedCount > 0) {
        try {
          window.dispatchEvent(new CustomEvent('cdb-sync-success', { detail: { count: acceptedCount } }));
        } catch (_) {}
      }

      // Onda 5: trata conflicts. Server retorna rejected[] com reason
      // começando com "conflict:" quando outro device sincronizou primeiro.
      // PWA mostra toast + força pull pra refletir versão mais nova.
      if (result.rejected && Array.isArray(result.rejected) && result.rejected.length > 0) {
        const conflicts = result.rejected.filter(r => r.reason && r.reason.startsWith('conflict:'));
        if (conflicts.length > 0) {
          // Remove os conflitados da fila (não vão ser aceitos mesmo).
          // Server retorna { mutationId, reason } — usar mutationId, não id.
          const conflictIds = new Set(conflicts.map(c => c.mutationId));
          const remaining = loadQueue().filter(m => !conflictIds.has(m.id));
          saveQueue(remaining);
          // C3: server pode incluir winningPayload com a versao server vencedora;
          // app usa pra mostrar diff visual (versao local vs server) ao operador.
          if (window.cdbApp && typeof window.cdbApp.onSyncConflict === 'function') {
            try { window.cdbApp.onSyncConflict(conflicts); } catch (e) {}
          }
          // Force pull pra alinhar com servidor
          setTimeout(pull, 200);
        }

        // B4: server pode rejeitar mutations em schema antigo com reason='migrate:N'.
        // Tentamos transformar via mutationMigrators[N] e re-enfileirar pra proxima
        // tentativa. Se nao houver migrator registrado, descarta com warn pra log
        // (alternativa seria deixar na fila pra acumular eternamente — pior).
        const toMigrate = result.rejected.filter(r => r.reason && r.reason.startsWith('migrate:'));
        if (toMigrate.length > 0) {
          applyMutationMigrationsAndRequeue(toMigrate);
        }
      }
      localStorage.setItem(LAST_FULL_SYNC_KEY, String(Date.now()));
      updatePendingCount();
      // Sucesso — reseta backoff pra delay normal no proximo tick.
      _consecutiveFlushFailures = 0;
      // F10-C-7: dispara upload de fotos em background apos flush bem-sucedido.
      try {
        if (window.cdbPhotoUpload && typeof window.cdbPhotoUpload.flush === 'function') {
          window.cdbPhotoUpload.flush(); // fire-and-forget
        }
      } catch (_) {}
      return 'ok';
    } catch (e) {
      console.warn('Erro no sync:', e.message);
      _consecutiveFlushFailures = Math.min(_consecutiveFlushFailures + 1, 5);
      if (_consecutiveFlushFailures >= 5) {
        openTicketAuto(
          'Sincronização falhando consecutivamente',
          'Device ' + deviceId + ' acumulou 5+ falhas de flush. Exception: ' + (e && e.message || e),
          'Incidente', 'flush-fail'
        );
      }
      return 'retry';
    }
    // F10-C-1: o reset do flushPromise mora no `.finally(...)` do wrapper flush().
  }

  // ---- Pull: traz mudanças do servidor (outros devices) ----
  // OFFLINE-FIRST: silencioso quando offline ou pairing invalido.
  // F7: contador de falhas consecutivas — abre ticket auto apos 5.
  let _consecutivePullFailures = 0;
  async function pull() {
    if (!navigator.onLine) return;
    if (_pairingInvalid) return;
    try {
      const since = localStorage.getItem(LAST_FULL_SYNC_KEY) || '0';
      const url = API_BASE_URL + API_PREFIX + '/sync/pull?' + new URLSearchParams({ since, deviceId });
      const ctrl = new AbortController();
      const timeoutId = setTimeout(() => ctrl.abort(), 15000);
      let resp;
      try {
        resp = await fetch(url, { headers: baseHeaders(), signal: ctrl.signal });
      } finally {
        clearTimeout(timeoutId);
      }
      updateServerTimeFromResponse(resp);
      if (resp.status === 401) { _pairingInvalid = true; return; }
      if (!resp.ok) {
        _consecutivePullFailures++;
        if (_consecutivePullFailures >= 5) {
          openTicketAuto(
            'Pull do servidor falhando',
            'Device ' + deviceId + ' acumulou ' + _consecutivePullFailures + ' falhas consecutivas de pull. Ultimo HTTP status: ' + resp.status,
            'Incidente', 'pull-fail'
          );
        }
        return;
      }
      const data = await resp.json();
      if (data && data.mutations && Array.isArray(data.mutations)) {
        applyServerMutations(data.mutations);
      }
      _consecutivePullFailures = 0;
    } catch (e) {
      _consecutivePullFailures++;
      try {
        if (typeof window !== 'undefined' && typeof window.logError === 'function') {
          window.logError(e, 'sync.pull');
        }
      } catch (_) {}
      console.warn('Pull falhou:', e && e.message);
      if (_consecutivePullFailures >= 5) {
        openTicketAuto(
          'Pull do servidor falhando',
          'Device ' + deviceId + ' acumulou ' + _consecutivePullFailures + ' falhas consecutivas. Exception: ' + (e && e.message || e),
          'Incidente', 'pull-fail'
        );
      }
    }
  }

  // F7-C: cashClosings vem como state.cashClosings (acessivel via cdbApp).
  // Mapeamento de tipo de mutation → colecao no state + chave localStorage.
  const _SERVER_MUTATION_MAP = {
    product:   { stateKey: 'products',     storage: 'cdb-products' },
    client:    { stateKey: 'clients',      storage: 'cdb-clients'  },
    order:     { stateKey: 'orders',       storage: 'cdb-orders'   },
    cashEntry: { stateKey: 'cashEntries',  storage: 'cdb-cash'     },
    batch:     { stateKey: 'batches',      storage: 'cdb-batches'  },
    // F7-C: fechamento de caixa snapshot. Mobile mantem em cashClosings[].
    closing:   { stateKey: 'cashClosings', storage: 'cdb-cash-closings' }
  };
  let _consecutiveApplyFailures = 0;

  function applyServerMutations(mutations) {
    if (!mutations.length || !window.cdbApp) return;
    const state = window.cdbApp.getState();
    let changed = false;
    let errorsThisBatch = 0;
    mutations.forEach(m => {
      try {
        const [type, op] = m.type.split('.');
        const map = _SERVER_MUTATION_MAP[type];
        if (!map) return; // tipo desconhecido — ignora silenciosamente
        const coll = state[map.stateKey];
        if (!coll) return;
        const idx = coll.findIndex(x => x.id === m.payload.id);

        // F7-B: cashEntry com estornado=true → mobile esconde do operador
        // (remove do array). Preserva no localStorage NUNCA — quando vier
        // pull pos-restauracao, server reenvia. Soft-delete visual.
        if (type === 'cashEntry' && m.payload && m.payload.estornado === true) {
          if (idx >= 0) { coll.splice(idx, 1); changed = true; }
          try { localStorage.setItem(map.storage, JSON.stringify(coll)); } catch (_) {}
          return;
        }

        if (op === 'upsert') {
          if (idx >= 0) coll[idx] = m.payload;
          else coll.push(m.payload);
          changed = true;
        } else if (op === 'delete') {
          if (idx >= 0) { coll.splice(idx, 1); changed = true; }
        }
        try { localStorage.setItem(map.storage, JSON.stringify(coll)); } catch (_) {}
      } catch (e) {
        errorsThisBatch++;
        try {
          if (typeof window.logError === 'function') {
            window.logError(e, 'applyServerMutations:' + (m && m.type));
          }
        } catch (_) {}
      }
    });

    // F7: tickets automaticos se applyServerMutations falhar muito.
    if (errorsThisBatch > 0) {
      _consecutiveApplyFailures++;
      if (_consecutiveApplyFailures >= 5) {
        try {
          openTicketAuto(
            'Sincronização do servidor falhando',
            'Device ' + deviceId + ' falhou ' + _consecutiveApplyFailures
              + ' batches de applyServerMutations consecutivas. '
              + 'Erros no batch atual: ' + errorsThisBatch + '/' + mutations.length,
            'Incidente', 'apply-server-fail');
        } catch (_) {}
      }
    } else if (mutations.length > 0) {
      _consecutiveApplyFailures = 0;
    }

    if (changed) {
      console.log('Mudanças do servidor aplicadas, recarregando...');
      (async () => {
        try { await flush(); } catch (e) { console.warn('flush before reload falhou', e); }
        setTimeout(() => location.reload(), 100);
      })();
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
      // A6: snapshot stripado pra bater com o diff em computeMutations.
      batches: state.batches.map(stripBatchPhotoBytes)
    }));
    if (muts.length) {
      enqueue(muts);
      // Tenta sincronizar na hora (se online)
      flush();
    }
  };

  // ---- Backoff exponencial ----
  // Calcula o proximo delay baseado no status do ultimo flush:
  //   ok / empty     → 30s (normal)
  //   retry          → 30s * 2^(failures-1), cap 10min (60/120/240/480/600)
  //   auth           → 10min (so re-pareamento destrava; loop checa de vez em quando)
  //   offline / busy → 30s (sem mudar contador — proximo tick vai tentar)
  function computeFlushDelay(status) {
    if (status === 'auth') return FLUSH_DELAY_CAP_MS;
    if (status === 'retry') {
      const exp = Math.pow(2, Math.max(0, _consecutiveFlushFailures - 1));
      const base = Math.min(FLUSH_DELAY_NORMAL_MS * exp, FLUSH_DELAY_CAP_MS);
      // B1: jitter +/-50% pra evitar tempestade quando frota inteira sai do
      // backoff junto (ex: servidor caiu por 10min e volta — N devices em
      // FLUSH_DELAY_CAP_MS sincronizado disparariam todos no mesmo tick).
      // Math.random() em [0, 1) → fator em [0.5, 1.5). Garante delay > 0.
      return Math.max(1000, Math.floor(base * (0.5 + Math.random())));
    }
    return FLUSH_DELAY_NORMAL_MS;
  }

  // ---- Eventos de conectividade ----
  function handleOnline() {
    console.log('Voltei online, sincronizando...');
    // Reseta backoff: nao faz sentido manter delay longo se a rede acabou
    // de voltar. Tambem reagenda o ciclo imediato pra nao esperar o tick
    // longo que ja estava agendado (ex: cap 10min apos varias falhas).
    _consecutiveFlushFailures = 0;
    scheduleNextFlush(0);
    pull().then(fetchAndProcessCommands).then(startRealtime).then(maybeAutoBackup);
  }
  function handleOffline() {
    stopRealtime();
    console.log('Offline, tudo vai pra fila.');
  }
  window.addEventListener('online', handleOnline);
  window.addEventListener('offline', handleOffline);

  // B2: visibilitychange → flush. Android pode matar timers em background;
  // ao voltar pro app o operador espera ate 30s pelo proximo tick. Com este
  // listener, qualquer transicao "background -> foreground" aciona flush
  // imediato. document pode nao existir em ambientes esotericos (tests vm)
  // entao testamos antes.
  if (typeof document !== 'undefined' && typeof document.addEventListener === 'function') {
    document.addEventListener('visibilitychange', function () {
      if (!document.hidden && navigator.onLine) {
        // Reseta backoff: voltar ao foreground sinaliza que ha demanda do
        // operador, vale tentar de novo sem esperar o cap acumulado.
        _consecutiveFlushFailures = 0;
        scheduleNextFlush(0);
      }
    });
  }

  // ---- Inicialização ----
  let flushIntervalId = null;
  // Auto-reagenda apos cada tick com delay computado pelo backoff. Cancela
  // qualquer timer pendente pra evitar duplo agendamento se chamado fora de tick.
  function scheduleNextFlush(delayMs) {
    if (flushIntervalId) { clearTimeout(flushIntervalId); flushIntervalId = null; }
    const delay = typeof delayMs === 'number' ? delayMs : FLUSH_DELAY_NORMAL_MS;
    flushIntervalId = setTimeout(async function flushTick() {
      let status = 'ok';
      try {
        status = (await flush()) || 'ok';
        fetchAndProcessCommands();
      } catch (e) {
        try { console.warn('[sync] tick falhou:', e && e.message); } catch (_) {}
        status = 'retry';
      } finally {
        scheduleNextFlush(computeFlushDelay(status));
      }
    }, delay);
  }
  function stop() {
    if (flushIntervalId) { clearTimeout(flushIntervalId); flushIntervalId = null; }
    stopRealtime();
    window.removeEventListener('online', handleOnline);
    window.removeEventListener('offline', handleOffline);
  }
  // Cleanup no unload — em Capacitor o pagehide dispara antes de descarregar
  // a webview; no browser puro tambem cobre fechamento de aba.
  window.addEventListener('pagehide', stop);

  // F10-C-5: limpa locks de OTA expirados (TTL 60s). Se app fechou durante
  // OTA, heartbeat parou e o lock expirou — boot limpa.
  try {
    const lockTs = parseInt(localStorage.getItem(UPDATE_LOCK_KEY) || '0', 10);
    if (lockTs && (Date.now() - lockTs > UPDATE_LOCK_TTL_MS)) {
      localStorage.removeItem(UPDATE_LOCK_KEY);
      localStorage.removeItem('cdb-mutations-blocked');
      _trace('boot', 'OTA lock expirado — limpando');
    }
  } catch (_) {}

  setTimeout(() => {
    if (window.cdbApp) {
      const s = window.cdbApp.getState();
      lastSnapshot = JSON.parse(JSON.stringify({
        products: s.products.map(({count, photo, ...r}) => r),
        clients: s.clients,
        orders: s.orders,
        cashEntries: s.cashEntries,
        // A6: snapshot inicial tambem stripado, senao o primeiro diff
        // gerado por cdbOnPersist comparar inflated vs stripped e enfileirar
        // batch inteira como "novidade".
        batches: s.batches.map(stripBatchPhotoBytes)
      }));
    }
    updatePendingCount();
    // A5: detecta reinstalacao + oferece restore via backup local. Roda em
    // paralelo com o resto do boot — emite evento cdb-restore-prompt que o
    // index.html escuta. Falha silenciosa nao bloqueia o app.
    maybeOfferRestoreOnReinstall();
    // F10-C-2: inicializa queue-store IDB antes do primeiro flush.
    _initQueueStore().then(function () {
      updatePendingCount();
    }).catch(function (e) {
      _trace('boot', 'queue-store init error (continuing with localStorage)', { error: e && e.message });
    });
    // F10-C-7: inicializa photo-store IDB (separado do queue-store).
    if (window.cdbPhotoStore && typeof window.cdbPhotoStore.init === 'function') {
      window.cdbPhotoStore.init().catch(function (e) {
        _trace('boot', 'photo-store init error (fotos nao persistirao)', { error: e && e.message });
      });
    }
    flush().then(pull).then(fetchAndProcessCommands).then(startRealtime).then(maybeAutoBackup);
    // Loop self-rescheduling com backoff exponencial em erro (substitui o
    // antigo setInterval(30000) fixo). Em rede OK fica a 30s; em incidente
    // longo do server escala ate 10min entre tentativas, economizando
    // bateria/rede sem perder mutations (fila persiste).
    scheduleNextFlush(FLUSH_DELAY_NORMAL_MS);
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
        if (k && k.startsWith('cdb-')) {
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

  // ---- Backup local em arquivo (offline-first) ----
  // Escreve snapshot JSON em pasta Documents do dispositivo, NAO depende de
  // servidor nem de rede. Sobrevive ao app crashar ou WebView limpar cache.
  // Em Android com Capacitor Filesystem v6, Directory.Documents usa scoped
  // storage e nao requer permissao runtime. Em Android 11+ o arquivo eh
  // criado em /storage/emulated/0/Documents/casa-da-baba/, visivel ao usuario
  // pelo gerenciador de arquivos e (na maioria dos OEMs) preservado mesmo
  // apos uninstall do app.
  //
  // Retorna { ok: bool, uri?: string, reason?: string }. Falha silenciosa
  // — nunca interrompe trabalho do operador.
  const LOCAL_BACKUP_DIR = 'casa-da-baba';
  const LOCAL_BACKUP_FILE = LOCAL_BACKUP_DIR + '/backup-latest.json';
  const LAST_LOCAL_BACKUP_KEY = 'cdb-last-local-backup-at';

  function _getFilesystemPlugin() {
    try {
      var Cap = window.Capacitor;
      if (!Cap || !Cap.Plugins) return null;
      return Cap.Plugins.Filesystem || null;
    } catch (e) { return null; }
  }

  // Plugin custom (Java) que escreve em MediaStore Documents/Downloads
  // PUBLICOS — sobrevive uninstall do app. Em ultima instancia, oferece
  // SAF picker pro user selecionar arquivo manualmente.
  function _getPublicBackupPlugin() {
    try {
      var Cap = window.Capacitor;
      if (!Cap || !Cap.Plugins) return null;
      return Cap.Plugins.PublicBackup || null;
    } catch (e) { return null; }
  }

  function _buildBackupPayload(note) {
    return {
      schema: 'cdb-backup-v1',
      capturedAt: Date.now(),
      capturedAtIso: new Date().toISOString(),
      deviceId: deviceId,
      bundleVersion: (window.__BUNDLE_INFO__ && window.__BUNDLE_INFO__.version) || null,
      operatorName: (window.cdbApp && window.cdbApp.getOperator)
        ? window.cdbApp.getOperator()
        : null,
      note: note || 'auto',
      data: _collectSnapshot()
    };
  }

  async function localBackupToFile(note) {
    var json = JSON.stringify(_buildBackupPayload(note));

    // Sanity-check de espaco antes de tentar escrever. StorageEstimate
    // reporta quota da origin (IndexedDB/localStorage) — nao reflete
    // exatamente o filesystem do device, mas se quota esta cheia
    // tipicamente indica pouco espaco geral. Aborta cedo em vez de
    // explodir no Filesystem.writeFile silenciosamente.
    try {
      if (navigator.storage && navigator.storage.estimate) {
        var est = await navigator.storage.estimate();
        if (est && typeof est.quota === 'number' && typeof est.usage === 'number') {
          var livre = est.quota - est.usage;
          if (livre < Math.max(json.length * 4, 1024 * 1024)) {
            console.warn('[localBackup] espaco insuficiente (livre=' + livre + 'B). Abortando.');
            return { ok: false, reason: 'insufficient-storage' };
          }
        }
      }
    } catch (_) { /* nao bloqueia se API indisponivel */ }

    var anyOk = false;
    var details = { publicUris: null, scopedUri: null };

    // CAMINHO 1 (PRIORITARIO): MediaStore publico via plugin custom.
    // Sobrevive uninstall — esse e o ponto crucial.
    var PB = _getPublicBackupPlugin();
    if (PB) {
      try {
        var pubRes = await PB.writePublicBackup({
          filename: 'backup-latest.json',
          json: json
        });
        details.publicUris = {
          documents: pubRes && pubRes.documentsUri,
          downloads: pubRes && pubRes.downloadsUri
        };
        anyOk = true;
        console.log('[localBackup] publico OK', details.publicUris);
      } catch (e) {
        console.warn('[localBackup] publico falhou:', e && e.message);
      }
    }

    // CAMINHO 2 (defensa em profundidade): Filesystem scoped do app.
    // NAO sobrevive uninstall, mas protege contra crash do WebView, limpeza
    // de cache, etc. Roda mesmo se o plugin custom estiver fora do APK
    // (compatibilidade com versoes antigas).
    var FS = _getFilesystemPlugin();
    if (FS) {
      try {
        var res = await FS.writeFile({
          path: LOCAL_BACKUP_FILE,
          data: json,
          directory: 'DOCUMENTS',
          encoding: 'utf8',
          recursive: true
        });
        details.scopedUri = res && res.uri;
        anyOk = true;
      } catch (e) {
        console.warn('[localBackup] scoped falhou:', e && e.message);
      }
    }

    if (anyOk) {
      try { localStorage.setItem(LAST_LOCAL_BACKUP_KEY, String(Date.now())); } catch (e) {}
      return { ok: true, details: details };
    }
    return { ok: false, reason: 'no-storage-available' };
  }

  // Le backup local existente. Retorna o JSON parseado ou null.
  // Usado no boot pra detectar reinstalacao + oferecer restore.
  // Tenta MediaStore publico primeiro (sobrevive uninstall), fallback Filesystem.
  async function readLocalBackup() {
    // CAMINHO 1: MediaStore publico — funciona se mesmo install, e em alguns
    // OEMs Android tambem apos reinstall (depende do MediaStore preservar
    // entries do uninstall).
    var PB = _getPublicBackupPlugin();
    if (PB) {
      try {
        var res = await PB.readPublicBackup({ filename: 'backup-latest.json' });
        if (res && res.found && res.content) {
          var parsed = JSON.parse(res.content);
          if (parsed && parsed.schema === 'cdb-backup-v1' && parsed.data) {
            return parsed;
          }
        }
      } catch (e) {
        console.warn('[readLocalBackup] publico falhou:', e && e.message);
      }
    }

    // CAMINHO 2: scoped storage — pode nao existir apos reinstall em Android 11+,
    // mas funciona pra recovery de crash do WebView no mesmo install.
    var FS = _getFilesystemPlugin();
    if (FS) {
      try {
        var res2 = await FS.readFile({
          path: LOCAL_BACKUP_FILE,
          directory: 'DOCUMENTS',
          encoding: 'utf8'
        });
        if (res2 && res2.data) {
          var parsed2 = JSON.parse(res2.data);
          if (parsed2 && parsed2.schema === 'cdb-backup-v1' && parsed2.data) {
            return parsed2;
          }
        }
      } catch (e) {
        // Silencioso — arquivo nao existe.
      }
    }
    return null;
  }

  // ---- A5: deteccao de reinstalacao + oferta de restore ----
  // Em Android, quando o usuario desinstala/reinstala o APK, o localStorage
  // do WebView eh limpo — todos os dados de negocio (pedidos, lotes, caixa)
  // somem. Antes desta entrega o operador descobria isso somente quando
  // notava o app vazio (e ai a fila ja era perdida tambem).
  //
  // Agora: no boot, se nao houver dados de negocio E houver backup local
  // preservado em MediaStore publico (que SOBREVIVE a uninstall em muitos
  // OEMs Android), dispara o evento window 'cdb-restore-prompt' com payload
  // do backup. O index.html escuta esse evento e mostra modal pro operador
  // confirmar o restore. Sem confirmacao explicita, dados nao sao escritos
  // — operador pode optar por comecar zerado se a perda foi intencional.
  function _localStorageHasBusinessData() {
    // Boot pos-reinstall = sem chaves de negocio (ou apenas '[]' vazio).
    // cdb-device-id pode existir pq foi recriado no getDeviceId() acima,
    // entao ignoramos. Mesmo pra cdb-pwa-installed-version e cdb-pairing.
    const businessKeys = [
      'cdb-products', 'cdb-orders', 'cdb-clients',
      'cdb-batches', 'cdb-cash', 'cdb-cash-closings'
    ];
    for (let i = 0; i < businessKeys.length; i++) {
      const v = localStorage.getItem(businessKeys[i]);
      if (v && v !== '[]' && v !== 'null' && v !== '{}') return true;
    }
    return false;
  }

  async function maybeOfferRestoreOnReinstall() {
    try {
      if (_localStorageHasBusinessData()) return; // boot normal
      const backup = await readLocalBackup();
      if (!backup || !backup.data) return;
      try { console.log('[restore] backup local detectado pos-reinstall — emitindo cdb-restore-prompt'); } catch (_) {}
      try {
        window.dispatchEvent(new CustomEvent('cdb-restore-prompt', {
          detail: {
            backup: backup,
            capturedAt: backup.capturedAt || null,
            capturedAtIso: backup.capturedAtIso || null,
            deviceId: backup.deviceId || null,
            source: 'local-mediastore'
          }
        }));
      } catch (_) {}
    } catch (e) {
      try { console.warn('[restore] maybeOfferRestoreOnReinstall falhou:', e && e.message); } catch (_) {}
    }
  }

  // Aplica o snapshot do backup escrevendo as chaves cdb-* no localStorage.
  // index.html chama isto apos o operador confirmar no modal de restore.
  // Forca reload em seguida pra que o app re-hidrate de cdb-* e reflita
  // os dados restaurados. Retorna { restored: number, skipped: number }.
  function applyBackupSnapshot(backup) {
    if (!backup || !backup.data || typeof backup.data !== 'object') {
      throw new Error('backup invalido');
    }
    let restored = 0, skipped = 0;
    const data = backup.data;
    Object.keys(data).forEach(k => {
      if (k.indexOf('cdb-') !== 0) { skipped++; return; }
      // Nunca sobrescrever pairing nem device-id no restore — se este device
      // foi re-pareado depois do backup, o pairing atual eh o correto.
      if (k === 'cdb-pairing' || k === 'cdb-device-id') { skipped++; return; }
      try {
        localStorage.setItem(k, String(data[k]));
        restored++;
      } catch (e) { skipped++; }
    });
    return { restored, skipped };
  }

  // ULTIMA LINHA DE DEFESA: SAF picker. Em Android 11+ apos reinstall, o
  // app perde ownership dos arquivos publicos que ele criou. SAF deixa o
  // user selecionar manualmente o arquivo (`backup-latest.json` ou backup
  // exportado mais antigo) e o sistema concede acesso temporario.
  async function pickBackupViaSAF() {
    var PB = _getPublicBackupPlugin();
    if (!PB) return null;
    try {
      var res = await PB.pickBackupFile();
      if (!res || res.cancelled || !res.content) return null;
      var parsed = JSON.parse(res.content);
      // Aceita 2 formatos: cdb-backup-v1 (auto) e o do exportBackup manual
      // (que tem .state e .localStorage). Devolve normalizado pra
      // applySnapshot saber o que fazer.
      if (parsed && parsed.schema === 'cdb-backup-v1' && parsed.data) {
        return { kind: 'snapshot', filename: res.filename, payload: parsed };
      }
      if (parsed && parsed.appId === 'casa-da-baba' && parsed.state) {
        return { kind: 'export', filename: res.filename, payload: parsed };
      }
      // Fallback: aceita objeto plano de chaves cdb-* (snapshot direto)
      if (parsed && typeof parsed === 'object') {
        var keys = Object.keys(parsed);
        if (keys.some(function (k) { return k.startsWith('cdb-'); })) {
          return {
            kind: 'snapshot',
            filename: res.filename,
            payload: { schema: 'cdb-backup-v1', data: parsed, capturedAt: Date.now() }
          };
        }
      }
      return null;
    } catch (e) {
      console.warn('[pickBackupViaSAF]', e);
      return null;
    }
  }

  // Throttled: chamado apos mutacoes (criar pedido, fechar caixa, etc).
  // Min 60s entre escritas pra nao martelar IO em rajadas de operacao.
  var _localBackupTimer = null;
  var LOCAL_BACKUP_MIN_INTERVAL = 60 * 1000;
  function scheduleLocalBackup(reason) {
    if (_localBackupTimer) return;
    _localBackupTimer = setTimeout(function () {
      _localBackupTimer = null;
      var last = 0;
      try { last = parseInt(localStorage.getItem(LAST_LOCAL_BACKUP_KEY) || '0', 10) || 0; } catch (e) {}
      if (Date.now() - last < LOCAL_BACKUP_MIN_INTERVAL) return;
      localBackupToFile(reason || 'mutation').catch(function () {});
    }, 5000);
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

    // Pre-drain: se fila tem mutations pendentes, drenar ANTES do switch.
    // Sem isto, mutations da loja antiga sobem APOS o POST switch-loja e
    // sao atribuidas (pelo servidor) ao pairing atual = loja nova. Drama
    // silencioso de cross-loja: vendas/lotes da loja A aparecem na loja B.
    // Drenagem tem cap de 40s (5x8s); se nao drenar, aborta com erro claro
    // pra o operador decidir (conectar rede melhor / esperar / cancelar).
    const pendingBefore = loadQueue().length;
    if (pendingBefore > 0) {
      const drainResult = await drainQueueWithBackoff(5, 8000);
      if (!drainResult.drained) {
        throw new Error(
          'Fila tem ' + drainResult.remaining + ' mutacao(oes) pendente(s) da loja atual. ' +
          'Conecte a uma rede estavel e tente sincronizar antes de trocar de loja.'
        );
      }
    }

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
  // Reconnect com backoff exponencial em caso de drop. O reconnect nativo
  // do EventSource é ~1s — em servidor down isso vira 3600 reconnects/h por
  // device. Aqui fechamos no onerror e religamos com 2s → 30s, e ainda
  // abortamos depois de 8 falhas consecutivas (polling 30s segue como
  // fallback até o usuário voltar pro app).
  //
  // Auth: pedimos um token JWT efemero (5min) via POST /operation/sse-token
  // com header X-Mobile-Api-Key, e usamos esse token na URL do EventSource.
  // Antes a apiKey ia direto na URL — ficava em log de proxy, historico de
  // browser, telemetria. Servidor velho sem o endpoint cai no fallback de
  // ?apiKey= legado.
  let _sseSource = null;
  let _sseRetryCount = 0;
  let _sseRetryTimer = null;
  const SSE_MAX_RETRIES = 8;

  async function fetchSseToken() {
    const apiKey = getApiKey();
    if (!apiKey) return null;
    try {
      const ctrl = new AbortController();
      const timeoutId = setTimeout(() => ctrl.abort(), 8000);
      let resp;
      try {
        resp = await fetch(API_BASE_URL + API_PREFIX + '/operation/sse-token', {
          method: 'POST',
          headers: baseHeaders({ 'Content-Type': 'application/json' }),
          body: '{}',
          signal: ctrl.signal,
        });
      } finally { clearTimeout(timeoutId); }
      if (!resp.ok) return null;
      const data = await resp.json();
      return (data && typeof data.token === 'string' && data.token) ? data.token : null;
    } catch (_) {
      return null;
    }
  }

  async function startRealtime() {
    if (_sseSource) return; // já conectado
    if (!navigator.onLine) return;
    if (typeof EventSource === 'undefined') return; // browser sem suporte
    const apiKey = getApiKey();
    if (!apiKey) return; // só pareados

    // Tenta token efemero. Se servidor antigo (404) ou rede falha, cai no
    // fallback ?apiKey= pra nao perder realtime durante a transicao.
    const token = await fetchSseToken();
    const queryParam = token ? ('token=' + encodeURIComponent(token))
                              : ('apiKey=' + encodeURIComponent(apiKey));

    try {
      const url = API_BASE_URL + API_PREFIX + '/operation/stream?' + queryParam;
      _sseSource = new EventSource(url);
      _sseSource.onopen = function () { _sseRetryCount = 0; };
      _sseSource.onmessage = function (ev) {
        try {
          const data = JSON.parse(ev.data);
          if (data && data.type === 'mutations-applied') {
            // Outro device sincronizou — puxa atualizações.
            console.log('[realtime] mutations-applied de', data.originDeviceId);
            pull();
            // KDS e outras views standalone escutam esse evento pra refazer
            // fetch independente sem depender do estado local da PWA.
            try {
              window.dispatchEvent(new CustomEvent('cdb-mutations-applied', { detail: data }));
            } catch (_) {}
          } else if (data && data.type === 'command-queued') {
            console.log('[realtime] command-queued', data.commandType);
            fetchAndProcessCommands();
          } else if (data && data.type === 'order.ready') {
            // C4: pedido ficou pronto em outro device — alerta garcom aqui.
            // Custom event pra index.html mostrar UX (toast/beep/badge).
            try {
              window.dispatchEvent(new CustomEvent('cdb-order-ready', { detail: data }));
            } catch (_) {}
            // Notification API: dispara so quando aba/app esta em background
            // (operador no foreground ja viu via UX in-app). Permission
            // tem que ter sido concedida previamente — sem prompt aqui.
            try {
              if (typeof Notification !== 'undefined'
                  && Notification.permission === 'granted'
                  && typeof document !== 'undefined' && document.hidden) {
                const body = (data.clientName ? data.clientName + ' — ' : '')
                  + (data.itemCount || 0) + ' item(s) — R$ ' + (data.total || 0);
                // tag: dedup notificacoes do mesmo pedido (substitui em vez de empilhar)
                new Notification('Pedido pronto', {
                  body: body,
                  tag: 'cdb-order-ready-' + data.orderId,
                  renotify: false
                });
              }
            } catch (_) {}
          }
        } catch (e) {}
      };
      _sseSource.onerror = function () {
        try { _sseSource.close(); } catch (_) {}
        _sseSource = null;
        if (_sseRetryCount >= SSE_MAX_RETRIES) {
          try { console.warn('[realtime] SSE desligado apos', SSE_MAX_RETRIES, 'tentativas — polling segue'); } catch (_) {}
          return;
        }
        const delay = Math.min(2000 * Math.pow(2, _sseRetryCount), 30000);
        _sseRetryCount++;
        if (_sseRetryTimer) clearTimeout(_sseRetryTimer);
        _sseRetryTimer = setTimeout(function () {
          _sseRetryTimer = null;
          startRealtime();
        }, delay);
      };
    } catch (e) {
      try { console.warn('[realtime] startRealtime falhou:', e && e.message); } catch (_) {}
      _sseSource = null;
    }
  }
  function stopRealtime() {
    if (_sseRetryTimer) { clearTimeout(_sseRetryTimer); _sseRetryTimer = null; }
    _sseRetryCount = 0;
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
      } else if (type === 'pwa_update') {
        // Onda 9 — gestor força atualizacao do bundle pelo painel web.
        // Drena fila antes; trigger com {force:true} limpa caches e reload.
        flush().finally(() => triggerPwaUpdate({ reason: 'remote-command', force: true }));
      } else if (type === 'clear_cache') {
        // Debug remoto: limpa caches do SW sem reload (operador continua trabalhando).
        try {
          if (typeof caches !== 'undefined' && caches.keys) {
            caches.keys().then(ks => Promise.all(ks.map(k => caches.delete(k)))).catch(() => {});
          }
          const ctrl = navigator.serviceWorker && navigator.serviceWorker.controller;
          if (ctrl) ctrl.postMessage({ type: 'CLEAR_CACHE' });
        } catch (e) {}
      }
    } catch (e) {
      console.warn('Comando remoto falhou:', type, e && e.message);
    }
  }

  // F5: heartbeat enviado junto com pingVersion. Mantem visivel no dashboard de
  // logs qual fracao da frota esta em qual CACHE_VERSION/schema, e o tamanho da
  // fila local. Nao bloqueia o boot; falha silenciosa.
  function sendHeartbeat(versionPayload) {
    if (!navigator.onLine) return;
    try {
      const installed = (function () {
        try { return localStorage.getItem('cdb-pwa-installed-version') || null; } catch (_) { return null; }
      })();
      const lastSync = (function () {
        try { return parseInt(localStorage.getItem(LAST_FULL_SYNC_KEY) || '0', 10) || 0; } catch (_) { return 0; }
      })();
      const body = {
        deviceId,
        cacheVersion: installed
          || (versionPayload && versionPayload.Ota && versionPayload.Ota.PwaCacheVersion)
          || null,
        schemaVersion: (versionPayload && versionPayload.MobileSchemaVersion) || 0,
        queueSize: loadQueue().length,
        lastSyncAt: lastSync,
        online: !!navigator.onLine,
        // D2: alerta Diagnostico quando WebView nao tem suporte a Service
        // Worker (cache offline ausente). Sync.js segue funcional.
        degraded: !_swSupported
      };
      fetch(API_BASE_URL + API_PREFIX + '/diagnostics/heartbeat', {
        method: 'POST',
        headers: baseHeaders({ 'Content-Type': 'application/json' }),
        body: JSON.stringify(body),
        keepalive: true
      }).catch(() => {});
    } catch (_) {}
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
      // B3: calibra serverTimeOffset.
      updateServerTimeFromResponse(resp);
      if (!resp.ok) return null;
      const data = await resp.json();
      try {
        localStorage.setItem('cdb-server-version', JSON.stringify({
          fetchedAt: Date.now(),
          ...data
        }));
      } catch (e) {}
      // Onda 9 — auto-update OTA orquestrado pelo servidor.
      // Se PwaCacheVersion mudou desde a ultima instalacao confirmada, dispara
      // update do SW + reload assim que o novo SW assumir controle. Falha
      // silenciosa: app continua funcionando offline com o bundle atual.
      try { await maybeApplyPwaUpdate(data); } catch (e) {}
      // F5: heartbeat best-effort — alimenta dashboard de versao lag por device.
      try { sendHeartbeat(data); } catch (e) {}
      return data;
    } catch (e) {
      // Sem rede / timeout / DNS / TLS — silencioso. PWA continua offline.
      try { console.warn('pingVersion falhou:', e && e.message); } catch (_) {}
      return null;
    }
  }

  // ---- OTA: atualizacao do bundle PWA controlada pelo servidor (Onda 9) ----
  // O backend reporta no /version qual o CACHE_VERSION servido pelo sw.js. O PWA
  // guarda em cdb-pwa-installed-version o ultimo valor que ja viu. Quando o
  // servidor anuncia versao diferente, pedimos pro browser refazer fetch do
  // sw.js (registration.update()): se o conteudo mudou, o navegador instala o
  // novo SW. O install_listener original ja chama skipWaiting() — entao o
  // controllerchange dispara em seguida e o listener no index.html recarrega.
  // Caso o SW novo trave em "waiting" por algum motivo, mandamos SKIP_WAITING
  // explicito como ultimo recurso.
  const PWA_INSTALLED_VERSION_KEY = 'cdb-pwa-installed-version';
  let _pwaUpdating = false;

  // ---- F4: Drenagem segura pré-update + lock persistente + schema gate ----
  //
  // Antes de aplicar SKIP_WAITING + reload garantimos:
  //   1. fila cdb-sync-queue drenada (nao perde venda em curso)
  //   2. backup remoto pre-update feito (defesa contra corrupcao de storage)
  //   3. nenhuma operacao critica em andamento (caixa aberto / venda em curso)
  //
  // cdb-update-in-progress mora no localStorage — sobrevive a reload e impede
  // que um segundo ciclo dispare se o reload do controllerchange demorar.
  const UPDATE_LOCK_KEY = 'cdb-update-in-progress';
  // F10-C-5: TTL reduzido pra 60s + heartbeat 5s. Se app fechar durante OTA,
  // TTL expira rapido e boot seguinte limpa o lock.
  const UPDATE_LOCK_TTL_MS = 60 * 1000;
  const UPDATE_HEARTBEAT_MS = 5000;
  let _updateHeartbeatTimer = null;

  function startUpdateHeartbeat() {
    stopUpdateHeartbeat();
    _updateHeartbeatTimer = setInterval(function () {
      try { localStorage.setItem(UPDATE_LOCK_KEY, String(Date.now())); } catch (_) {}
    }, UPDATE_HEARTBEAT_MS);
  }

  function stopUpdateHeartbeat() {
    if (_updateHeartbeatTimer) {
      clearInterval(_updateHeartbeatTimer);
      _updateHeartbeatTimer = null;
    }
  }

  // F10-C-5: flag que bloqueia enqueues nao-criticas durante OTA.
  let _mutationsBlocked = false;
  const CRITICAL_MUTATION_TYPES = {
    'order.upsert': true, 'order.update': true,
    'cashEntry.upsert': true, 'cashEntry.update': true,
    'batch.upsert': true, 'batch.update': true,
    'cashClosing.upsert': true
  };
  // Schema minimo do servidor que esta versao do PWA requer. Bate com
  // MobileVersionController.mobileSchemaVersion. Bump quando o PWA passar a
  // depender de mutations/comandos novos.
  const PWA_REQUIRED_API_VERSION = 2;

  function acquireUpdateLock() {
    try {
      const raw = localStorage.getItem(UPDATE_LOCK_KEY);
      if (raw) {
        const ts = parseInt(raw, 10) || 0;
        if (Date.now() - ts < UPDATE_LOCK_TTL_MS) return false;
      }
      localStorage.setItem(UPDATE_LOCK_KEY, String(Date.now()));
      return true;
    } catch (e) { return true; }
  }

  function releaseUpdateLock() {
    try { localStorage.removeItem(UPDATE_LOCK_KEY); } catch (e) {}
  }

  function isCriticalOperationActive() {
    try {
      if (localStorage.getItem('cdb-caixa-aberto') === '1') return true;
      if (localStorage.getItem('cdb-venda-em-curso') === '1') return true;
    } catch (e) {}
    return false;
  }

  // Loop ate fila vazia ou tentativas esgotadas. flush() ja tem seu proprio
  // guard (flushing flag); aqui so esperamos entre tentativas e re-checamos
  // o tamanho da fila persistida.
  //
  // Retorna { drained: bool, remaining: number }. Defaults 5x8000ms — antes
  // eram 3x5000ms, insuficiente em rede ruim com fila >100 mutations
  // (cenario observado em frota voltando online junto: tempestade de POSTs
  // no servidor faz flush demorar). 5x8s = 40s no pior caso, ainda cabe
  // dentro da janela aceitavel pra OTA e switchLoja.
  async function drainQueueWithBackoff(maxAttempts, perAttemptMs) {
    maxAttempts = maxAttempts || 5;
    perAttemptMs = perAttemptMs || 8000;
    for (let i = 0; i < maxAttempts; i++) {
      if (loadQueue().length === 0) return { drained: true, remaining: 0 };
      try { await flush(); } catch (e) { /* flush eh defensivo */ }
      await new Promise(r => setTimeout(r, perAttemptMs));
    }
    const remaining = loadQueue().length;
    return { drained: remaining === 0, remaining };
  }

  // Telemetria best-effort do ciclo de update. Em F5 vira POST formal pra
  // /api/mobile/diagnostics/update — por ora envia como erro generico pro
  // endpoint ja existente, com payload estruturado, sem bloquear o update.
  async function logUpdateApplied(info) {
    try {
      if (!getApiKey() || !navigator.onLine) return;
      const body = {
        deviceId,
        category: 'pwa.update.applied',
        oldVersion: getInstalledPwaVersion() || null,
        newVersion: (function () {
          try {
            const sv = JSON.parse(localStorage.getItem('cdb-server-version') || 'null');
            return sv && sv.Ota && sv.Ota.PwaCacheVersion ? sv.Ota.PwaCacheVersion : null;
          } catch (_) { return null; }
        })(),
        queueSize: loadQueue().length,
        drainedOk: !!(info && info.drainedOk),
        backupOk: !!(info && info.backupOk),
        force: !!(info && info.force),
        at: Date.now()
      };
      // Endpoint existente aceita payload livre; em F5 viramos pra rota dedicada.
      fetch(API_BASE_URL + API_PREFIX + '/diagnostics/errors', {
        method: 'POST',
        headers: baseHeaders({ 'Content-Type': 'application/json' }),
        body: JSON.stringify({ message: 'pwa.update', context: body }),
        keepalive: true
      }).catch(() => {});
    } catch (e) {}
  }

  function getInstalledPwaVersion() {
    try { return localStorage.getItem(PWA_INSTALLED_VERSION_KEY) || null; } catch (e) { return null; }
  }

  function setInstalledPwaVersion(v) {
    try { if (v) localStorage.setItem(PWA_INSTALLED_VERSION_KEY, String(v)); } catch (e) {}
  }

  async function maybeApplyPwaUpdate(versionPayload) {
    if (_pwaUpdating) return;
    if (!navigator.serviceWorker || !navigator.serviceWorker.getRegistration) return;

    // F4 Gate: se a API estiver em schema menor do que este PWA requer, NAO
    // atualiza ainda. Render publica os 3 services em momentos diferentes;
    // sem este gate, o PWA novo pode aterrissar antes do API novo e chamar
    // endpoints que ainda nao existem.
    const apiSchema = versionPayload && versionPayload.MobileSchemaVersion;
    if (typeof apiSchema === 'number' && apiSchema < PWA_REQUIRED_API_VERSION) {
      try { console.warn('[OTA] api schema', apiSchema, '< PWA requer', PWA_REQUIRED_API_VERSION, '— adiando update'); } catch (_) {}
      return;
    }

    const remote = versionPayload && versionPayload.Ota && versionPayload.Ota.PwaCacheVersion;
    if (!remote || typeof remote !== 'string') return;

    const installed = getInstalledPwaVersion();
    if (installed === null) {
      // Primeira execucao com Onda 9 — apenas registra a versao corrente; nao
      // dispara update (assumimos que o que esta cached eh o que o servidor servia
      // ate agora; bump real no proximo deploy gera divergencia).
      setInstalledPwaVersion(remote);
      return;
    }
    if (installed === remote) return;

    console.log('[OTA] versao do servidor', remote, 'difere da instalada', installed, '— atualizando');
    await triggerPwaUpdate({ reason: 'version-bump', force: false });
  }

  // Forca atualizacao do bundle PWA: dispara registration.update() e, se o novo
  // SW ficar em waiting, manda SKIP_WAITING. controllerchange listener no
  // index.html cuida do reload. Se {force:true} (comando remoto pwa_update),
  // limpa caches antes pra garantir que ate index.html cacheado seja descartado.
  async function triggerPwaUpdate(opts) {
    opts = opts || {};
    if (_pwaUpdating) return;

    // F4: operacao critica em andamento? Adia 5min (a nao ser que force).
    if (!opts.force && isCriticalOperationActive()) {
      try { console.log('[OTA] caixa/venda em andamento — adiando update por 5min'); } catch (_) {}
      setTimeout(() => triggerPwaUpdate(opts), 5 * 60 * 1000);
      return;
    }

    // F4: lock persistente em localStorage — sobrevive a reload e impede
    // re-entry quando o controllerchange demora pra acontecer.
    if (!acquireUpdateLock()) {
      try { console.log('[OTA] update lock ja segurado — ignorando trigger'); } catch (_) {}
      return;
    }

    _pwaUpdating = true;
    // F10-C-5: bloqueia mutations nao-criticas + heartbeat
    _mutationsBlocked = true;
    localStorage.setItem('cdb-mutations-blocked', 'true');
    startUpdateHeartbeat();
    try {
      if (window.cdbApp && typeof window.cdbApp.showToast === 'function') {
        window.cdbApp.showToast('Atualizando — aguarde 10s...', 'info');
      }
    } catch (_) {}

    let drainedOk = false;
    let drainRemaining = 0;
    let backupOk = false;
    try {
      // F4: 1) drena a fila antes de aplicar (anti-perda de venda).
      const drainResult = await drainQueueWithBackoff(5, 8000);
      drainedOk = drainResult.drained;
      drainRemaining = drainResult.remaining;
      if (!drainedOk && !opts.force) {
        try { console.warn('[OTA] fila com', drainRemaining, 'pendencias — postergando update 5min'); } catch (_) {}
        releaseUpdateLock();
        _pwaUpdating = false;
        setTimeout(() => triggerPwaUpdate(opts), 5 * 60 * 1000);
        return;
      }

      // F4: 2) backup remoto pre-update (defesa em profundidade).
      try {
        if (getApiKey() && navigator.onLine) {
          await uploadBackup('pre-update');
          backupOk = true;
        }
      } catch (e) {
        try { console.warn('[OTA] backup pre-update falhou:', e && e.message); } catch (_) {}
      }

      // D1: se device pareado mas backup falhou (e nao e' force), adia.
      // Pareado + online + backup falhando indica problema transitorio
      // (servidor instavel, rede ruim, quota). SE este OTA corromper o
      // localStorage e nao houver snapshot recente, perda potencial. O
      // commando remoto pwa_update com force:true ignora esse gate pra
      // dar ao gestor controle manual em casos extremos. Devices nao
      // pareados nao tentam backup, entao prosseguem normalmente.
      if (getApiKey() && !backupOk && !opts.force) {
        try { console.warn('[OTA] backup pre-update falhou em device pareado — postergando update 5min'); } catch (_) {}
        releaseUpdateLock();
        _pwaUpdating = false;
        setTimeout(() => triggerPwaUpdate(opts), 5 * 60 * 1000);
        return;
      }

      // F4: 3) telemetria do ciclo (best-effort).
      try { logUpdateApplied({ drainedOk, backupOk, force: !!opts.force }); } catch (_) {}

      const reg = await navigator.serviceWorker.getRegistration();
      if (!reg) {
        if (opts.force) location.reload();
        return;
      }

      if (opts.force) {
        // Limpa caches por dois caminhos: API direta da window (suficiente quando
        // o SW responde) e via mensagem ao SW (caminho oficial). Ambos best-effort.
        try {
          if (typeof caches !== 'undefined' && caches.keys) {
            const ks = await caches.keys();
            await Promise.all(ks.map(k => caches.delete(k).catch(() => {})));
          }
        } catch (e) {}
        try {
          const ctrl = navigator.serviceWorker.controller;
          if (ctrl) ctrl.postMessage({ type: 'CLEAR_CACHE' });
        } catch (e) {}
      }

      try { await reg.update(); } catch (e) { console.warn('[OTA] reg.update() falhou', e); }

      // Se ja existe um SW novo aguardando (waiting), pede skipWaiting.
      if (reg.waiting) {
        try { reg.waiting.postMessage({ type: 'SKIP_WAITING' }); } catch (e) {}
      }

      // Se a versao remota foi passada e nao havia waiting, marca a remota como
      // installed agora pra evitar loop de update enquanto o browser instala.
      // Caso o reload do controllerchange nao dispare (sem SW novo), o estado
      // local fica consistente.
      try {
        const sv = JSON.parse(localStorage.getItem('cdb-server-version') || 'null');
        if (sv && sv.Ota && sv.Ota.PwaCacheVersion) {
          setInstalledPwaVersion(sv.Ota.PwaCacheVersion);
        }
      } catch (e) {}

      // Force: garantia final de reload depois de um delay curto.
      if (opts.force) {
        setTimeout(() => { try { location.reload(); } catch (e) {} }, 1500);
      }
    } finally {
      // Libera os locks depois de um pequeno tempo — evita reentry mas nao trava
      // pra sempre se o reload nao acontecer. Tambem libera o lock persistente
      // (cdb-update-in-progress) caso a pagina nao recarregue (ex: registration
      // ja era nula). Em fluxo normal o reload limpa tudo via TTL.
      setTimeout(() => {
        _pwaUpdating = false;
        _mutationsBlocked = false;
        try { localStorage.removeItem('cdb-mutations-blocked'); } catch (_) {}
        stopUpdateHeartbeat();
        releaseUpdateLock();
      }, 5000);
    }
  }

  // ---- Auto-provisionamento via token compartilhado ----
  // APK pre-configurado (Casa da Baba) embute o secret no config.js. No
  // primeiro boot o sync.js troca o secret por apiKey + contexto via
  // POST /api/mobile/devices/pair-auto, sem operador digitar nada.
  // Idempotente no server (mesmo deviceId rotaciona apiKey).
  async function pairAuto(provisioningSecret, label) {
    if (!navigator.onLine) {
      _trace('pair-auto', 'abort: offline', null);
      throw new Error('sem rede');
    }
    const url = API_BASE_URL + API_PREFIX + '/devices/pair-auto';
    _trace('pair-auto', 'fetch ' + url, { secretLen: (provisioningSecret || '').length, deviceId });
    const ctrl = new AbortController();
    const timeoutId = setTimeout(() => ctrl.abort(), 12000);
    const startT = Date.now();
    let resp;
    try {
      resp = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-Device-Id': deviceId },
        body: JSON.stringify({
          provisioningSecret: String(provisioningSecret || '').trim(),
          deviceId,
          label: label || null
        }),
        signal: ctrl.signal
      });
    } catch (e) {
      _trace('pair-auto', 'network FAIL ' + (Date.now() - startT) + 'ms: ' + (e && e.message || e), null);
      throw new Error('servidor inacessivel');
    } finally {
      clearTimeout(timeoutId);
    }
    _trace('pair-auto', 'http ' + resp.status + ' ' + (Date.now() - startT) + 'ms', null);
    if (!resp.ok) {
      let msg = 'auto-provisioning rejeitado';
      try { const e = await resp.json(); if (e && e.error) msg = e.error; } catch (_) {}
      _trace('pair-auto', 'rejeitado: ' + msg, null);
      throw new Error(msg);
    }
    const data = await resp.json();
    _trace('pair-auto', 'OK pareado', { empresaId: data.empresaId, lojaId: data.lojaId });
    savePairing({
      apiKey: data.apiKey,
      empresaId: data.empresaId,
      lojaId: data.lojaId,
      label: data.label,
      defaultOperatorName: data.defaultOperatorName,
      pairedAt: data.pairedAt,
      deviceId: data.deviceId
    });
    _pairingInvalid = false;
    try { await pingVersion(); } catch (_) {}
    // Marca a UI pra mostrar modal de relatorio apos o pull inicial.
    // Sobrevive ao location.reload() que applyServerMutations dispara
    // quando o pull trouxe dados — boot do index.html le a flag e
    // renderiza o modal com contadores atualizados.
    try { localStorage.setItem('cdb-pull-report-pending', '1'); } catch (_) {}
    try { flush(); } catch (_) {}
    try { await pull(); } catch (_) {}
    // Pull rodou sem causar reload (nenhuma mutation) — dispara evento
    // manual pra UI mostrar modal "sem novidades" tambem.
    if (typeof window !== 'undefined') {
      try {
        if (localStorage.getItem('cdb-pull-report-pending') === '1') {
          window.dispatchEvent(new CustomEvent('cdb-initial-pull-done', { detail: { reloaded: false } }));
        }
      } catch (_) {}
    }
    return data;
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
    // Marca a UI pra mostrar modal de relatorio apos o pull inicial.
    try { localStorage.setItem('cdb-pull-report-pending', '1'); } catch (_) {}
    // Tenta drenar fila imediatamente — pode ter mutations acumuladas
    // do periodo offline / sem pairing.
    try { flush(); } catch (_) {}
    // Pull inicial pra trazer produtos/clientes/pedidos. Sem isso a UI
    // fica vazia ate o ciclo de 30s do sync loop disparar o proximo pull.
    try { await pull(); } catch (_) {}
    if (typeof window !== 'undefined') {
      try {
        if (localStorage.getItem('cdb-pull-report-pending') === '1') {
          window.dispatchEvent(new CustomEvent('cdb-initial-pull-done', { detail: { reloaded: false } }));
        }
      } catch (_) {}
    }
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

  // ---- Bootstrap push: enfileira o state ATUAL inteiro como upserts ----
  // Diferente do fluxo normal (cdbOnPersist computa diff vs lastSnapshot),
  // esta funcao re-envia todos os registros locais como if-new. Necessario
  // no primeiro pareamento de um device que ja tem dados acumulados offline
  // — sem isto o servidor so receberia mutations dos proximos saves e o
  // historico ficaria orfao no localStorage.
  //
  // Idempotente no servidor: SyncController faz upsert por (Id, EmpresaId).
  // Mutations ja na fila NAO sao removidas — bootstrap adiciona por cima.
  function pushAll() {
    if (!window.cdbApp || typeof window.cdbApp.getState !== 'function') {
      throw new Error('cdbApp.getState indisponivel');
    }
    const s = window.cdbApp.getState();
    const muts = [];
    const stripProd = (p) => { const { count, photo, ...r } = p; return r; };
    (s.products || []).forEach(p => { if (p && p.id) muts.push({ type: 'product.upsert', payload: stripProd(p) }); });
    (s.clients || []).forEach(c => { if (c && c.id) muts.push({ type: 'client.upsert', payload: c }); });
    (s.orders || []).forEach(o => { if (o && o.id) muts.push({ type: 'order.upsert', payload: o }); });
    // A6: pushAll tambem stripa fotos pesadas antes de enfileirar.
    (s.batches || []).forEach(b => { if (b && b.id) muts.push({ type: 'batch.upsert', payload: stripBatchPhotoBytes(b) }); });
    (s.cashEntries || []).forEach(c => { if (c && c.id) muts.push({ type: 'cashEntry.upsert', payload: c }); });
    if (muts.length === 0) return { enqueued: 0, queueSize: loadQueue().length };
    enqueue(muts);
    return { enqueued: muts.length, queueSize: loadQueue().length };
  }

  // Combina pushAll + flush. Retorna Promise com { enqueued, queueSizeBefore,
  // queueSizeAfter }. Usado pelo botao "Sincronizar tudo agora" do Diagnostico.
  async function pushAllAndFlush() {
    const before = pushAll();
    await flush();
    return { enqueued: before.enqueued, queueSizeBefore: before.queueSize, queueSizeAfter: loadQueue().length };
  }

  // Ping inicial — não bloqueia, dispara em paralelo com primeiro flush.
  setTimeout(pingVersion, 700);

  // ---- Abertura automatica de tickets (erros graves) ----
  // Criterios:
  //   - flush: 5+ falhas consecutivas (server fora, schema mismatch, etc)
  //   - window.onerror: exception nao capturada (handler em index.html chama
  //     window.cdbSync.openTicketAuto)
  // Anti-spam: cooldown 1h por tipo de evento + maximo 5 tickets/dia/device.
  // Precondicao: device JA pareado. Sem pairing nao consegue autenticar a
  // chamada POST /tickets — falha de pareamento fica so no cdb-error-log local.
  const TICKET_COOLDOWN_KEY = 'cdb-ticket-cooldown';
  const TICKET_DAILY_KEY = 'cdb-ticket-daily';
  const TICKET_COOLDOWN_MS = 60 * 60 * 1000;   // 1h por tipo
  const TICKET_MAX_PER_DAY = 5;
  let _flushFailTicketArmed = false;

  function _canOpenTicket(tipo) {
    try {
      const cd = JSON.parse(localStorage.getItem(TICKET_COOLDOWN_KEY) || '{}');
      if (cd[tipo] && Date.now() - cd[tipo] < TICKET_COOLDOWN_MS) return false;
      const daily = JSON.parse(localStorage.getItem(TICKET_DAILY_KEY) || '{}');
      const today = new Date().toISOString().slice(0, 10);
      if ((daily[today] || 0) >= TICKET_MAX_PER_DAY) return false;
      return true;
    } catch (_) { return true; }
  }

  function _markTicketOpened(tipo) {
    try {
      const cd = JSON.parse(localStorage.getItem(TICKET_COOLDOWN_KEY) || '{}');
      cd[tipo] = Date.now();
      localStorage.setItem(TICKET_COOLDOWN_KEY, JSON.stringify(cd));
      const daily = JSON.parse(localStorage.getItem(TICKET_DAILY_KEY) || '{}');
      const today = new Date().toISOString().slice(0, 10);
      daily[today] = (daily[today] || 0) + 1;
      // Limpa datas antigas para nao crescer indefinidamente.
      Object.keys(daily).forEach(k => { if (k !== today) delete daily[k]; });
      localStorage.setItem(TICKET_DAILY_KEY, JSON.stringify(daily));
    } catch (_) {}
  }

  async function openTicketAuto(titulo, descricao, categoria, tipo) {
    if (!loadPairing()) return null;
    if (!navigator.onLine) return null;
    const tipoKey = tipo || titulo;
    if (!_canOpenTicket(tipoKey)) return null;
    let errorLogJson = null;
    try {
      const errors = JSON.parse(localStorage.getItem('cdb-error-log') || '[]');
      if (errors.length > 0) errorLogJson = JSON.stringify(errors.slice(-5), null, 2);
    } catch (_) {}
    try {
      const resp = await fetch(API_BASE_URL + API_PREFIX + '/tickets', {
        method: 'POST',
        headers: baseHeaders(),
        body: JSON.stringify({
          titulo: String(titulo || 'Erro detectado no app').slice(0, 200),
          descricao: String(descricao || '').slice(0, 10000),
          categoria: categoria || 'Bug',
          errorLogJson: errorLogJson
        })
      });
      if (!resp.ok) return null;
      const data = await resp.json();
      _markTicketOpened(tipoKey);
      try {
        window.dispatchEvent(new CustomEvent('cdb-ticket-opened', {
          detail: { ticketId: data.ticketId, titulo: data.titulo }
        }));
      } catch (_) {}
      return data;
    } catch (_) { return null; }
  }

  // Auto-pair on first boot. Dois modos exclusivos (provisioning prefere):
  //  1. forcedProvisioningSecret (preferido) — token de longa vida embutido
  //     no APK + 3 env vars no server (AppProvisioning:Secret/EmpresaId/LojaId).
  //     APK pareia sozinho na primeira execucao, sem operador digitar nada.
  //  2. forcedPairingCode (legacy) — codigo de 6 digitos gerado no Admin e
  //     embutido pelo workflow build-casadababa-release.yml. Expira em ~10min.
  // No PWA padrao ambos sao undefined — bloco vira no-op.
  // Idempotente: se cdb-pairing ja existe (ex: APK atualizado por cima de
  // instalacao previamente pareada), pula.
  // Retry com backoff (60s, 2min, 5min) ate succeed - cobre cenarios onde
  // primeira tentativa falha por CORS/network e a segunda passa.
  let _autoPairAttempt = 0;
  // Retry agressivo: 5s, 15s, 30s, 1min, 2min, 5min, 10min. Antes era
  // 60s, 2min, 5min — primeira tentativa demorava demais quando a
  // primeira falha era CORS transitorio que se resolvia em segundos.
  const _autoPairDelays = [5000, 15000, 30000, 60000, 120000, 300000, 600000];
  function _logAutoPairError(msg) {
    _trace('auto-pair', msg, null);
    try {
      if (typeof window !== 'undefined' && typeof window.logError === 'function') {
        window.logError(new Error(msg), 'auto-pair');
      }
    } catch (_) {}
    try { console.warn('[auto-pair]', msg); } catch (_) {}
  }
  // Healthcheck do servidor antes de qualquer tentativa de pareamento.
  // Resolve `true` se servidor responde, `false` se 4xx/5xx/timeout.
  async function _serverHealthOk() {
    if (!navigator.onLine) return false;
    try {
      const ctrl = new AbortController();
      const t = setTimeout(() => ctrl.abort(), 8000);
      const r = await fetch(API_BASE_URL + '/health/live', { signal: ctrl.signal });
      clearTimeout(t);
      return r.ok;
    } catch (_) { return false; }
  }
  setTimeout(async function autoPairFromForcedCode() {
    _autoPairAttempt++;
    try {
      if (loadPairing()) {
        if (_autoPairAttempt === 1) _trace('auto-pair', 'skip: ja pareado', null);
        return;
      }
      const cfg = window.CDB_CONFIG || {};
      const provisioningSecret = cfg.forcedProvisioningSecret;
      const code = cfg.forcedPairingCode;

      // CONTINGÊNCIA: APK gerado sem o secret embutido = problema do build,
      // não da rede. Para de tentar e deixa diagnóstico óbvio.
      if (!provisioningSecret && !code) {
        if (_autoPairAttempt === 1) {
          if (typeof window.CDB_CONFIG === 'undefined') {
            _trace('auto-pair', 'ABORT: window.CDB_CONFIG undefined — config.js nao foi carregado pelo index.html', null);
          } else {
            _trace('auto-pair', 'ABORT: sem forcedProvisioningSecret nem forcedPairingCode no config.js — APK gerado sem secret embutido', null);
          }
        }
        return;
      }

      if (!navigator.onLine) {
        _trace('auto-pair', 'tentativa ' + _autoPairAttempt + ' offline — retry em 30s', null);
        setTimeout(autoPairFromForcedCode, 30000);
        return;
      }

      // Pré-flight: server respondendo? Evita acumular tentativas inúteis
      // quando o servidor está down (e tickets falsos por sync.flush failure).
      const healthy = await _serverHealthOk();
      if (!healthy) {
        _trace('auto-pair', 'tentativa ' + _autoPairAttempt + ' health probe FAIL em ' + API_BASE_URL + '/health/live', null);
        const delay = _autoPairDelays[Math.min(_autoPairAttempt - 1, _autoPairDelays.length - 1)];
        setTimeout(autoPairFromForcedCode, delay);
        return;
      }
      _trace('auto-pair', 'tentativa ' + _autoPairAttempt + ' health probe OK', null);

      // Modo 1: provisioning secret (preferido)
      if (provisioningSecret) {
        try {
          await pairAuto(provisioningSecret, cfg.forcedProvisioningLabel || cfg.forcedPairingLabel || 'Casa da Baba');
          _trace('auto-pair', 'SUCESSO via provisioning secret tentativa ' + _autoPairAttempt, null);
          return;
        } catch (e) {
          _logAutoPairError('tentativa ' + _autoPairAttempt + ' provisioning falhou: ' + (e && e.message || e));
          if (!code) {
            const delay = _autoPairDelays[Math.min(_autoPairAttempt - 1, _autoPairDelays.length - 1)];
            _trace('auto-pair', 'retry em ' + delay + 'ms', null);
            setTimeout(autoPairFromForcedCode, delay);
            return;
          }
        }
      }
      // Modo 2: pairing code legacy
      try {
        await pairWithCode(code, cfg.forcedPairingLabel || 'Casa da Baba');
        _trace('auto-pair', 'SUCESSO via pairing code tentativa ' + _autoPairAttempt, null);
      } catch (e) {
        _logAutoPairError('pairing code falhou: ' + (e && e.message || e));
        const delay = _autoPairDelays[Math.min(_autoPairAttempt - 1, _autoPairDelays.length - 1)];
        setTimeout(autoPairFromForcedCode, delay);
      }
    } catch (e) {
      _logAutoPairError('boot exception: ' + (e && e.message || e));
    }
  }, 2000);

  // Expõe utilitários pra debug + integração com Diagnóstico
  window.cdbSync = {
    flush, pull, stop,
    pingVersion, uploadErrorLog,
    pairWithCode, pairAuto,
    openTicketAuto,
    // Contingência: trace + diagnóstico expostos pro Diagnóstico UI usar
    getTrace, clearTrace, exportTraceText,
    serverHealthOk: _serverHealthOk,
    // Força um único ciclo de pareamento manualmente (botão Diagnóstico).
    forcePairNow: function () { _autoPairAttempt = 0; return (async function () {
      const cfg = window.CDB_CONFIG || {};
      const secret = cfg.forcedProvisioningSecret;
      const code = cfg.forcedPairingCode;
      if (secret) return await pairAuto(secret, cfg.forcedProvisioningLabel || 'Casa da Baba');
      if (code) return await pairWithCode(code, cfg.forcedPairingLabel || 'Casa da Baba');
      throw new Error('Nenhum secret/codigo embutido no APK — não é possível parear automaticamente.');
    })(); },
    getPairing: loadPairing,
    clearPairing,
    isPairingInvalid: () => _pairingInvalid,
    fetchCommands: fetchAndProcessCommands,
    startRealtime, stopRealtime,
    realtimeConnected: () => _sseSource && _sseSource.readyState === 1,
    listLojasDisponiveis, switchLoja,
    uploadBackup, maybeAutoBackup,
    localBackupToFile, readLocalBackup, scheduleLocalBackup,
    pickBackupViaSAF,
    // A5: restore-on-reinstall — index.html chama applyBackupSnapshot
    // depois de o operador confirmar no modal disparado por cdb-restore-prompt.
    maybeOfferRestoreOnReinstall, applyBackupSnapshot,
    // A6: stripBatchPhotoBytes exposto pra testes — retorna copia do batch
    // sem bytes de fotos pesadas (substituidos por *Hash). Estado local
    // permanece intacto.
    computeStrippedBatch: stripBatchPhotoBytes,
    // B/C/D: hooks de inspecao pra testes e diagnostico runtime. Nao parte
    // do contrato publico — uso em UI deve ser raro/justificado.
    _internal: {
      nowSafe: nowSafe,                                   // B3
      getServerTimeOffsetMs: () => _serverTimeOffsetMs,   // B3
      getLastSeenServerTs: () => _lastSeenServerTs,       // B3
      mutationMigrators: mutationMigrators,               // B4 (mutable: register migrators)
      mutationSchemaVersion: PWA_MUTATION_SCHEMA_VERSION, // B4
      swSupported: () => _swSupported                     // D2
    },
    hasPublicBackupPlugin: () => !!_getPublicBackupPlugin(),
    lastLocalBackupAt: () => parseInt(localStorage.getItem(LAST_LOCAL_BACKUP_KEY) || '0', 10) || 0,
    lastBackupAt: () => parseInt(localStorage.getItem(LAST_BACKUP_KEY) || '0', 10) || 0,
    queueSize: () => loadQueue().length,
    clearQueue: () => { saveQueue([]); updatePendingCount(); },
    // F10-C-2: acesso ao queue-store pra diagnostico e export/import
    queueStore: typeof window.cdbQueueStore !== 'undefined' ? window.cdbQueueStore : null,
    getQueueStats: async () => {
      if (typeof window.cdbQueueStore !== 'undefined' && window.cdbQueueStore.ready) {
        return window.cdbQueueStore.getStats();
      }
      var q = loadQueue();
      return { queueCount: q.length, deadletterCount: 0, conflictCount: 0, storage: 'localStorage', degraded: true };
    },
    pushAll, pushAllAndFlush,
    // Onda 9 — OTA do PWA: ver maybeApplyPwaUpdate() no fluxo de pingVersion().
    forceUpdate: (opts) => triggerPwaUpdate(Object.assign({ reason: 'manual', force: true }, opts || {})),
    installedPwaVersion: getInstalledPwaVersion,
    deviceId,
    apiBaseUrl: API_BASE_URL
  };
})();
