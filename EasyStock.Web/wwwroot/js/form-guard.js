/* form-guard.js — guarda contra perda de alterações + autosave em localStorage.
 *
 * Uso:
 *   <form data-form-guard data-autosave-key="produto:novo">
 *     <input name="nome" />
 *     ...
 *   </form>
 *
 *   - data-form-guard       → bloqueia close/refresh se houver mudanças não-salvas.
 *   - data-autosave-key="X" → salva inputs em localStorage sob a chave X
 *                              (debounce 600ms) e restaura no load.
 *   - data-autosave-clear   → limpa autosave ao submit bem-sucedido.
 */
(function () {
    'use strict';

    const STORAGE_PREFIX = 'easystock-autosave:';
    const DEBOUNCE_MS = 600;

    function serialize(form) {
        const data = {};
        for (const el of form.elements) {
            if (!el.name || el.type === 'password' || el.type === 'file') continue;
            if (el.type === 'checkbox' || el.type === 'radio') {
                if (el.checked) data[el.name] = el.value;
            } else {
                data[el.name] = el.value;
            }
        }
        return data;
    }

    function restore(form, data) {
        for (const [name, value] of Object.entries(data || {})) {
            const els = form.querySelectorAll('[name="' + CSS.escape(name) + '"]');
            for (const el of els) {
                if (el.type === 'checkbox' || el.type === 'radio') {
                    if (el.value === value) el.checked = true;
                } else if (!el.value) {
                    el.value = value;
                }
            }
        }
    }

    function attach(form) {
        const guarded = form.hasAttribute('data-form-guard');
        const autosaveKey = form.dataset.autosaveKey
            ? STORAGE_PREFIX + form.dataset.autosaveKey
            : null;

        // Restore autosave first (without marking dirty).
        if (autosaveKey) {
            try {
                const raw = localStorage.getItem(autosaveKey);
                if (raw) restore(form, JSON.parse(raw));
            } catch (_) { /* malformed JSON */ }
        }

        const initial = JSON.stringify(serialize(form));
        let dirty = false;
        let saveTimer = null;

        const onChange = () => {
            const cur = JSON.stringify(serialize(form));
            dirty = cur !== initial;
            form.dataset.dirty = dirty ? 'true' : 'false';
            if (autosaveKey) {
                clearTimeout(saveTimer);
                saveTimer = setTimeout(() => {
                    try { localStorage.setItem(autosaveKey, cur); } catch (_) { /* quota */ }
                }, DEBOUNCE_MS);
            }
        };
        form.addEventListener('input', onChange, true);
        form.addEventListener('change', onChange, true);

        if (guarded) {
            window.addEventListener('beforeunload', (e) => {
                if (!dirty || form.dataset.submitting === 'true') return;
                e.preventDefault();
                e.returnValue = '';
            });
        }

        form.addEventListener('submit', () => {
            form.dataset.submitting = 'true';
            if (autosaveKey && form.hasAttribute('data-autosave-clear')) {
                try { localStorage.removeItem(autosaveKey); } catch (_) { /* */ }
            }
        });
    }

    document.addEventListener('DOMContentLoaded', () => {
        document.querySelectorAll('form[data-form-guard], form[data-autosave-key]').forEach(attach);
    });

    window.EasyFormGuard = { attach };
})();
