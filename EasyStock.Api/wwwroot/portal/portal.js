/* Portal do Cliente — auth + API client.
 *
 * Token de acesso (JWT) em sessionStorage — perde ao fechar browser
 * (mais seguro contra XSS persistente).
 * Refresh token em localStorage — persiste entre sessões; usado quando
 * o JWT expira (recebe 401, troca por novo JWT, retenta).
 */

(function () {
  const TOKEN_KEY = 'es-portal-token';
  const REFRESH_KEY = 'es-portal-refresh';
  const USER_KEY = 'es-portal-user';

  // ── Storage helpers ─────────────────────────────────────────────
  function getToken() { return sessionStorage.getItem(TOKEN_KEY); }
  function setToken(t) { sessionStorage.setItem(TOKEN_KEY, t); }
  function clearToken() { sessionStorage.removeItem(TOKEN_KEY); }
  function getRefresh() { return localStorage.getItem(REFRESH_KEY); }
  function setRefresh(t) { localStorage.setItem(REFRESH_KEY, t); }
  function clearRefresh() { localStorage.removeItem(REFRESH_KEY); }
  function getUser() {
    try { return JSON.parse(sessionStorage.getItem(USER_KEY) || 'null'); } catch (e) { return null; }
  }
  function setUser(u) { sessionStorage.setItem(USER_KEY, JSON.stringify(u)); }

  function clearAll() {
    clearToken();
    clearRefresh();
    sessionStorage.removeItem(USER_KEY);
  }

  function logout() {
    clearAll();
    location.href = '/portal/login.html';
  }

  // ── Auth ───────────────────────────────────────────────────────
  // A API envelopa todas as respostas em { data: ..., meta: ... } via DataOk.
  // Login: data tem { token, refreshToken, expiresIn, usuario }.
  // Refresh: data tem { accessToken, refreshToken, expiresIn }.
  async function login(email, senha, empresaId) {
    const resp = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, senha, empresaId: empresaId || null })
    });
    if (!resp.ok) {
      const err = await resp.json().catch(() => ({}));
      const msg = (err.error && (err.error.detail || err.error.message))
        || err.message
        || 'Email ou senha invalidos.';
      throw new Error(msg);
    }
    const env = await resp.json();
    const data = env.data || env; // tolerante a ambos formatos
    setToken(data.token);
    setRefresh(data.refreshToken);
    setUser(data.usuario);
    return data;
  }

  async function refreshAccessToken() {
    const refresh = getRefresh();
    if (!refresh) return null;
    try {
      const resp = await fetch('/api/auth/refresh', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken: refresh })
      });
      if (!resp.ok) return null;
      const env = await resp.json();
      const data = env.data || env;
      // Refresh retorna 'accessToken' (nao 'token').
      const newToken = data.accessToken || data.token;
      if (newToken) setToken(newToken);
      if (data.refreshToken) setRefresh(data.refreshToken);
      return newToken;
    } catch (e) {
      return null;
    }
  }

  // ── API client com retry de 401 ────────────────────────────────
  async function apiFetch(path, options) {
    options = options || {};
    const headers = Object.assign({}, options.headers || {});
    const token = getToken();
    if (token) headers['Authorization'] = 'Bearer ' + token;
    if (!headers['Content-Type'] && options.body && !(options.body instanceof FormData)) {
      headers['Content-Type'] = 'application/json';
    }

    let resp = await fetch(path, Object.assign({}, options, { headers }));

    if (resp.status === 401) {
      // Tenta renovar 1x. Falhou? Logout.
      const newToken = await refreshAccessToken();
      if (!newToken) { logout(); throw new Error('Sessao expirada.'); }
      headers['Authorization'] = 'Bearer ' + newToken;
      resp = await fetch(path, Object.assign({}, options, { headers }));
      if (resp.status === 401) { logout(); throw new Error('Sessao expirada.'); }
    }

    return resp;
  }

  async function apiJson(path, options) {
    const resp = await apiFetch(path, options);
    if (!resp.ok) {
      const err = await resp.json().catch(() => ({}));
      const msg = (err.error && (err.error.detail || err.error.message)) || err.message || ('Erro ' + resp.status);
      const e = new Error(msg);
      e.status = resp.status;
      throw e;
    }
    return resp.json();
  }

  async function apiBlob(path, options) {
    const resp = await apiFetch(path, options);
    if (!resp.ok) {
      const e = new Error('Falha ao baixar arquivo (HTTP ' + resp.status + ').');
      e.status = resp.status;
      throw e;
    }
    return resp.blob();
  }

  // ── Helpers de UI ──────────────────────────────────────────────
  function fmtCurrency(v, moeda) {
    moeda = moeda || 'BRL';
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: moeda }).format(Number(v) || 0);
  }
  function fmtDate(s) {
    if (!s) return '—';
    const d = new Date(s);
    return d.toLocaleDateString('pt-BR');
  }
  function fmtDateTime(s) {
    if (!s) return '—';
    const d = new Date(s);
    return d.toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
  }

  function statusBadgeClass(status) {
    switch ((status || '').toLowerCase().replace(/\s/g, '')) {
      case 'rascunho': return 'badge-rascunho';
      case 'emitida': return 'badge-emitida';
      case 'parcialmentepaga': return 'badge-parcial';
      case 'paga': return 'badge-paga';
      case 'vencida': return 'badge-vencida';
      case 'cancelada': return 'badge-cancelada';
      default: return 'badge-rascunho';
    }
  }
  function statusLabel(s) {
    return s === 'ParcialmentePaga' ? 'Parcial' : s;
  }

  function escapeHtml(s) {
    if (s == null) return '';
    return String(s).replace(/[&<>"']/g, c =>
      ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])
    );
  }

  // ── Guard: se nao autenticado, redireciona pra login ───────────
  function requireAuth() {
    if (!getToken()) {
      location.href = '/portal/login.html';
      return false;
    }
    return true;
  }

  // ── Topbar render ──────────────────────────────────────────────
  function renderTopbar(elemId) {
    const u = getUser();
    const el = document.getElementById(elemId);
    if (!el) return;
    el.innerHTML = `
      <div class="brand">
        <svg class="brand-logo" viewBox="0 0 64 64" fill="none" xmlns="http://www.w3.org/2000/svg">
          <path d="M32 6 L56 18 L32 30 L8 18 Z" fill="#E85814"/>
          <path d="M8 18 L32 30 L32 58 L8 46 Z" fill="#0E2A6E"/>
          <path d="M56 18 L32 30 L32 58 L56 46 Z" fill="#06143A"/>
        </svg>
        <div>
          <span class="easy">Easy</span><span class="stok">Stok</span>
          <small>Faturas</small>
        </div>
      </div>
      <div class="user-info">
        ${u ? `<span class="name">${escapeHtml(u.nome || u.email || '')}</span>` : ''}
        <button type="button" class="btn-logout" onclick="EasyStokPortal.logout()">Sair</button>
      </div>
    `;
  }

  // Public API
  window.EasyStokPortal = {
    login, logout, refreshAccessToken,
    apiFetch, apiJson, apiBlob,
    getToken, getUser, requireAuth,
    renderTopbar,
    fmt: { currency: fmtCurrency, date: fmtDate, dateTime: fmtDateTime },
    ui: { statusBadgeClass, statusLabel, escapeHtml }
  };
})();
