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
    if (_uploading) return;
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
    var headers = {};
    try {
      // Tenta pegar apiBase e apiKey do sync state
      var pairing = JSON.parse(localStorage.getItem('cdb-pairing') || '{}');
      var apiKey = pairing.apiKey || '';
      var deviceId = pairing.deviceId || localStorage.getItem('cdb-device-id') || '';
      if (!apiKey) return; // sem apiKey, nao pode fazer upload
      headers = {
        'X-Mobile-Api-Key': apiKey,
        'X-Device-Id': deviceId
      };
    } catch (e) {
      return;
    }

    _uploading = true;
    try {
      // Upload em chunks de MAX_CONCURRENT
      for (var i = 0; i < pending.length; i += MAX_CONCURRENT) {
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

  // ---------- Expose ----------
  window.cdbPhotoUpload = {
    flush: flushPhotos,
    cleanup: cleanupUploaded,
    get uploading() { return _uploading; }
  };
})();
