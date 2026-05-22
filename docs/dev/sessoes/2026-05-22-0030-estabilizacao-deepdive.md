# Sessao estabilizacao deep-dive (migrations + DI + integration tests + xmin)

Data: 2026-05-22 ~00:30 (UTC)
Status: parcial — bugs de produto resolvidos; resta dívida de teste (workflows) + decisao RLS
Identidade: felipe.azevedo@gmail.com / gh michel-az-de

## O que foi feito (commitado na master)
Bugs de PRODUCAO:
- `c5dbb555` fix(migrations): remove ~69 creates duplicados (3 migrations re-emitiam
  schema por reset do ModelSnapshot) — replay do-zero quebrava com 42P07/42701/42710.
  Prod-safe (migrations ja aplicadas nao re-rodam). Validado via Testcontainers (WSL).
- `e25b1c33` fix(estoque/fiscal): FromSqlRaw FOR UPDATE seleciona `xmin`. Entidades com
  xmin como concurrency token (IsRowVersion) + `SELECT *` -> EF envolve em subquery p/
  LIMIT e referencia e.xmin que SELECT * nao expoe -> 42703. Afetava 4 fluxos CORE:
  saida de estoque (GetByIdComLockAsync), consumo FIFO/FEFO de lotes, numeracao NFC-e
  (NumeracaoNfeService), baixa de lancamento (LancamentoRepository). Aditivo, prod-safe.

Integration tests: 3 -> 26/44 verdes (toda a divida de test-data/tenant dos repositorios):
- 752a3d39 Analytics (9), ea05e250 AnuncioIa (5), d46f92ae Produto/ProdutoVariacao/UsoIa (5),
  a5a69518 ItemEstoque (4/5). Padroes: varchar(30) estourado, contexto bare sem tenant
  (SetMobileTenantContext), FK sem pai (criar Empresa/Produto), campos obrigatorios faltando.

Antes (mesma sessao): merge PRs #191/#192, deploy 3 apps Fly, fix(etiqueta) render.pdf
(db767627), fix(caixa) /api/empresa (2d9d2c12), Admin hardening (94a18034), auditoria DI.

## Como rodar os integration tests (sem Docker no host)
Docker nao esta no Windows host, MAS o WSL Ubuntu tem .NET 10 SDK + Docker (instalado
nesta sessao: `sudo apt-get install -y docker.io`; `sudo service docker start`;
`sudo chmod 666 /var/run/docker.sock`). Rodar:
`wsl bash -lc 'cd /mnt/c/easy/EasyStok && dotnet test EasyStock.Infra.Postgre.IntegrationTests/...csproj -c Debug'`
Testcontainers sobe postgres:16-alpine sozinho.

## Restam 18 falhas (diagnosticadas)
1. **~13 EstoqueWorkflows + EstoqueConcurrency:** test-data. Cada teste de workflow precisa
   criar Empresa (FK_categorias_empresas), CriadoEm/AlteradoEm onde falta, QuantidadeInicial+
   CustoUnitario nos ItemEstoque, e SetMobileTenantContext. Mecanico, mesmo padrao dos repos
   ja corrigidos, mas volumoso (grafos completos). Prod-safe (so teste).
2. **~5 RowLevelSecurityTests + puzzle IsDeletado:**
   - Os testes conectam como DONO das tabelas; a migration AddRowLevelSecurity NAO faz
     `FORCE ROW LEVEL SECURITY` -> o dono bula RLS -> testes veem cross-tenant.
   - **ACHADO DE SEGURANCA:** se em prod o app conecta como dono das tabelas, o RLS
     (ADR-0010, defesa-em-profundidade) pode estar **inativo** — so o filtro EF protege.
   - **CUIDADO:** adicionar FORCE e mudanca de comportamento — bloquearia QUALQUER query
     sem `SET app.empresa_id` nem bypass (seeds, jobs, paths de boot). Avaliar TODO path
     antes de ativar, senao quebra prod. NAO fazer as pressas.
   - `column "IsDeletado" of relation produtos does not exist`: IsDeletado NAO esta em
     entidade/config/snapshot/migration — so na ignore-list do AuditTimestampsInterceptor.
     Investigar qual teste/raw-SQL referencia produtos.IsDeletado.

## Pendencias adicionais
- EasyStock.Api.IntegrationTests + Infra.MongoDb.IntegrationTests: nao rodadas — provavel
  divida de teste stale similar.
- SQLite dev-fallback incompleto (~25 repos nao registrados) — decisao: completar / remover
  / fail-fast (ver handoff 2026-05-21-2345). Felipe optou por nao mexer.
- Resolver de provider faz fallback SILENCIOSO p/ sqlite quando postgres indisponivel —
  mascarou a validacao de DI; considerar log/fail-fast mais claro.

## Proxima acao recomendada
Sessao focada com contexto fresco: (1) test-data dos workflows (rapido), (2) decisao FORCE
RLS com avaliacao de impacto em prod. Tudo rastreado em task #18.
