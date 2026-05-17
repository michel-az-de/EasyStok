/**
 * F10-C-2 — Checksum Web Worker.
 *
 * Recebe {items} via postMessage, retorna {sha256, count}.
 * Canonicaliza keys antes de hash pra garantir estabilidade.
 * Usa crypto.subtle.digest (SHA-256).
 *
 * Fallback: se Worker indisponivel, sync.js usa computeChecksumMainThread()
 * via requestIdleCallback em chunks de 50 itens.
 */
'use strict';

// ---------- Canonicalize ----------
function canonicalize(obj) {
  if (obj === null || obj === undefined) return 'null';
  if (typeof obj === 'string') return JSON.stringify(obj);
  if (typeof obj === 'number' || typeof obj === 'boolean') return String(obj);
  if (Array.isArray(obj)) {
    return '[' + obj.map(canonicalize).join(',') + ']';
  }
  if (typeof obj === 'object') {
    var keys = Object.keys(obj).sort();
    var parts = [];
    for (var i = 0; i < keys.length; i++) {
      parts.push(JSON.stringify(keys[i]) + ':' + canonicalize(obj[keys[i]]));
    }
    return '{' + parts.join(',') + '}';
  }
  return String(obj);
}

// ---------- SHA-256 ----------
async function sha256(text) {
  var encoder = new TextEncoder();
  var data = encoder.encode(text);
  var hashBuffer = await crypto.subtle.digest('SHA-256', data);
  var hashArray = Array.from(new Uint8Array(hashBuffer));
  return hashArray.map(function (b) { return b.toString(16).padStart(2, '0'); }).join('');
}

// ---------- Worker message handler ----------
self.onmessage = async function (e) {
  try {
    var items = e.data.items || [];
    var canonical = canonicalize(items);
    var hash = await sha256(canonical);
    self.postMessage({ sha256: hash, count: items.length });
  } catch (err) {
    self.postMessage({ error: err.message || 'checksum failed', count: 0 });
  }
};
