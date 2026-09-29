# ADR-0053 — CSRF do storefront: POST com cookie só da mesma origem

- Status: Aceito
- Data: 2026-09-29
- Relacionados: ADR-0012 (sessão do storefront por cookie), ADR-0052 (CodeQL CSRF na Api); issue #1088

## Contexto

A Api autentica quase tudo por JWT bearer. A exceção são os cookies `__Host-cdb_*` do
storefront: `__Host-cdb_session`, a sessão do cliente emitida no `validar-otp`, e
`__Host-cdb_aval_*`, emitido pelo link de avaliação. O navegador anexa esses cookies em qualquer
request para o host, inclusive quando outro site dispara a request.

Defesas que já existiam, sem validação anti-CSRF explícita:

- `SameSite=Lax`, que barra POST cross-site mas não same-site (subdomínio irmão comprometido);
- corpo JSON obrigatório, que força preflight;
- CORS sem `AllowCredentials`.

A proteção dependia de detalhes que ninguém tinha escolhido como defesa.

O front (`casa-da-baba`) é same-origin: o Caddy serve o storefront e faz proxy de `/api/*` no
mesmo host, e o build de produção usa `VITE_API_BASE_URL` vazio. Web, Admin e Capacitor chamam
a Api por bearer, de outra origem, inclusive em `/api/storefront/pedidos`.

## Decisão

O `ProtecaoCsrfCookieMiddleware` roda logo depois do `UseCors` e antes da autenticação. Ele só
atua em requests que alteram estado (POST, PUT, PATCH, DELETE) e carregam um cookie `__Host-cdb_*`.
Essas requests precisam provar a mesma origem:

1. Com Fetch Metadata, `Sec-Fetch-Site` precisa ser `same-origin` ou `none`. `same-site` e
   `cross-site` recebem 403.
2. Sem Fetch Metadata, o `Origin`, quando vem, precisa ter a mesma autoridade do `Host`. `null`
   ou outra origem recebem 403.
3. Sem nenhum dos dois headers, a request segue. Navegador sempre manda `Origin` em POST, então
   a ausência dos dois indica cliente que não anexa cookie sozinho.

A decisão depende da presença do cookie, e não do caminho. Assim o middleware cobre sozinho
qualquer rota nova com cookie e não trava as chamadas bearer cross-origin do Web e do Admin.

Guardas no gate: `ProtecaoCsrfCookieMiddlewareTests` (regras) e `ProtecaoCsrfCookiePipelineTests`
(middleware ligado antes da autenticação).

## Alternativas descartadas

- **Antiforgery do ASP.NET (`RequireAntiforgeryToken` + `UseAntiforgery`).** O CodeQL reconhece,
  mas exige que o front busque e reenvie o token. Isso muda outro repositório e quebra o checkout
  entre os dois deploys. Fetch Metadata é a defesa primária recomendada pela OWASP e o navegador
  já envia os headers.
- **Header customizado obrigatório.** Também exige mudar o front e protege menos que Fetch
  Metadata contra same-site.
- **`SameSite=Strict`.** O link de avaliação chega pelo WhatsApp (navegação cross-site) e perderia
  o cookie na primeira abertura.
- **Filtro por caminho (`/api/storefront`).** Barraria a aprovação de pedido que o Web faz por
  bearer.

## Consequências

- Um front do storefront em outra origem (por exemplo `api.` separado de `loja.`) passa a
  receber 403. Hoje esse caso já não funcionaria, porque o CORS não permite credenciais. Se
  surgir, esta decisão precisa ser revista junto com o CORS.
- O CodeQL não reconhece esta defesa. Os alertas `cs/web/missing-token-validation` dos
  controllers com cookie são dispensados citando este ADR, e cada POST novo nesses arquivos
  continua gerando alerta para triagem.
