/**
 * Barra de progresso de navegação no topo da página.
 *
 * Mostra um indicador fino quando o usuário clica em links internos ou
 * envia forms (busca, filtros, criação). Reduz a percepção de "página
 * em branco" durante navegação server-side e cold starts do Render.
 *
 * Estratégia: trickle artificial até ~85% durante navegação, depois
 * completa pra 100% quando a nova página termina de carregar.
 *
 * Não roda em:
 *   - Links externos (target=_blank, hosts diferentes)
 *   - Cliques com modificadores (Cmd/Ctrl/Shift/middle-click — usuário
 *     quer abrir em nova aba)
 *   - Anchors (#hash) e hrefs javascript:
 *   - Forms com data-noprogress (caso o caller queira opt-out)
 */
(function () {
    'use strict';

    var bar = null;
    var ticker = null;
    var progress = 0;
    var running = false;

    function ensureBar() {
        if (bar) return bar;
        bar = document.createElement('div');
        bar.className = 'nav-progress';
        bar.setAttribute('aria-hidden', 'true');
        var fill = document.createElement('div');
        fill.className = 'nav-progress__fill';
        bar.appendChild(fill);
        document.body.appendChild(bar);
        return bar;
    }

    function setProgress(pct) {
        ensureBar();
        progress = Math.max(0, Math.min(100, pct));
        var fill = bar.querySelector('.nav-progress__fill');
        if (fill) fill.style.width = progress + '%';
    }

    function show() {
        if (running) return;
        running = true;
        ensureBar();
        bar.classList.remove('nav-progress--done');
        bar.classList.add('nav-progress--active');
        setProgress(8);
        // Trickle artificial — sobe rápido até 30%, devagar até ~85%.
        var step = function () {
            if (!running) return;
            var jump = progress < 30 ? 6
                     : progress < 60 ? 3
                     : progress < 80 ? 1
                                     : 0.3;
            if (progress < 85) setProgress(progress + jump);
            ticker = setTimeout(step, 250);
        };
        step();
    }

    function done() {
        if (!running) return;
        running = false;
        if (ticker) { clearTimeout(ticker); ticker = null; }
        setProgress(100);
        // Pequeno delay pra animação de 100% ser percebida antes do fade.
        setTimeout(function () {
            if (bar) {
                bar.classList.add('nav-progress--done');
                bar.classList.remove('nav-progress--active');
            }
        }, 180);
        setTimeout(function () { setProgress(0); }, 500);
    }

    function isInternalLink(a) {
        if (!a || !a.href) return false;
        if (a.target && a.target !== '' && a.target !== '_self') return false;
        if (a.hasAttribute('download')) return false;
        var href = a.getAttribute('href');
        if (!href) return false;
        if (href.startsWith('#')) return false;
        if (href.startsWith('javascript:')) return false;
        if (href.startsWith('mailto:') || href.startsWith('tel:')) return false;
        try {
            var url = new URL(a.href, window.location.href);
            return url.origin === window.location.origin;
        } catch (_) {
            return false;
        }
    }

    // Defere show() ate o proximo tick pra que TODOS os handlers sincronos
    // do evento (capture + bubble, incluindo Alpine @submit.prevent /
    // @click.prevent) tenham rodado. Se algum chamou preventDefault, o
    // browser nao vai navegar e nao faz sentido mostrar a barra.
    function showIfNotPrevented(e) {
        setTimeout(function () {
            if (e.defaultPrevented) return;
            show();
        }, 0);
    }

    document.addEventListener('click', function (e) {
        // Modificadores → usuário quer nova aba/janela. Não interceptamos.
        if (e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
        if (e.button !== 0) return; // só botão primário
        var a = e.target.closest('a');
        if (!isInternalLink(a)) return;
        // Mesmo path (só hash) → não dispara navegação real.
        try {
            var dest = new URL(a.href, window.location.href);
            if (dest.pathname === window.location.pathname &&
                dest.search === window.location.search &&
                dest.hash !== window.location.hash) return;
        } catch (_) { /* segue */ }
        showIfNotPrevented(e);
    }, true);

    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (!form || form.tagName !== 'FORM') return;
        if (form.hasAttribute('data-noprogress')) return;
        showIfNotPrevented(e);
    }, true);

    // Reset duro da barra — usado quando a pagina e restaurada do bfcache
    // com estado visual carry-over do clique-que-saiu-dela.
    function resetBar() {
        if (ticker) { clearTimeout(ticker); ticker = null; }
        running = false;
        progress = 0;
        if (bar) {
            bar.classList.remove('nav-progress--active', 'nav-progress--done');
            var fill = bar.querySelector('.nav-progress__fill');
            if (fill) fill.style.width = '0%';
        }
    }

    // Página antiga ainda carregando → completa quando a nova chegar.
    window.addEventListener('pageshow', function (e) {
        // bfcache restore: a pagina volta com a barra travada em 85%.
        if (e.persisted) { resetBar(); return; }
        done();
    });
    window.addEventListener('DOMContentLoaded', function () { done(); });

    // Navegacao saindo: limpa estado antes de eventual entrada no bfcache.
    window.addEventListener('pagehide', function () { resetBar(); });
})();
