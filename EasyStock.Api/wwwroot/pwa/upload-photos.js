/**
 * F10-C-7 — Background photo uploader.
 *
 * Após sync de batches (aceitos pelo server), percorre o photo-store
 * e faz upload das fotos pendentes via POST multipart.
 *
 * Endpoint: POST /api/mobile/batches/{batchId}/photos
 * Body: FormData com { hash, field, photo (Blob) }
 *
 * Idempotente via hash — server ignora se foto já existe.
 * Roda em background após cada flush bem-sucedido.
 */
'use strict';
(function () {
  var _uploading = false;
  var MAX_CONCURRENT = 2;
  var RETRY_DELAY = 30000; // 30s entre tentativas

  /**
   * Converte data-URL base64 em Blob.
   */
  function dataUrlToBlob(dataUrl) {
    var parts = dataUrl.split(',');
    var mime = (parts[0].match(/:(.*?);/) || [])[1] || 'image/jpeg';
    var raw = atob(parts[1]);
    var arr = new Uint8Array(raw.length);
    for (var i = 0; i < raw.length; i++) arr[i] = raw.charCodeAt(i);
    return new Blob([arr], { type: mime });
  }

  /**
   * Upload de uma foto pro server.
   * @returns {Promise<boolean>} true se sucesso, false se falha
   */
  async function uploadOne(entry, apiBase, headers) {
    try {
      var blob = dataUrlToBlob(entry.dataUrl);
      var form = new FormData();
      form.append('hash', entry.hash);
      form.append('field', entry.field || 'unknown');
      form.append('photo', blob, 'photo.jpg');

      var url = apiBase + '/api/mobile/batches/' + encodeURIComponent(entry.batchId) + '/photos';
      var ctrl = new AbortController();
      var tId = setTimeout(function() { ctrl.abort(); }, RETRY_DELAY);
      var resp;
      try {
        resp = await fetch(url, { method: 'POST', headers: headers, body: form, signal: ctrl.signal });
      } finally {
        clearTimeout(tId);
      }

      if (resp.ok || resp.status === 409) {
        // 200 = uploaded, 409 = already exists (idempotente)
        await window.cdbPhotoStore.markUploaded(entry.hash);
        return true;
      }

      if (resp.status === 401 || resp.status === 403) {
        // Auth falhou — nao retenta ate re-pair
        console.warn('[photo-upload] auth failed for', entry.hash, resp.status);
        return false;
      }

      console.warn('[photo-upload] upload failed', entry.hash, resp.status);
      return false;
    } catch (e) {
      console.warn('[photo-upload] error uploading', entry.hash, e && e.message);
      return false;
    }
  }

  /**
   * Flush: envia todas as fotos pendentes, MAX_CONCURRENT por vez.
   * Chamado após cada sync flush bem-sucedido.
   */
  async function flushPhotos() {
    if (_uploading || (window.cdbSync && window.cdbSync.isSyncHeldForImport && window.cdbSync.isSyncHeldForImport())) return;
    if (!window.cdbPhotoStore || !window.cdbPhotoStore.ready) return;

    var pending;
    try {
      pending = await window.cdbPhotoStore.listPending();
    } catch (e) {
      return;
    }
    if (!pending || pending.length === 0) return;

    // Resolve API base e headers
    var sync = window.cdbSync;
    if (!sync) return;
    var apiBase = '';
    var headers = deviceHeaders();
    if (!headers) return; // sem apiKey, nao pode fazer upload

    _uploading = true;
    try {
      // Upload em chunks de MAX_CONCURRENT
      for (var i = 0; i < pending.length; i += MAX_CONCURRENT) {
        if (sync.isSyncHeldForImport && sync.isSyncHeldForImport()) return;
        var chunk = pending.slice(i, i + MAX_CONCURRENT);
        var promises = chunk.map(function (entry) {
          return uploadOne(entry, apiBase, headers);
        });
        await Promise.all(promises);
      }
    } catch (e) {
      console.warn('[photo-upload] flush error:', e && e.message);
    } finally {
      _uploading = false;
    }
  }

  /**
   * Limpa fotos ja uploadadas com mais de N dias (economia de espaco).
   * @param {number} days — default 7
   */
  async function cleanupUploaded(days) {
    if (!window.cdbPhotoStore || !window.cdbPhotoStore.ready) return;
    days = days || 7;
    var cutoff = Date.now() - (days * 86400000);
    try {
      var uploaded = await window.cdbPhotoStore.listUploaded();
      if (!uploaded || uploaded.length === 0) return;
      var toRemove = [];
      for (var i = 0; i < uploaded.length; i++) {
        if (uploaded[i].uploadedAt && uploaded[i].uploadedAt < cutoff) {
          toRemove.push(uploaded[i].hash);
        }
      }
      for (var j = 0; j < toRemove.length; j++) {
        await window.cdbPhotoStore.remove(toRemove[j]);
      }
      if (toRemove.length > 0) {
        console.log('[photo-upload] cleaned up ' + toRemove.length + ' uploaded photos older than ' + days + 'd');
      }
    } catch (e) {
      console.warn('[photo-upload] cleanup error:', e && e.message);
    }
  }

  /** Headers do aparelho pareado, ou null sem apiKey. */
  function deviceHeaders() {
    try {
      var pairing = JSON.parse(localStorage.getItem('cdb-pairing') || '{}');
      var apiKey = pairing.apiKey || '';
      if (!apiKey) return null;
      var deviceId = pairing.deviceId || localStorage.getItem('cdb-device-id') || '';
      return { 'X-Mobile-Api-Key': apiKey, 'X-Device-Id': deviceId };
    } catch (e) {
      return null;
    }
  }

  // #2459: a foto de lote no servidor exige a chave do aparelho. <img> nao manda header,
  // entao buscamos com fetch e exibimos por blob URL (cacheada por URL na sessao).
  var ROTA_FOTO_LOTE = /^\/api\/mobile\/batches\/[^/?#]+\/photos\/[A-Za-z0-9_-]+\.(jpg|png)$/;
  var _fotosAutenticadas = new Map();

  function ehFotoAutenticada(url) {
    return typeof url === 'string' && ROTA_FOTO_LOTE.test(url) && url.indexOf('..') < 0;
  }

  function srcAutenticado(url) {
    if (!ehFotoAutenticada(url)) return Promise.resolve(url || null);
    if (!_fotosAutenticadas.has(url)) {
      var busca = (async function () {
        var headers = deviceHeaders();
        if (!headers) return null;
        try {
          var resp = await fetch(url, { headers: headers });
          if (!resp.ok) return null;
          return URL.createObjectURL(await resp.blob());
        } catch (e) {
          return null;
        }
      })().then(function (src) {
        if (!src) _fotosAutenticadas.delete(url); // falha nao fica em cache: tenta de novo depois
        return src;
      });
      _fotosAutenticadas.set(url, busca);
    }
    return _fotosAutenticadas.get(url);
  }

  /** Preenche as <img data-foto-auth> de root; sem foto acessivel, a <img> sai. */
  function hidratarFotos(root) {
    if (!root || !root.querySelectorAll) return;
    Array.prototype.forEach.call(root.querySelectorAll('img[data-foto-auth]'), function (img) {
      var url = img.getAttribute('data-foto-auth');
      img.removeAttribute('data-foto-auth');
      srcAutenticado(url).then(function (src) {
        if (src) img.src = src;
        else if (img.parentNode) img.parentNode.removeChild(img);
      });
    });
  }

  // ---------- Expose ----------
  window.cdbPhotoUpload = {
    flush: flushPhotos,
    cleanup: cleanupUploaded,
    ehFotoAutenticada: ehFotoAutenticada,
    srcAutenticado: srcAutenticado,
    hidratarFotos: hidratarFotos,
    get uploading() { return _uploading; }
  };
})();
