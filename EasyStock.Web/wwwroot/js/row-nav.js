/**
 * row-nav.js — navegacao de linha da tabela respeitando middle-click,
 * Cmd+click e Ctrl+click. Substitui o anti-pattern
 *   <tr onclick="location.href='...'">
 * que quebrava "abrir em nova aba" e Cmd+click.
 *
 * Uso na view: <tr data-row-href="/clientes/123" class="cursor-pointer">
 *   ...
 * </tr>
 *
 * Comportamento:
 *   - Click esquerdo na linha (sem modificadores)        -> navega
 *   - Cmd/Ctrl/Shift+click ou middle-click               -> nova aba
 *   - Click dentro de <a>, <button>, <input>, <label>    -> ignorado (controle inline)
 *   - Enter quando a linha esta focada                   -> navega
 */
(function () {
    'use strict';

    function isInteractive(el) {
        return el && el.closest && el.closest('a, button, input, select, textarea, label, [role="button"]');
    }

    function navigate(href, newTab) {
        if (newTab) window.open(href, '_blank', 'noopener');
        else window.location.href = href;
    }

    function bindRow(tr) {
        if (tr.dataset._rowNavBound === '1') return;
        tr.dataset._rowNavBound = '1';

        var href = tr.dataset.rowHref;
        if (!href) return;

        // Acessibilidade: ja que a row e clicavel, vira focavel via tab
        // e tem role="link" pra screen readers anunciarem como link.
        if (!tr.hasAttribute('tabindex')) tr.setAttribute('tabindex', '0');
        if (!tr.hasAttribute('role')) tr.setAttribute('role', 'link');

        tr.addEventListener('click', function (e) {
            if (e.button !== 0) return;
            if (isInteractive(e.target)) return;
            var newTab = e.metaKey || e.ctrlKey || e.shiftKey;
            navigate(href, newTab);
        });

        tr.addEventListener('auxclick', function (e) {
            // Middle-click (botao 1) sempre abre em nova aba.
            if (e.button === 1 && !isInteractive(e.target)) {
                e.preventDefault();
                navigate(href, true);
            }
        });

        tr.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' && !isInteractive(e.target)) {
                e.preventDefault();
                navigate(href, false);
            }
        });
    }

    function init() {
        document.querySelectorAll('tr[data-row-href]').forEach(bindRow);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
    // Para conteudo carregado dinamicamente (modais, tabs)
    window.addEventListener('pageshow', init);
})();
