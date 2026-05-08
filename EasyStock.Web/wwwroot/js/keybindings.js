/* EasyStock keybindings registry — Fase 1
 *
 * Registry central para atalhos de teclado. Resolve colisoes entre Ctrl+K, /, Esc,
 * setas (atalhos existentes) e novos atalhos g+letra, ? (Fase 5).
 *
 * Uso:
 *   EasyKeys.register({
 *     id: 'global.search',
 *     keys: 'mod+k',          // 'mod' = Ctrl no Win/Linux, Cmd no Mac
 *     description: 'Abrir busca unificada',
 *     when: 'global',         // 'global' | 'no-input' | (ev) => bool
 *     handler: ev => { ev.preventDefault(); openSearch(); }
 *   });
 *
 * Atalho de sequencia (g+letra):
 *   EasyKeys.register({ id: 'go.produtos', keys: 'g p', description: 'Ir para Produtos', when: 'no-input', handler: () => location.href = '/produtos' });
 *
 * Listar atalhos (cheatsheet):
 *   EasyKeys.list().forEach(b => ...)
 */
(function (global) {
  'use strict';

  if (global.EasyKeys && global.EasyKeys.__initialized) return;

  const SEQUENCE_TIMEOUT_MS = 800;
  const isMac = /Mac|iPhone|iPod|iPad/i.test(navigator.platform || navigator.userAgent || '');

  const bindings = new Map();
  let pendingSequence = null;
  let pendingTimer = null;

  function normalizeCombo(keys) {
    // Suporta 'mod+k', 'ctrl+shift+p', 'g p' (sequence), '?', 'Escape'
    const parts = keys.trim().toLowerCase().split(/\s+/);
    return parts.map(part => {
      const tokens = part.split('+').map(t => t.trim());
      const out = { mod: false, ctrl: false, alt: false, shift: false, meta: false, key: '' };
      tokens.forEach(t => {
        if (t === 'mod') out.mod = true;
        else if (t === 'ctrl') out.ctrl = true;
        else if (t === 'alt') out.alt = true;
        else if (t === 'shift') out.shift = true;
        else if (t === 'meta' || t === 'cmd') out.meta = true;
        else out.key = t;
      });
      return out;
    });
  }

  function modifiersMatch(part, ev) {
    const wantMod = part.mod;
    const ctrlOrMeta = isMac ? ev.metaKey : ev.ctrlKey;
    if (wantMod && !ctrlOrMeta) return false;
    if (!wantMod && part.ctrl && !ev.ctrlKey) return false;
    if (!wantMod && part.meta && !ev.metaKey) return false;
    if (part.alt && !ev.altKey) return false;
    if (part.shift && !ev.shiftKey) return false;
    // Modificadores nao requeridos nao devem estar acionados (exceto shift para teclas como ?)
    if (!wantMod && !part.ctrl && !part.meta && ctrlOrMeta) return false;
    if (!part.alt && ev.altKey) return false;
    return true;
  }

  function keyMatches(part, ev) {
    if (!modifiersMatch(part, ev)) return false;
    const k = (ev.key || '').toLowerCase();
    if (part.key === 'space') return k === ' ';
    if (part.key === 'esc') return k === 'escape';
    return k === part.key;
  }

  function isInInput(ev) {
    const el = ev.target;
    if (!el || !el.tagName) return false;
    const tag = el.tagName;
    if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return true;
    if (el.isContentEditable) return true;
    return false;
  }

  function evaluateWhen(when, ev) {
    if (typeof when === 'function') return !!when(ev);
    if (when === 'no-input') return !isInInput(ev);
    return true; // 'global' ou nao definido
  }

  function clearPending() {
    pendingSequence = null;
    if (pendingTimer) {
      clearTimeout(pendingTimer);
      pendingTimer = null;
    }
  }

  function onKeyDown(ev) {
    // Procura match em qualquer binding
    for (const binding of bindings.values()) {
      const combo = binding.combo;
      if (combo.length === 1) {
        if (keyMatches(combo[0], ev) && evaluateWhen(binding.when, ev)) {
          if (binding.preventDefault !== false) ev.preventDefault();
          clearPending();
          try { binding.handler(ev); } catch (err) { console.error('[EasyKeys]', binding.id, err); }
          return;
        }
      }
    }

    // Sequence: primeiro tecla
    if (!pendingSequence) {
      for (const binding of bindings.values()) {
        if (binding.combo.length > 1 && keyMatches(binding.combo[0], ev) && evaluateWhen(binding.when, ev)) {
          pendingSequence = { firstKey: ev.key.toLowerCase(), at: Date.now() };
          if (pendingTimer) clearTimeout(pendingTimer);
          pendingTimer = setTimeout(clearPending, SEQUENCE_TIMEOUT_MS);
          ev.preventDefault();
          return;
        }
      }
    } else {
      // Sequence: segunda tecla
      for (const binding of bindings.values()) {
        if (binding.combo.length > 1
            && (binding.combo[0].key === pendingSequence.firstKey)
            && keyMatches(binding.combo[1], ev)
            && evaluateWhen(binding.when, ev)) {
          ev.preventDefault();
          clearPending();
          try { binding.handler(ev); } catch (err) { console.error('[EasyKeys]', binding.id, err); }
          return;
        }
      }
      // Nao matchou, descarta sequence
      clearPending();
    }
  }

  function register(b) {
    if (!b || !b.id || !b.keys || typeof b.handler !== 'function') {
      throw new Error('[EasyKeys.register] requer { id, keys, handler }');
    }
    const combo = normalizeCombo(b.keys);
    bindings.set(b.id, {
      id: b.id,
      keys: b.keys,
      combo,
      description: b.description || '',
      group: b.group || 'global',
      when: b.when || 'global',
      handler: b.handler,
      preventDefault: b.preventDefault,
    });
    return () => bindings.delete(b.id);
  }

  function unregister(id) { return bindings.delete(id); }

  function list() {
    return Array.from(bindings.values()).map(b => ({
      id: b.id,
      keys: b.keys,
      description: b.description,
      group: b.group,
    }));
  }

  function isMacPlatform() { return isMac; }

  document.addEventListener('keydown', onKeyDown, { capture: true });

  global.EasyKeys = {
    register,
    unregister,
    list,
    isMac: isMacPlatform,
    __initialized: true,
  };
})(window);
