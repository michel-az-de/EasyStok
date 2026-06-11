/* EasyStok — menu-sidebar.js (ADR-0032, fatia 7a): accordion exclusivo + pin/unpin.
   Estrutura renderizada pelo <es-sidebar> (server). Rail e reorder = fatias seguintes.
   Sem framework: delegacao de eventos no root [data-es-sidebar]. */
(function () {
  var root = document.querySelector('[data-es-sidebar]');
  if (!root) return;

  var MAX_FAV = 20;
  var fine = !!(window.matchMedia && window.matchMedia('(pointer: fine)').matches); // desktop -> DnD

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
      // reorder (fatia 7c): DnD no desktop + botao ⋮ (menu de contexto, a11y/touch)
      clone.setAttribute('draggable', fine ? 'true' : 'false');
      var more = document.createElement('button');
      more.type = 'button';
      more.className = 'es-more';
      more.setAttribute('data-more', k);
      more.setAttribute('aria-label', 'Mover ou remover de Meu dia');
      more.innerHTML = '<span aria-hidden="true">⋮</span>';
      clone.appendChild(more);
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

  // ─── Reorder dos favoritos (fatia 7c): DnD desktop + menu de contexto ───
  var live = document.createElement('span');
  live.setAttribute('aria-live', 'polite');
  live.style.cssText = 'position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0,0,0,0);white-space:nowrap;';
  root.appendChild(live);

  function favOrder() {
    return Array.prototype.map.call(
      root.querySelectorAll('[data-meu-dia] .es-ni-row[data-menu-key]'),
      function (r) { return r.getAttribute('data-menu-key'); });
  }
  function announce(key, order) {
    var lbl = root.querySelector('[data-meu-dia] .es-ni-row[data-menu-key="' + cssEsc(key) + '"] .es-ni-lbl');
    var nome = lbl ? lbl.textContent : key;
    live.textContent = nome + ', posicao ' + (order.indexOf(key) + 1) + ' de ' + order.length;
  }
  function move(key, dir) {
    var order = favOrder();
    var i = order.indexOf(key);
    if (i < 0) return;
    var j = i + dir;
    if (j < 0 || j >= order.length) return;
    var before = order.slice();
    order.splice(i, 1);
    order.splice(j, 0, key);
    applyFav(order);
    announce(key, order);
    save(order, before);
  }
  function unpin(key) {
    var before = favOrder();
    var next = before.filter(function (k) { return k !== key; });
    applyFav(next);
    save(next, before);
  }

  // DnD (desktop, pointer:fine)
  var dragKey = null;
  root.addEventListener('dragstart', function (e) {
    var row = e.target.closest ? e.target.closest('[data-meu-dia] .es-ni-row[data-menu-key]') : null;
    if (!row) return;
    dragKey = row.getAttribute('data-menu-key');
    if (e.dataTransfer) { e.dataTransfer.effectAllowed = 'move'; try { e.dataTransfer.setData('text/plain', dragKey); } catch (x) { } }
  });
  root.addEventListener('dragover', function (e) {
    if (dragKey && e.target.closest && e.target.closest('[data-meu-dia] .es-ni-row[data-menu-key]')) e.preventDefault();
  });
  root.addEventListener('drop', function (e) {
    if (!dragKey) return;
    var row = e.target.closest ? e.target.closest('[data-meu-dia] .es-ni-row[data-menu-key]') : null;
    var key = dragKey; dragKey = null;
    if (!row) return;
    e.preventDefault();
    var target = row.getAttribute('data-menu-key');
    if (target === key) return;
    var order = favOrder();
    var before = order.slice();
    order.splice(order.indexOf(key), 1);
    order.splice(order.indexOf(target), 0, key);
    applyFav(order);
    announce(key, order);
    save(order, before);
  });

  // Menu de contexto (botao ⋮) — alternativa a11y/touch
  var ctx = document.createElement('div');
  ctx.className = 'es-ctx';
  ctx.setAttribute('role', 'menu');
  ctx.hidden = true;
  ctx.innerHTML =
    '<button type="button" role="menuitem" data-act="up">Mover para cima</button>' +
    '<button type="button" role="menuitem" data-act="down">Mover para baixo</button>' +
    '<button type="button" role="menuitem" data-act="remove">Remover de Meu dia</button>';
  document.body.appendChild(ctx);
  var ctxKey = null;
  function openCtx(btn, key) {
    ctxKey = key;
    var r = btn.getBoundingClientRect();
    ctx.hidden = false;
    ctx.style.top = (r.bottom + 4) + 'px';
    ctx.style.left = Math.max(8, Math.min(r.left, window.innerWidth - 200)) + 'px';
    var first = ctx.querySelector('button');
    if (first) first.focus();
  }
  function closeCtx() { ctx.hidden = true; ctxKey = null; }
  root.addEventListener('click', function (e) {
    var b = e.target.closest ? e.target.closest('[data-more]') : null;
    if (!b) return;
    e.preventDefault();
    e.stopPropagation();
    if (ctx.hidden) openCtx(b, b.getAttribute('data-more')); else closeCtx();
  });
  ctx.addEventListener('click', function (e) {
    var b = e.target.closest ? e.target.closest('button[data-act]') : null;
    if (!b || !ctxKey) return;
    var act = b.getAttribute('data-act');
    var key = ctxKey;
    closeCtx();
    if (act === 'up') move(key, -1);
    else if (act === 'down') move(key, 1);
    else if (act === 'remove') unpin(key);
  });
  document.addEventListener('click', function (e) {
    if (!ctx.hidden && !ctx.contains(e.target) && !(e.target.closest && e.target.closest('[data-more]'))) closeCtx();
  });
  document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && !ctx.hidden) closeCtx(); });

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
