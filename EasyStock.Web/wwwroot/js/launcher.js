// Contagem crescente dos numeros do portal (ADR-0046).
//
// Progressive enhancement puro: o valor final ja esta no HTML renderizado pelo servidor.
// Se este script nao carregar, falhar ou o usuario pedir menos movimento, o numero
// simplesmente aparece pronto — nunca zerado, nunca vazio.
(function () {
    'use strict';

    var DURACAO_MS = 600;

    function animar(el) {
        var alvo = parseInt(el.textContent.trim(), 10);
        // So numeros inteiros; qualquer outra coisa (moeda, texto) fica como esta.
        if (!isFinite(alvo) || alvo <= 0) return;

        var inicio = null;

        function passo(agora) {
            // Zera so DENTRO do primeiro frame. requestAnimationFrame nao dispara em aba
            // de fundo (Ctrl+clique, restauracao de sessao): zerar antes deixaria o badge
            // preso em "0" ate o usuario recarregar. Numero errado e pior que sem animacao.
            if (inicio === null) inicio = agora;

            var t = Math.min((agora - inicio) / DURACAO_MS, 1);
            // easeOutCubic: rapido no comeco, assenta no fim.
            var eased = 1 - Math.pow(1 - t, 3);
            el.textContent = String(t < 1 ? Math.round(alvo * eased) : alvo);
            if (t < 1) requestAnimationFrame(passo);
        }

        requestAnimationFrame(passo);
    }

    function iniciar() {
        if (window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
        document.querySelectorAll('[data-countup]').forEach(animar);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', iniciar);
    } else {
        iniciar();
    }
})();
