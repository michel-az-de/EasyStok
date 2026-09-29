# ADR-0052 — CSRF do CodeQL filtrado na Api bearer, mantido nos controllers com cookie

- Status: Aceito
- Data: 2026-09-29
- Relacionados: ADR-0012 (sessão do storefront por cookie); issues #1089 (esta) e #1088 (CSRF do storefront)

## Contexto

Em 29/09/2026 o master tinha 28 alertas `cs/web/missing-token-validation` abertos em 14
controllers da `EasyStock.Api`. CSRF depende de o navegador anexar a credencial sozinho, e isso
acontece com cookie, não com `Authorization: Bearer`. A pergunta, então, era se a Api aceita
cookie. Medido:

1. **A Api não é 100% bearer.** O esquema padrão é JwtBearer, mas 4 controllers do storefront
   leem ou emitem cookie direto: `Auth` (emite `__Host-cdb_session` no OTP), `Checkout`,
   `PedidosCliente` e `Avaliacao` (`__Host-cdb_aval_*`). A defesa deles hoje é `SameSite=Lax` com
   CORS sem `AllowCredentials`. Isso reduz o risco, mas não é validação anti-CSRF.
2. **O CodeQL não reconhece `[IgnoreAntiforgeryToken]`.** A query só aceita
   `ValidateAntiForgeryToken`, `AutoValidateAntiforgeryToken` global ou `RequireAntiforgeryToken`
   com `UseAntiforgery`. O alerta #518 continuou aberto com o atributo e foi dispensado à mão.
3. **A regra só dispara porque o `EasyStock.Web` usa `ValidateAntiForgeryToken`.** A query exige
   que a validação apareça em algum ponto da base analisada, e a solução inteira vira uma base só.

## Decisão

- O `codeql.yml` sobe o SARIF depois do `advanced-security/filter-sarif` (fixado por SHA). O
  filtro remove `cs/web/missing-token-validation` de `EasyStock.Api/**` e reinclui, arquivo a
  arquivo, os controllers da Api que usam cookie. Web e Admin não são filtrados.
- O `CodeQlCsrfFilterTests` (arquitetura, roda no gate) amarra a lista ao código. Todo arquivo da
  Api que usa `.Cookies` ou o esquema `ClienteSession` precisa estar reincluído, toda entrada
  reincluída precisa ainda usar cookie, e a única exclusão da regra é `EasyStock.Api/**`.
- Os alertas já abertos nos controllers bearer são dispensados como `false positive`, citando este
  ADR. Os dos controllers com cookie continuam abertos e vão para a #1088.

## Alternativas descartadas

- **`[IgnoreAntiforgeryToken]` numa base comum.** Não fecha nenhum alerta (item 2). Também não muda
  o runtime, porque a Api não liga validação antiforgery global. Além disso, só 12 dos 95
  controllers herdam `EasyStockControllerBase`.
- **`query-filters` no config do CodeQL.** Filtra por id de regra e não por caminho, então
  desligaria a regra também no Web e no Admin, que autenticam por cookie.
- **Dispensar os alertas um a um, sem filtro.** Cada controller bearer novo abriria alerta novo, e
  a triagem manual acaba treinando quem mantém o código a dispensar sem ler, inclusive no storefront.

## Consequências

- Um controller da Api que passar a usar cookie quebra o gate até entrar na lista, e a mudança
  aparece no diff do workflow.
- O teste reconhece cookie pelo texto (`.Cookies`, `ClienteSession`). Um helper fora de
  `EasyStock.Api` que leia cookie para um controller escaparia. Hoje não existe nenhum.
- Se a Api passar a autenticar por cookie de forma geral, este ADR deixa de valer e o filtro sai.
