/* shortcuts.js — registra atalhos globais via EasyKeys (Fase 5).
 *
 *   ?  → abre cheatsheet
 *   g p / g e / g c / g d / g l / g u / g f / g v / g s
 *      → navegacao por sequence
 *
 * Os atalhos nao disparam quando o foco esta em input/textarea/select/contenteditable.
 */
(function () {
    'use strict';

    function ready(fn) {
        if (document.readyState === 'loading')
            document.addEventListener('DOMContentLoaded', fn, { once: true });
        else
            fn();
    }

    ready(function () {
        if (!window.EasyKeys) return;

        const go = (path) => () => { window.location.href = path; };

        // Sequencias g + letra (somente quando foco nao esta em input)
        const seqs = [
            ['go.dashboard',     'g d', 'Ir para Dashboard',        '/dashboard'],
            ['go.produtos',      'g p', 'Ir para Produtos',         '/produtos'],
            ['go.estoque',       'g e', 'Ir para Estoque',          '/estoque'],
            ['go.caixa',         'g c', 'Ir para Caixa',            '/caixa'],
            ['go.lojas',         'g l', 'Ir para Configuracoes/Lojas', '/configuracoes'],
            ['go.usuarios',      'g u', 'Ir para Usuarios',         '/usuarios'],
            ['go.fornecedores',  'g f', 'Ir para Fornecedores',     '/fornecedores'],
            ['go.vendas',        'g v', 'Ir para Pedidos',          '/pedidos'],
            ['go.saidas',        'g s', 'Ir para Saidas',           '/saidas/historico'],
        ];

        for (const [id, keys, description, path] of seqs) {
            window.EasyKeys.register({
                id, keys, description, when: 'no-input',
                group: 'Navegacao',
                handler: go(path)
            });
        }

        // Cheatsheet — '?' (com Shift, sempre vem com o key '?')
        window.EasyKeys.register({
            id: 'app.cheatsheet',
            keys: '?',
            description: 'Mostrar atalhos de teclado',
            when: 'no-input',
            group: 'Geral',
            handler: function (ev) {
                ev.preventDefault();
                window.dispatchEvent(new CustomEvent('cheatsheet:toggle'));
            }
        });

        // Esc fecha cheatsheet
        window.EasyKeys.register({
            id: 'app.cheatsheet.close',
            keys: 'esc',
            description: 'Fechar cheatsheet',
            when: function () {
                const m = document.getElementById('es-cheatsheet-modal');
                return m && m.dataset.open === 'true';
            },
            group: 'Geral',
            handler: function () {
                window.dispatchEvent(new CustomEvent('cheatsheet:close'));
            }
        });
    });
})();
