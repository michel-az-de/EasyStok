/* EasyStock — Notification polling (extraído de _Layout.cshtml).
   - Pollar /notificacoes/resumo a cada 45s.
   - Atualiza badge, balança o sino quando chegam novas, toast em criticas.
   - Para de pollar após 3 falhas consecutivas (exponential pause).
*/
(function () {
  const INTERVAL = 45000;
  let lastCount = -1;
  let lastCritical = 0;
  let consecutiveFailures = 0;
  let timerId = null;

  function updateBadge(id, count) {
    const el = document.getElementById(id);
    if (!el) return;
    if (count > 0) {
      el.textContent = count > 99 ? '99+' : String(count);
      el.classList.remove('hidden');
    } else {
      el.classList.add('hidden');
    }
  }

  function shakeBell() {
    const bell = document.getElementById('notif-bell-icon');
    if (!bell) return;
    bell.classList.remove('bell-shake');
    void bell.offsetWidth; // force reflow
    bell.classList.add('bell-shake');
    setTimeout(() => bell.classList.remove('bell-shake'), 800);
  }

  async function poll() {
    if (document.hidden) return;
    try {
      const r = await fetch('/notificacoes/resumo', { credentials: 'same-origin' });
      if (!r.ok) {
        consecutiveFailures++;
        if (consecutiveFailures >= 3) {
          console.warn('[notifications] 3 falhas consecutivas, pausando polling.');
          stopPolling();
        }
        return;
      }
      consecutiveFailures = 0;
      const data = await r.json();
      const count    = data.totalNaoLidas ?? 0;
      const critical = data.criticas      ?? 0;

      updateBadge('topbar-notif-badge', count);
      window.__notifResumo = { data, ts: Date.now() };

      if (lastCount === -1) {
        lastCount = count;
        lastCritical = critical;
        return;
      }

      if (count > lastCount) {
        shakeBell();
        const delta = count - lastCount;
        if (critical > lastCritical) {
          const dc = critical - lastCritical;
          window.showToast && window.showToast(
            dc + ' nova' + (dc > 1 ? 's' : '') + ' notificação' + (dc > 1 ? 'ões' : '') + ' crítica' + (dc > 1 ? 's' : ''),
            'error'
          );
        } else {
          window.showToast && window.showToast(
            delta + ' nova' + (delta > 1 ? 's' : '') + ' notificação' + (delta > 1 ? 'ões' : ''),
            'info'
          );
        }
      }

      lastCount = count;
      lastCritical = critical;
    } catch (e) {
      consecutiveFailures++;
      console.error('[notifications] poll falhou:', e);
      if (consecutiveFailures >= 3) {
        console.warn('[notifications] 3 falhas consecutivas, pausando polling.');
        stopPolling();
      }
    }
  }

  function startPolling() { if (!timerId) timerId = setInterval(poll, INTERVAL); }
  function stopPolling()  { clearInterval(timerId); timerId = null; }

  document.addEventListener('visibilitychange', () => {
    if (document.hidden) stopPolling();
    else { poll(); startPolling(); }
  });

  document.addEventListener('DOMContentLoaded', () => { poll(); startPolling(); });
})();
