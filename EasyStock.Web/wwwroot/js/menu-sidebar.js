/* EasyStok — menu-sidebar.js (ADR-0032, fatia 7a): accordion exclusivo + pin/unpin.
   Estrutura renderizada pelo <es-sidebar> (server). Rail e reorder = fatias seguintes.
   Sem framework: delegacao de eventos no root [data-es-sidebar]. */
(function () {
  var root = document.querySelector('[data-es-sidebar]');
  if (!root) return;

  var MAX_FAV = 20;

  function token() {
    var m = document.querySelector('meta[name="csrf-token"]');
    return m ? m.getAttribute('content') : '';
  }
  function cssEsc(s) { return (window.CSS && CSS.escape) ? CSS.escape(s) : String(s).replace(/"/g, '\\"'); }

  // ─── Accordion exclusivo: abrir um grupo fecha os demais (rota abre no load) ───
  function syncExpanded() {
    root.querySelectorAll('details.es-group').forEach(function (d) {
      var s = d.querySelector('summary');
      if (s) s.setAttribute('aria-expanded', d.open ? 'true' : 'false');
    });
  }
  root.querySelectorAll('details.es-group').forEach(function (d) {
    d.addEventListener('toggle', function () {
      if (d.open) {
        root.querySelectorAll('details.es-group[open]').forEach(function (o) {
          if (o !== d) o.open = false;
        });
      }
      syncExpanded();
    });
  });
  syncExpanded();

  // ─── Pin / unpin (otimista -> PUT -> reverte em erro) ───
  function favKeys() {
    var sec = root.querySelector('[data-meu-dia]');
    if (!sec) return [];
    return Array.prototype.map.call(
      sec.querySelectorAll('.es-ni-row[data-menu-key]'),
      function (r) { return r.getAttribute('data-menu-key'); });
  }

  function setStars(keys) {
    var set = {};
    keys.forEach(function (k) { set[k] = true; });
    root.querySelectorAll('.es-star[data-pin]').forEach(function (b) {
      var on = !!set[b.getAttribute('data-pin')];
      b.setAttribute('aria-pressed', on ? 'true' : 'false');
      b.setAttribute('aria-label', on ? 'Remover de Meu dia' : 'Fixar em Meu dia');
    });
  }

  // Linha canonica (a do grupo, que tem estrela) p/ clonar no Meu dia.
  function groupRow(key) {
    return root.querySelector('.es-group .es-ni-row[data-menu-key="' + cssEsc(key) + '"]');
  }

  function rebuildMeuDia(keys) {
    var sec = root.querySelector('[data-meu-dia]');

    if (!keys.length) { if (sec) sec.remove(); return; }

    if (!sec) {
      sec = document.createElement('div');
      sec.className = 'es-fav';
      sec.setAttribute('data-meu-dia', '');
      var lbl = document.createElement('div');
      lbl.className = 'es-nav-label';
      lbl.textContent = 'Meu dia';
      sec.appendChild(lbl);
      var dash = root.querySelector('#es-row-dashboard');
      if (dash && dash.nextSibling) root.insertBefore(sec, dash.nextSibling);
      else root.insertBefore(sec, root.firstChild);
    }

    sec.querySelectorAll('.es-ni-row').forEach(function (r) { r.remove(); });

    keys.forEach(function (k) {
      var src = groupRow(k);
      if (!src) return; // chave orfa (ex.: KDS off) — ignora
      var clone = src.cloneNode(true);
      clone.id = 'es-row-' + k + '-fav';
      var star = clone.querySelector('.es-star');
      if (star) { star.setAttribute('aria-pressed', 'true'); star.setAttribute('aria-label', 'Remover de Meu dia'); }
      sec.appendChild(clone);
    });
  }

  function applyFav(keys) { setStars(keys); rebuildMeuDia(keys); }

  function save(next, prev) {
    esFetch('/menu/favoritos', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token() },
      body: JSON.stringify({ favoritos: next })
    }).then(function (r) {
      if (!r.ok) throw new Error('HTTP ' + r.status);
      return r.json();
    }).then(function (data) {
      if (data && Array.isArray(data.favoritos)) applyFav(data.favoritos); // reconcilia normalizado
    }).catch(function () {
      applyFav(prev); // reverte
      if (window.showToast) window.showToast('Nao foi possivel salvar os favoritos.', 'error');
    });
  }

  root.addEventListener('click', function (e) {
    var btn = e.target && e.target.closest ? e.target.closest('.es-star') : null;
    if (!btn || !root.contains(btn)) return;
    e.preventDefault();
    e.stopPropagation();

    var key = btn.getAttribute('data-pin');
    var pressed = btn.getAttribute('aria-pressed') === 'true';
    var current = favKeys();
    var next;

    if (pressed) {
      next = current.filter(function (k) { return k !== key; });
    } else {
      if (current.length >= MAX_FAV) {
        if (window.showToast) window.showToast('Limite de ' + MAX_FAV + ' favoritos atingido.', 'warning');
        return;
      }
      next = current.concat([key]);
    }

    applyFav(next); // otimista
    save(next, current);
  });

  // ─── Rail (~64px) — preferencia de DISPOSITIVO (es:rail), sem scope ───
  function setRail(on) {
    document.documentElement.classList.toggle('es-rail', on);
    try { localStorage.setItem('es:rail', on ? '1' : '0'); } catch (e) { }
  }
  document.querySelectorAll('[data-rail-toggle]').forEach(function (b) {
    b.addEventListener('click', function () {
      setRail(!document.documentElement.classList.contains('es-rail'));
    });
  });
  // Em rail, clicar no header de um grupo expande o menu e abre o grupo (sem flyouts).
  root.querySelectorAll('details.es-group > summary').forEach(function (s) {
    s.addEventListener('click', function (e) {
      if (document.documentElement.classList.contains('es-rail')) {
        e.preventDefault();
        setRail(false);
        if (s.parentElement) s.parentElement.open = true;
      }
    });
  });
})();
