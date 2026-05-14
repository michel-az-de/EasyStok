/**
 * F10-C-7 — Photo Store (IndexedDB separado).
 *
 * Armazena fotos base64 de batches fora da fila de sync,
 * evitando quota overflow. IDB separado do queue-store pra
 * não competir por espaço.
 *
 * Estrutura:
 *   DB: cdb-photo-store, v1
 *   Store: photos  — key = hash (fnv1a:hex:len), value = { hash, dataUrl, batchId, field, uploadedAt?, ts }
 *
 * API:
 *   init()               — abre IDB
 *   save(hash, dataUrl, batchId, field)  — persiste foto
 *   get(hash)            — retorna entry ou null
 *   remove(hash)         — deleta entry
 *   listPending()        — fotos não uploadadas (uploadedAt == null)
 *   markUploaded(hash)   — seta uploadedAt = Date.now()
 *   getStats()           — { total, pending, uploadedCount, totalSizeEstimate }
 *   clear()              — limpa tudo (debug)
 */
'use strict';
(function () {
  var DB_NAME = 'cdb-photo-store';
  var DB_VERSION = 1;
  var STORE = 'photos';
  var _db = null;
  var _ready = false;

  // ---------- IDB helpers ----------
  function openDb() {
    return new Promise(function (resolve, reject) {
      if (typeof indexedDB === 'undefined') {
        reject(new Error('IndexedDB indisponivel'));
        return;
      }
      var req = indexedDB.open(DB_NAME, DB_VERSION);
      req.onupgradeneeded = function (e) {
        var db = e.target.result;
        if (!db.objectStoreNames.contains(STORE)) {
          var store = db.createObjectStore(STORE, { keyPath: 'hash' });
          store.createIndex('pending', 'uploadedAt', { unique: false });
          store.createIndex('batchId', 'batchId', { unique: false });
        }
      };
      req.onsuccess = function (e) { resolve(e.target.result); };
      req.onerror = function (e) { reject(e.target.error || new Error('IDB open failed')); };
    });
  }

  function tx(mode) {
    if (!_db) throw new Error('photo-store nao inicializado');
    return _db.transaction(STORE, mode);
  }

  function idbReq(req) {
    return new Promise(function (resolve, reject) {
      req.onsuccess = function () { resolve(req.result); };
      req.onerror = function () { reject(req.error || new Error('IDB request failed')); };
    });
  }

  // ---------- Public API ----------
  async function init() {
    if (_ready && _db) return;
    try {
      _db = await openDb();
      _ready = true;
    } catch (e) {
      console.warn('[photo-store] IDB init falhou:', e && e.message);
      _ready = false;
    }
  }

  /**
   * Persiste foto.
   * @param {string} hash - FNV-1a hash (fnv1a:hex:len)
   * @param {string} dataUrl - data:image/... base64
   * @param {string} batchId - ID do batch dono da foto
   * @param {string} field - 'batchPhoto' ou 'item:<itemIndex>'
   */
  async function save(hash, dataUrl, batchId, field) {
    if (!_ready) return;
    var t = tx('readwrite');
    var store = t.objectStore(STORE);
    var entry = {
      hash: hash,
      dataUrl: dataUrl,
      batchId: batchId,
      field: field,
      uploadedAt: null,
      ts: Date.now()
    };
    await idbReq(store.put(entry));
  }

  async function get(hash) {
    if (!_ready) return null;
    var t = tx('readonly');
    return await idbReq(t.objectStore(STORE).get(hash));
  }

  async function remove(hash) {
    if (!_ready) return;
    var t = tx('readwrite');
    await idbReq(t.objectStore(STORE).delete(hash));
  }

  /**
   * Lista fotos que ainda nao foram uploadadas ao server.
   * @returns {Promise<Array>}
   */
  async function listPending() {
    if (!_ready) return [];
    var t = tx('readonly');
    var store = t.objectStore(STORE);
    var idx = store.index('pending');
    // uploadedAt == null → IDBKeyRange.only(null) nao funciona cross-browser.
    // Scan completo e filtra.
    return new Promise(function (resolve, reject) {
      var results = [];
      var cursor = store.openCursor();
      cursor.onsuccess = function (e) {
        var c = e.target.result;
        if (c) {
          if (!c.value.uploadedAt) results.push(c.value);
          c.continue();
        } else {
          resolve(results);
        }
      };
      cursor.onerror = function () { reject(cursor.error); };
    });
  }

  /**
   * Lista fotos de um batch especifico.
   */
  async function listByBatch(batchId) {
    if (!_ready) return [];
    var t = tx('readonly');
    var store = t.objectStore(STORE);
    var idx = store.index('batchId');
    return new Promise(function (resolve, reject) {
      var results = [];
      var range = IDBKeyRange.only(batchId);
      var cursor = idx.openCursor(range);
      cursor.onsuccess = function (e) {
        var c = e.target.result;
        if (c) {
          results.push(c.value);
          c.continue();
        } else {
          resolve(results);
        }
      };
      cursor.onerror = function () { reject(cursor.error); };
    });
  }

  async function markUploaded(hash) {
    if (!_ready) return;
    var t = tx('readwrite');
    var store = t.objectStore(STORE);
    var existing = await idbReq(store.get(hash));
    if (!existing) return;
    existing.uploadedAt = Date.now();
    await idbReq(store.put(existing));
  }

  async function getStats() {
    if (!_ready) return { total: 0, pending: 0, uploadedCount: 0, totalSizeEstimate: 0 };
    var t = tx('readonly');
    var store = t.objectStore(STORE);
    return new Promise(function (resolve, reject) {
      var total = 0, pending = 0, uploaded = 0, size = 0;
      var cursor = store.openCursor();
      cursor.onsuccess = function (e) {
        var c = e.target.result;
        if (c) {
          total++;
          if (c.value.uploadedAt) uploaded++;
          else pending++;
          if (c.value.dataUrl) size += c.value.dataUrl.length;
          c.continue();
        } else {
          resolve({ total: total, pending: pending, uploadedCount: uploaded, totalSizeEstimate: size });
        }
      };
      cursor.onerror = function () { reject(cursor.error); };
    });
  }

  async function clear() {
    if (!_ready) return;
    var t = tx('readwrite');
    await idbReq(t.objectStore(STORE).clear());
  }

  // ---------- Expose ----------
  window.cdbPhotoStore = {
    init: init,
    save: save,
    get: get,
    remove: remove,
    listPending: listPending,
    listByBatch: listByBatch,
    markUploaded: markUploaded,
    getStats: getStats,
    clear: clear,
    get ready() { return _ready; }
  };
})();
