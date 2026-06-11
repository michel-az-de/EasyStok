/* EasyStok — menu-badges.js (ADR-0032, fatia 2).

   Pollar GET /menu/resumo (60s, alinhado ao cache do BFF) e refletir os contadores
   nos badges do menu lateral. Espelha o padrao de notifications.js:
   - pausa quando a aba esta oculta; busca imediata ao voltar a ficar visivel;
   - para apos 3 falhas consecutivas;
   - so inicia se o sidebar existir (login e paginas sem shell nao tem).

   Contrato de DOM (emitido pela fatia 6 — por isso este script ainda NAO e
   carregado no _Layout):
   - badge de item:  [data-badge="pedidos-abertos|produtos-criticos|lotes-vencidos|dashboard-total"]
   - soma de grupo:  [data-group-badge] dentro de um [data-group] (recalculada dos filhos)
   Um item favoritado aparece 2x no DOM -> querySelectorAll atualiza TODAS as instancias
   (nunca getElementById). */
(function () {
  var INTERVAL = 60000;
  var timerId = null;
  var failures = 0;

  function sidebarPresent() {
    return !!document.querySelector('[data-es-sidebar]');
  }

  function setBadge(el, n) {
    el.textContent = n > 99 ? '99+' : String(n);
    el.classList.toggle('is-zero', n <= 0);
    el.setAttribute('aria-hidden', n <= 0 ? 'true' : 'false');
  }

  function apply(data) {
    var map = {
      'pedidos-abertos': data.pedidos || 0,
      'produtos-criticos': data.criticos || 0,
      'lotes-vencidos': data.vencidos || 0,
      'dashboard-total': data.dashboard || 0
    };

    Object.keys(map).forEach(function (key) {
      document.querySelectorAll('[data-badge="' + key + '"]').forEach(function (el) {
        setBadge(el, map[key]);
      });
    });

    // Soma do grupo = soma dos badges de item dentro do grupo (exclui o do Dashboard,
    // que nao vive em grupo). Recalculada no cliente p/ bater com os itens.
    document.querySelectorAll('[data-group]').forEach(function (group) {
      var sum = 0;
      group.querySelectorAll('[data-badge]').forEach(function (b) {
        var key = b.getAttribute('data-badge');
        if (key !== 'dashboard-total') sum += (map[key] || 0);
      });
      var gb = group.querySelector('[data-group-badge]');
      if (gb) setBadge(gb, sum);
    });
  }

  async function poll() {
    if (document.hidden) return;
    try {
      var r = await esFetch('/menu/resumo', { credentials: 'same-origin' });
      if (!r.ok) throw new Error('HTTP ' + r.status);
      var data = await r.json();
      failures = 0;
      window.__menuResumo = { data: data, ts: Date.now() };
      apply(data);
    } catch (e) {
      failures++;
      if (failures >= 3) { console.warn('[menu-badges] 3 falhas consecutivas, pausando.'); stop(); }
    }
  }

  function start() { if (!timerId) { timerId = setInterval(poll, INTERVAL); poll(); } }
  function stop() { if (timerId) { clearInterval(timerId); timerId = null; } }

  function init() {
    if (!sidebarPresent()) return;
    start();
    document.addEventListener('visibilitychange', function () {
      if (!document.hidden) poll();
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
