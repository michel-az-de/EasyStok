/* EasyStock — esFetch: wrapper único de chamadas AJAX da Web (BUG-65 / #452).

   Problema: chamadas fetch() a endpoints protegidos recebiam 302 -> página de
   login HTML quando a sessão expirava; o fetch seguia o redirect e r.json()
   quebrava ("Unexpected token <"). O servidor agora responde 401/403/409 a quem
   manda o sinal X-Requested-With; este wrapper injeta esse sinal e trata os 2
   casos de redirect no cliente.

   Contrato:
   - Injeta 'X-Requested-With: fetch' (preserva o que o caller já tiver).
   - 401 (sessão expirada)            -> navega p/ /auth/login e NUNCA resolve.
   - header X-EasyStok-Auth: no-store  -> navega p/ /auth/selecionar-loja e NUNCA resolve.
     "Nunca resolve" = a continuation do caller (r.json(), catch, set de estado)
     não roda durante o redirect, evitando toast/erro espúrio.
   - Qualquer outra resposta (2xx, 400/422, 409 de negócio, 5xx) passa CRUA: o
     caller segue com r.ok / r.json() como antes.

   Uso: troque `fetch(url, opts)` por `esFetch(url, opts)`. Preserva
   method/body/signal/credentials/headers. NÃO usar em páginas anônimas nem em
   endpoints de outra origem/auth (ex.: /api/* da API).
*/
(function () {
  // Uma única Promise que nunca resolve — interrompe a continuation do caller
  // enquanto o navegador navega para o login/seleção de loja.
  var NEVER = new Promise(function () {});

  window.esFetch = async function (url, options) {
    options = options || {};
    var headers = new Headers(options.headers || {});
    if (!headers.has('X-Requested-With')) headers.set('X-Requested-With', 'fetch');
    var opts = Object.assign({ credentials: 'same-origin' }, options);
    opts.headers = headers;

    var res = await fetch(url, opts);

    if (res.status === 401) {
      var back = encodeURIComponent(window.location.pathname + window.location.search);
      window.location.href = '/auth/login?returnUrl=' + back + '&sessionExpired=1';
      return NEVER;
    }
    if (res.headers.get('X-EasyStok-Auth') === 'no-store') {
      window.location.href = '/auth/selecionar-loja';
      return NEVER;
    }
    return res;
  };
})();
