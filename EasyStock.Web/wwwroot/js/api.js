/* EasyStock — fetch wrapper centralizado.
   - Anexa CSRF token (de <meta name="csrf-token">) em POST/PUT/PATCH/DELETE.
   - Trata 401 (sessão expirada) com redirect para /auth/login.
   - 5xx mostra toast "ops, tente de novo" sem mascarar.
   - Loga erros em console.error em vez de catch{} silencioso.

   Uso:
     const data = await window.api.get('/notificacoes/resumo');
     await window.api.post('/lotes/criar', { sku: 'X', qtd: 10 });
*/
(function () {
  function csrf() {
    const m = document.querySelector('meta[name="csrf-token"]');
    return m ? m.getAttribute('content') : null;
  }

  async function send(method, url, body) {
    const opts = {
      method,
      credentials: 'same-origin',
      headers: { 'Accept': 'application/json' },
    };
    if (body !== undefined) {
      opts.headers['Content-Type'] = 'application/json';
      opts.body = JSON.stringify(body);
    }
    const token = csrf();
    if (token && method !== 'GET') opts.headers['RequestVerificationToken'] = token;

    let res;
    try {
      // Delega ao esFetch (issue 806): injeta X-Requested-With e trata 401,
      // X-EasyStok-Auth: no-store (selecionar loja) e 402 de assinatura com os
      // MESMOS redirects do resto do app — antes este wrapper divergia do contrato.
      const doFetch = window.esFetch || fetch;
      res = await doFetch(url, opts);
    } catch (netErr) {
      console.error('[api]', method, url, 'network error:', netErr);
      if (window.showToast) window.showToast('Sem conexão. Verifique sua rede.', 'error');
      throw netErr;
    }

    if (!res.ok) {
      let msg = 'Erro ' + res.status;
      try {
        const ct = res.headers.get('content-type') || '';
        if (ct.includes('application/json')) {
          const data = await res.json();
          msg = data.message || data.title || msg;
        }
      } catch (_) { /* ignora parse error, usa msg padrão */ }
      console.error('[api]', method, url, res.status, msg);
      if (res.status >= 500 && window.showToast) window.showToast('Erro no servidor: ' + msg, 'error');
      throw new Error(msg);
    }

    if (res.status === 204) return null;
    const ct = res.headers.get('content-type') || '';
    if (ct.includes('application/json')) return res.json();
    return res.text();
  }

  window.api = {
    get:    (url)       => send('GET',    url),
    post:   (url, body) => send('POST',   url, body || {}),
    put:    (url, body) => send('PUT',    url, body || {}),
    patch:  (url, body) => send('PATCH',  url, body || {}),
    delete: (url)       => send('DELETE', url),
  };
})();
