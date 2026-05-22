# Sessao varredura de estabilizacao (testes + DI + Web/API/PWA) + Admin hardening

Data: 2026-05-21 23:45 (UTC)
Worktree: commits feitos no repo principal C:\easy\EasyStok (master direto, autorizado)
Identidade Git: felipe.azevedo@gmail.com / gh michel-az-de
Status final: completo (varredura) + 1 commit na master; 3 achados deferidos p/ decisao

## O que foi feito
- **Suite de testes (logica): 1544 passando, 0 falhas** — Domain 594, Application 620,
  Api.UnitTests 293, Web.UnitTests 21, ArchitectureTests 16. O `Exceptions_De_Domain`
  do flaky-tests.md ja passa (entrada stale). IntegrationTests usam Testcontainers e
  Docker NAO esta instalado nesta maquina — nao rodaram.
- **DI / lifetimes:** 0 captive dependencies nos 31 hosted services (cada construtor
  auditado — todos usam IServiceScopeFactory/IServiceProvider). Grafo de DI de PRODUCAO
  (Postgres) completo: todos os repositorios e servicos consumidos por use cases estao
  registrados. Provider de prod e explicito (`Database:Provider=PostgreSql`) — sem risco
  de fallback sqlite.
- **Tecnica de validacao de DI sem Docker:** rodar `dotnet run -- --migrate-only` em
  ASPNETCORE_ENVIRONMENT=Development liga `ValidateOnBuild`/`ValidateScopes` no
  `builder.Build()` (antes do branch migrate-only do PR #191) — valida o grafo inteiro e
  encerra. Pega captive deps e registros faltando de uma vez.
- **Admin hardening (trabalho dirty que estava no working tree):** revisado, validado
  (build 0 erros + ArchitectureTests 16/16) e **commitado na master: 94a18034**.
  SetErroSeguro (nao vaza erro tecnico), modais de confirmacao, loading states,
  validacao de input (broadcast, motivo suspensao/reativacao min 10 chars), e
  AdminApiClient lancando EMPTY_RESPONSE em corpo vazio. Verificado seguro: todos os
  endpoints admin chamados via PostAsync<T>/PatchAsync<T> retornam body (envelope DataOk);
  o unico NoContent (AdminReportsController:131) nao e consumido por metodo tipado.

## Achados deferidos (precisam DECISAO — nao commitados)

> ATUALIZACAO 2026-05-21: itens 2 e 3 CORRIGIDOS e commitados na master —
> db767627 fix(etiqueta): render.pdf -> impressao client-side;
> 2d9d2c12 fix(caixa): /api/empresa -> /api/configuracao-fiscal.
> Resta apenas o item 1 (SQLite), por decisao de nao mexer agora.

1. **SQLite dev-fallback incompleto:** `AddEasyStockSqliteInfrastructure` registra ~25
   repositorios A MENOS que o Postgres (FAQ, tickets, CAP/CAR, fiscal, fatura, etc.) — o
   app NAO sobe em SQLite (crasha em ValidateOnBuild). Dev-only (sqlite proibido em prod).
   Risco de "completar" cego: repos Postgres podem usar features incompativeis com SQLite
   (RLS, jsonb, advisory locks). Opcoes: completar+validar / remover o fallback / fail-fast
   com mensagem clara.
2. **`render.pdf` (etiquetas):** chamado por Web (Views/Lotes/Imprimir.cshtml:224) e PWA
   (wwwroot/pwa/etiqueta/imprimir.js:121), mas LotesController so tem
   `GET {id}/etiquetas/render` (JSON p/ render client-side, :130) — nao existe handler
   PDF. "Baixar PDF" da 404 nos dois. Opcoes: implementar PDF server / trocar p/ print
   client-side / remover o botao.
3. **`/api/empresa` (caixa NFC-e):** wwwroot/pwa/caixa/caixa.js:72 chama endpoint
   inexistente (EmpresaController e `api/empresas`, sem GET empresa-atual). Degrada p/
   placeholder ("(razao social)", CNPJ vazio) no display do emitente. Opcoes: adicionar
   endpoint empresa-atual / reusar dados de /api/configuracao-fiscal.

## Commits criados
- 94a18034: feat(admin): hardening de UX e tratamento de erro nas paginas admin (master, pushed)

## Pendencias
- Doc untracked no repo principal: docs/dev/sessoes/2026-05-17-1245-redeploy-fly-3-apps.md
  (decidir se commita — nao toquei, nao e meu).
- Handoff do merge das PRs: docs/dev/sessoes/2026-05-21-2318-review-merge-prs-191-192.md
  (esta no worktree brave-shannon-078b0e, nao commitado).

## Proxima acao recomendada
- Decidir os 3 achados acima (especialmente SQLite — ver tambem [[deploy-and-migrations-gotcha]]).
- `fly deploy` da API para ativar o gate de migrations do PR #191 (ainda nao deployado).

## Prontidao para deploy no Fly (pente-fino 2026-05-21)

3 apps Fly: **easystok** (API, Dockerfile, porta 8080), **easystok-web**
(Dockerfile.Web, porta 8081), **easystok-admin** (Dockerfile.cloudrun.admin,
porta 8080). Worker tem Dockerfile mas NAO e app Fly (pipeline roda in-process na API).

VERIFICADO OK:
- `dotnet publish -c Release`: API, Web (-p:SkipTailwind=true), Admin
  (-p:UseAppHost=false -p:SkipTailwind=true) e Worker — todos **0 erros**.
- Portas batem entre Dockerfile (ASPNETCORE_URLS/EXPOSE) e fly.toml (internal_port).
- Entrypoints: API usa scripts/docker/api-entrypoint.sh (exige
  ConnectionStrings__DefaultConnection; trata `--migrate-only`); Web/Admin usam
  `dotnet X.dll` direto, sem dependencia de DB.
- `/health/live` = liveness puro (Predicate `_ => false`) — 200 assim que o
  processo sobe, independe de DB/migrations. fly.toml da API checa /health/live.
- release_command `--migrate-only` coerente: fly.toml -> entrypoint $1 ->
  `dotnet EasyStock.Api.dll --migrate-only` -> Program.cs (apos builder.Build).
  Em Production ValidateOnBuild OFF + Database:Provider=PostgreSql -> NAO cai no
  problema sqlite/ValidateOnBuild visto em dev.
- ProjectReference x Dockerfile COPY: API referencia 7 projetos (+ Domain/Contracts
  transitivos), todos no COPY; Web e Admin sao standalone (zero ProjectReference).
- .dockerignore re-inclui scripts/docker/api-entrypoint.sh (necessario p/ API).

CHECKLIST PRE-DEPLOY (secrets no app `easystok`; tambem exigidos pela release VM
do `--migrate-only`, pois AddEasyStockAuth fail-fasta ANTES do builder.Build):
- ConnectionStrings__DefaultConnection (Postgres Render)
- Jwt__SecretKey, Jwt__Issuer, Jwt__Audience
- (Cors:AllowedOrigins ja vem do appsettings.Production.json)
- Conferir com `fly secrets list -a easystok`.

LIMITACOES / WATCH:
- Docker NAO instalado nesta maquina: nao rodei `docker build` real; o passo
  npm/tailwind dos Dockerfiles Web/Admin nao foi exercido localmente (mas ja
  funcionou em deploys anteriores).
- 1o deploy a USAR o gate `--migrate-only`: acompanhar os logs da release VM
  ("[migrate-only] ... Concluido. Encerrando..."). Migration que falhar aborta
  o deploy (versao antiga continua) — comportamento esperado.
- Ordem: API (easystok) primeiro, depois Web/Admin.

NENHUM erro bloqueante de deploy encontrado.

## Referencias
- ADR-0010 (RLS / UseRowLevelSecurityBypass), Program.cs:300 (RunMigrationsOnStartup), :130-150 (provider resolver)
