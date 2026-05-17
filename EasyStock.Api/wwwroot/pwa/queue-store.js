/**
 * F10-C-2 — Queue Store: IndexedDB wrapper para fila de mutations.
 *
 * 4 stores:
 *   cdb-queue          — mutations pendentes (principal)
 *   cdb-deadletter     — mutations que falharam 50x (nunca auto-descartadas)
 *   cdb-conflict-queue  — conflicts aguardando resolucao manual
 *   cdb-audit-log      — round-buffer de eventos locais (retention 30d)
 *
 * Migracao: dual-read localStorage + IDB por 7 dias. Apos 7d, localStorage
 * limpo. Flag 'cdb-queue-storage' = 'idb' | 'ls-fallback' | 'migrating'.
 *
 * Principio #1: nenhum dado some sem acao humana explicita.
 * Principio #6: deviceId persiste atraves de re-pair.
 */
'use strict';

(function (root) {
  var DB_NAME = 'cdb-sync-store';
  var DB_VERSION = 1;
  var STORE_QUEUE = 'cdb-queue';
  var STORE_DEADLETTER = 'cdb-deadletter';
  var STORE_CONFLICTS = 'cdb-conflict-queue';
  var STORE_AUDIT = 'cdb-audit-log';
  var META_KEY = '_meta';
  var LS_QUEUE_KEY = 'cdb-sync-queue';
  var LS_MIGRATION_KEY = 'cdb-queue-migrated-at';
  var LS_STORAGE_KEY = 'cdb-queue-storage';
  var MIGRATION_TTL_DAYS = 7;
  var AUDIT_RETENTION_DAYS = 30;
  var AUDIT_MAX_ENTRIES = 5000;
  var DEADLETTER_CAP = 200;
  var MAX_ATTEMPTS = 50;

  var db = null;
  var _ready = false;
  var _degraded = false; // fallback pra localStorage

  // ---------- IDB open ----------
  function openDb() {
    return new Promise(function (resolve, reject) {
      if (!root.indexedDB) {
        reject(new Error('IndexedDB indisponivel'));
        return;
      }
      var req = indexedDB.open(DB_NAME, DB_VERSION);
      req.onupgradeneeded = function (e) {
        var d = e.target.result;
        if (!d.objectStoreNames.contains(STORE_QUEUE)) {
          d.createObjectStore(STORE_QUEUE, { keyPath: 'id' });
        }
        if (!d.objectStoreNames.contains(STORE_DEADLETTER)) {
          d.createObjectStore(STORE_DEADLETTER, { keyPath: 'id' });
        }
        if (!d.objectStoreNames.contains(STORE_CONFLICTS)) {
          d.createObjectStore(STORE_CONFLICTS, { keyPath: 'id' });
        }
        if (!d.objectStoreNames.contains(STORE_AUDIT)) {
          var audit = d.createObjectStore(STORE_AUDIT, { keyPath: 'id', autoIncrement: true });
          audit.createIndex('ts', 'ts', { unique: false });
        }
      };
      req.onsuccess = function (e) { resolve(e.target.result); };
      req.onerror = function (e) { reject(e.target.error || new Error('IDB open failed')); };
    });
  }

  // ---------- Generic IDB helpers ----------
  function idbGetAll(storeName) {
    return new Promise(function (resolve, reject) {
      if (!db) { resolve([]); return; }
      var tx = db.transaction(storeName, 'readonly');
      var store = tx.objectStore(storeName);
      var req = store.getAll();
      req.onsuccess = function () { resolve(req.result || []); };
      req.onerror = function () { reject(req.error); };
    });
  }

  function idbPutAll(storeName, items) {
    return new Promise(function (resolve, reject) {
      if (!db) { reject(new Error('no db')); return; }
      var tx = db.transaction(storeName, 'readwrite');
      var store = tx.objectStore(storeName);
      for (var i = 0; i < items.length; i++) {
        store.put(items[i]);
      }
      tx.oncomplete = function () { resolve(); };
      tx.onerror = function () { reject(tx.error); };
    });
  }

  function idbDeleteKeys(storeName, keys) {
    return new Promise(function (resolve, reject) {
      if (!db) { reject(new Error('no db')); return; }
      var tx = db.transaction(storeName, 'readwrite');
      var store = tx.objectStore(storeName);
      for (var i = 0; i < keys.length; i++) {
        store.delete(keys[i]);
      }
      tx.oncomplete = function () { resolve(); };
      tx.onerror = function () { reject(tx.error); };
    });
  }

  function idbClear(storeName) {
    return new Promise(function (resolve, reject) {
      if (!db) { reject(new Error('no db')); return; }
      var tx = db.transaction(storeName, 'readwrite');
      tx.objectStore(storeName).clear();
      tx.oncomplete = function () { resolve(); };
      tx.onerror = function () { reject(tx.error); };
    });
  }

  function idbCount(storeName) {
    return new Promise(function (resolve, reject) {
      if (!db) { resolve(0); return; }
      var tx = db.transaction(storeName, 'readonly');
      var req = tx.objectStore(storeName).count();
      req.onsuccess = function () { resolve(req.result); };
      req.onerror = function () { reject(req.error); };
    });
  }

  // ---------- Initialization + migration ----------
  async function init() {
    try {
      db = await openDb();
      _ready = true;
      _degraded = false;
      localStorage.setItem(LS_STORAGE_KEY, 'idb');

      // Migrate from localStorage if not done yet
      await migrateFromLocalStorage();

      // Cleanup old audit entries
      await trimAuditLog();

      return { storage: 'idb', degraded: false };
    } catch (e) {
      console.warn('[queue-store] IDB failed, falling back to localStorage:', e && e.message);
      _degraded = true;
      _ready = true;
      localStorage.setItem(LS_STORAGE_KEY, 'ls-fallback');
      localStorage.setItem('cdb-queue-degraded-storage', 'idb-failed');
      return { storage: 'localStorage', degraded: true };
    }
  }

  async function migrateFromLocalStorage() {
    var migratedAt = localStorage.getItem(LS_MIGRATION_KEY);
    if (migratedAt) {
      // Already migrated. Check if we should clean up localStorage copy.
      var elapsed = Date.now() - parseInt(migratedAt, 10);
      if (elapsed > MIGRATION_TTL_DAYS * 24 * 60 * 60 * 1000) {
        localStorage.removeItem(LS_QUEUE_KEY);
        auditLog('migration', 'localStorage cleanup apos ' + MIGRATION_TTL_DAYS + 'd');
      }
      return;
    }

    // Read from localStorage
    var lsItems = [];
    try {
      lsItems = JSON.parse(localStorage.getItem(LS_QUEUE_KEY) || '[]');
    } catch (e) { lsItems = []; }

    if (lsItems.length === 0) {
      localStorage.setItem(LS_MIGRATION_KEY, String(Date.now()));
      return;
    }

    // Enrich items with F10-C-2 fields
    var now = Date.now();
    for (var i = 0; i < lsItems.length; i++) {
      var item = lsItems[i];
      if (!item.createdAt) item.createdAt = item.ts || now;
      if (!item.attempts) item.attempts = 0;
      if (!item.lastError) item.lastError = null;
      if (!item.lastAttemptAt) item.lastAttemptAt = null;
    }

    // Write to IDB
    await idbPutAll(STORE_QUEUE, lsItems);
    localStorage.setItem(LS_MIGRATION_KEY, String(Date.now()));
    localStorage.setItem(LS_STORAGE_KEY, 'idb');

    auditLog('migration', 'migrated ' + lsItems.length + ' items from localStorage to IDB');
  }

  // ---------- Queue operations ----------
  async function loadAll() {
    if (_degraded) {
      try { return JSON.parse(localStorage.getItem(LS_QUEUE_KEY) || '[]'); } catch (e) { return []; }
    }
    return idbGetAll(STORE_QUEUE);
  }

  async function saveAll(items) {
    if (_degraded) {
      try {
        localStorage.setItem(LS_QUEUE_KEY, JSON.stringify(items));
      } catch (e) {
        if (e && (e.name === 'QuotaExceededError' || e.code === 22)) {
          _onQuotaExceeded('queue-saveAll');
        }
      }
      return;
    }
    // IDB: clear + re-put atomically
    try {
      await new Promise(function (resolve, reject) {
        var tx = db.transaction(STORE_QUEUE, 'readwrite');
        var store = tx.objectStore(STORE_QUEUE);
        store.clear();
        for (var i = 0; i < items.length; i++) {
          store.put(items[i]);
        }
        tx.oncomplete = function () { resolve(); };
        tx.onerror = function () { reject(tx.error); };
      });
    } catch (e) {
      if (e && e.name === 'QuotaExceededError') {
        _onQuotaExceeded('queue-saveAll-idb');
      }
      throw e;
    }

    // Dual-write to localStorage during migration period
    var migratedAt = localStorage.getItem(LS_MIGRATION_KEY);
    if (migratedAt) {
      var elapsed = Date.now() - parseInt(migratedAt, 10);
      if (elapsed < MIGRATION_TTL_DAYS * 24 * 60 * 60 * 1000) {
        try { localStorage.setItem(LS_QUEUE_KEY, JSON.stringify(items)); } catch (_) {}
      }
    }
  }

  async function addOne(item) {
    if (!item.createdAt) item.createdAt = item.ts || Date.now();
    if (!item.attempts) item.attempts = 0;

    if (_degraded) {
      var q = await loadAll();
      q.push(item);
      await saveAll(q);
      return;
    }
    await idbPutAll(STORE_QUEUE, [item]);
  }

  async function removeMany(ids) {
    if (_degraded) {
      var q = await loadAll();
      var idSet = {};
      for (var i = 0; i < ids.length; i++) idSet[ids[i]] = true;
      await saveAll(q.filter(function (m) { return !idSet[m.id]; }));
      return;
    }
    await idbDeleteKeys(STORE_QUEUE, ids);
  }

  async function incrementAttempts(ids) {
    if (!db || _degraded) return;
    var items = await idbGetAll(STORE_QUEUE);
    var idSet = {};
    for (var i = 0; i < ids.length; i++) idSet[ids[i]] = true;
    var toDeadletter = [];
    var toUpdate = [];
    var now = Date.now();
    for (var j = 0; j < items.length; j++) {
      if (!idSet[items[j].id]) continue;
      items[j].attempts = (items[j].attempts || 0) + 1;
      items[j].lastAttemptAt = now;
      if (items[j].attempts >= MAX_ATTEMPTS) {
        toDeadletter.push(items[j]);
      } else {
        toUpdate.push(items[j]);
      }
    }
    if (toUpdate.length > 0) await idbPutAll(STORE_QUEUE, toUpdate);
    if (toDeadletter.length > 0) await moveToDeadletter(toDeadletter, 'max-attempts-exceeded');
  }

  // ---------- Dead-letter ----------
  async function moveToDeadletter(items, reason) {
    if (_degraded) {
      auditLog('deadletter', items.length + ' items -> deadletter (degraded mode, reason: ' + reason + ')');
      return;
    }
    // Check cap
    var currentCount = await idbCount(STORE_DEADLETTER);
    if (currentCount + items.length > DEADLETTER_CAP) {
      _onDeadletterFull();
    }

    var now = Date.now();
    for (var i = 0; i < items.length; i++) {
      items[i].deadletterReason = reason;
      items[i].deadletteredAt = now;
    }

    // Atomic: remove from queue + add to deadletter
    await new Promise(function (resolve, reject) {
      var tx = db.transaction([STORE_QUEUE, STORE_DEADLETTER], 'readwrite');
      var qStore = tx.objectStore(STORE_QUEUE);
      var dlStore = tx.objectStore(STORE_DEADLETTER);
      for (var i = 0; i < items.length; i++) {
        qStore.delete(items[i].id);
        dlStore.put(items[i]);
      }
      tx.oncomplete = function () { resolve(); };
      tx.onerror = function () { reject(tx.error); };
    });

    auditLog('deadletter', items.length + ' items movidos (reason: ' + reason + ')');
  }

  async function getDeadletter() {
    if (_degraded) return [];
    return idbGetAll(STORE_DEADLETTER);
  }

  async function clearDeadletter(ids) {
    if (_degraded) return;
    await idbDeleteKeys(STORE_DEADLETTER, ids);
    auditLog('deadletter-cleared', ids.length + ' items removidos');
  }

  // ---------- Conflict queue ----------
  async function addConflict(item) {
    if (_degraded) return;
    item.conflictedAt = Date.now();
    await idbPutAll(STORE_CONFLICTS, [item]);
    auditLog('conflict', 'mutation ' + item.id + ' -> conflict queue');
  }

  async function getConflicts() {
    if (_degraded) return [];
    return idbGetAll(STORE_CONFLICTS);
  }

  async function resolveConflict(id, resolution) {
    if (_degraded) return;
    await idbDeleteKeys(STORE_CONFLICTS, [id]);
    auditLog('conflict-resolved', id + ' resolved: ' + resolution);
  }

  // ---------- Audit log ----------
  async function auditLog(action, detail) {
    var entry = {
      id: Date.now() + '-' + Math.random().toString(36).substr(2, 6),
      action: action,
      detail: detail,
      ts: Date.now()
    };
    if (_degraded) return; // no audit in degraded mode
    try {
      await idbPutAll(STORE_AUDIT, [entry]);
    } catch (_) {}
  }

  async function getAuditLog(limit) {
    if (_degraded) return [];
    var all = await idbGetAll(STORE_AUDIT);
    all.sort(function (a, b) { return b.ts - a.ts; });
    return all.slice(0, limit || AUDIT_MAX_ENTRIES);
  }

  async function trimAuditLog() {
    if (_degraded || !db) return;
    try {
      var all = await idbGetAll(STORE_AUDIT);
      var cutoff = Date.now() - (AUDIT_RETENTION_DAYS * 24 * 60 * 60 * 1000);
      var toRemove = [];
      for (var i = 0; i < all.length; i++) {
        if (all[i].ts < cutoff) toRemove.push(all[i].id);
      }
      // Also trim to max entries
      if (all.length > AUDIT_MAX_ENTRIES) {
        all.sort(function (a, b) { return b.ts - a.ts; });
        for (var j = AUDIT_MAX_ENTRIES; j < all.length; j++) {
          if (toRemove.indexOf(all[j].id) === -1) toRemove.push(all[j].id);
        }
      }
      if (toRemove.length > 0) {
        await idbDeleteKeys(STORE_AUDIT, toRemove);
      }
    } catch (_) {}
  }

  // ---------- Export / Import ----------
  async function exportAll() {
    var queue = await loadAll();
    var deadletter = await getDeadletter();
    var conflicts = await getConflicts();
    var audit = await getAuditLog(AUDIT_MAX_ENTRIES);
    return {
      schema: 'cdb-export-v1',
      deviceId: localStorage.getItem('cdb-device-id') || '',
      empresaId: localStorage.getItem('cdb-empresa-id') || '',
      lojaId: localStorage.getItem('cdb-loja-id') || '',
      exportedAt: Date.now(),
      queue: queue,
      deadletter: deadletter,
      conflicts: conflicts,
      auditLog: audit
    };
  }

  async function importBundle(bundle) {
    if (!bundle || bundle.schema !== 'cdb-export-v1') {
      throw new Error('Bundle invalido: schema desconhecido');
    }
    // Validate expiry (7 days)
    if (bundle.exportedAt && (Date.now() - bundle.exportedAt > 7 * 24 * 60 * 60 * 1000)) {
      throw new Error('Bundle expirado (>7 dias)');
    }
    // Validate empresaId match
    var currentEmpresa = localStorage.getItem('cdb-empresa-id') || '';
    if (bundle.empresaId && currentEmpresa && bundle.empresaId !== currentEmpresa) {
      throw new Error('EmpresaId nao confere (cross-tenant bloqueado)');
    }

    var imported = 0;
    if (bundle.queue && bundle.queue.length > 0) {
      await idbPutAll(STORE_QUEUE, bundle.queue);
      imported += bundle.queue.length;
    }
    if (bundle.deadletter && bundle.deadletter.length > 0) {
      await idbPutAll(STORE_DEADLETTER, bundle.deadletter);
      imported += bundle.deadletter.length;
    }
    if (bundle.conflicts && bundle.conflicts.length > 0) {
      await idbPutAll(STORE_CONFLICTS, bundle.conflicts);
      imported += bundle.conflicts.length;
    }
    auditLog('import', 'imported ' + imported + ' items from bundle (device: ' + (bundle.deviceId || '?') + ')');
    return { imported: imported };
  }

  // ---------- Stats / Meta ----------
  async function getStats() {
    var queueItems = await loadAll();
    var dlCount = _degraded ? 0 : await idbCount(STORE_DEADLETTER);
    var conflictCount = _degraded ? 0 : await idbCount(STORE_CONFLICTS);

    var oldest = null;
    var criticalCount = 0;
    var criticalTypes = { 'order.upsert': 1, 'cashEntry.upsert': 1, 'batch.upsert': 1 };
    for (var i = 0; i < queueItems.length; i++) {
      var item = queueItems[i];
      var age = item.createdAt || item.ts;
      if (!oldest || age < oldest) oldest = age;
      if (criticalTypes[item.type]) criticalCount++;
    }

    return {
      queueCount: queueItems.length,
      deadletterCount: dlCount,
      conflictCount: conflictCount,
      oldestItemAge: oldest ? Date.now() - oldest : 0,
      criticalPending: criticalCount,
      storage: _degraded ? 'localStorage' : 'idb',
      degraded: _degraded
    };
  }

  // ---------- Quota / limits ----------
  function _onQuotaExceeded(source) {
    localStorage.setItem('cdb-storage-exhausted', 'true');
    auditLog('quota', 'QuotaExceededError from ' + source);
    try {
      if (root.cdbApp && typeof root.cdbApp.showToast === 'function') {
        root.cdbApp.showToast('Armazenamento local cheio. Conecte a internet para sincronizar.', 'error');
      }
      root.dispatchEvent(new CustomEvent('cdb-storage-exhausted', { detail: { source: source } }));
    } catch (_) {}
  }

  function _onDeadletterFull() {
    auditLog('deadletter-full', 'cap ' + DEADLETTER_CAP + ' atingido');
    try {
      if (root.cdbApp && typeof root.cdbApp.showToast === 'function') {
        root.cdbApp.showToast('Fila de erros cheia (' + DEADLETTER_CAP + '). Exporte antes de continuar.', 'error');
      }
      root.dispatchEvent(new CustomEvent('cdb-deadletter-full'));
    } catch (_) {}
  }

  // ---------- Public API ----------
  var queueStore = {
    init: init,
    loadAll: loadAll,
    saveAll: saveAll,
    addOne: addOne,
    removeMany: removeMany,
    incrementAttempts: incrementAttempts,
    moveToDeadletter: moveToDeadletter,
    getDeadletter: getDeadletter,
    clearDeadletter: clearDeadletter,
    addConflict: addConflict,
    getConflicts: getConflicts,
    resolveConflict: resolveConflict,
    auditLog: auditLog,
    getAuditLog: getAuditLog,
    exportAll: exportAll,
    importBundle: importBundle,
    getStats: getStats,
    get ready() { return _ready; },
    get degraded() { return _degraded; },
    MAX_ATTEMPTS: MAX_ATTEMPTS,
    DEADLETTER_CAP: DEADLETTER_CAP
  };

  // Export
  if (typeof module !== 'undefined' && module.exports) {
    module.exports = queueStore;
  } else {
    root.cdbQueueStore = queueStore;
  }
})(typeof window !== 'undefined' ? window : globalThis);
