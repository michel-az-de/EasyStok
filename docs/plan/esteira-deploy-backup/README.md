# Esteira rápida, deploy seguro e backup completo (#2965)

- Status: proposta v2, revisada pela banca (`revisao-adversarial.md`, 36 achados, todos incorporados); aguarda a aprovação do Felipe
- Data: 2026-11-20
- Tier: alto (workflows, script de deploy com segredo, processo)
- Relacionados: ADR-0012 (backup de rótulos), ADR-0055 (tier por spec), #2958 (espelho do código), estudo `docs/dev/estudo-esteira-rapida-e-backup-2026-11-20.pdf`

## 1. Problema

| Dor | Causa medida |
|---|---|
| CI de uma PR leva 36 min | 32 min são a etapa Test dos dois jobs. `PostgreSqlDatabaseFixture.ResetDatabaseAsync` faz `EnsureDeleted` e roda as 219 migrações; há 149 chamadas a ele em 53 arquivos. 111 classes com `IClassFixture` sobem um contêiner cada, e 88 classes da `PostgreSqlTestCollection` rodam em série (`PostgreSqlDatabaseFixture.cs:113-121`). A Api.IntegrationTests sobe mais um contêiner por classe (`ci.yml:122-127`) |
| CodeQL de 18 min em toda PR e todo push | `codeql.yml:4-7` dispara em `pull_request` e `push` |
| PR em rascunho e PR só de docs rodam o CI .NET inteiro | `ci.yml:3-8` sem `paths-ignore` nem filtro de rascunho; 45 execuções canceladas em 4 dias |
| Deploy publica master sem CI verde | `vps-deploy.sh` não consulta o GitHub; compila na VPS (`vps-deploy.sh:101-106`) |
| Imagens do CI não servem ao deploy | `build-images.yml` publica api, web e worker no GHCR (pacotes públicos, mesmo Dockerfile e mesmo `GIT_SHA` da VPS), mas a VPS compila de novo e o compose usa tags locais `:vps`. O `build-images` cancela o run anterior por ref (`build-images.yml:17-19`), então SHAs intermediários do master ficam sem imagem |
| Nenhuma cópia do banco fora da VPS | O dump do deploy fica em `/home/felipe/backups`. `backup-rotulos.yml` (ADR-0012) é pulado todo dia porque `BACKUP_ENABLED` não existe (execuções de 18, 19 e 20/11 em `skipped`) |
| Uma feature vira até 35 PRs | 497 PRs em 30 dias, 67% `fix`. Cada fatia repete issue, PR, CI, review, merge e limpeza |

## 2. Decisões

| # | Decisão | Origem |
|---|---|---|
| D1 | GitHub continua sendo o forge e o CI. Não haverá runner próprio: o repositório é público e código de PR rodaria ao lado da produção | Felipe, 20/11; estudo |
| D2 | Nenhum teste é removido nem pulado para ganhar tempo. O número de testes executados antes e depois é aceite | Felipe, 20/11 |
| D3 | Ordem: E1, E2, E3 em sequência, cada uma uma PR completa. E3 pode começar em paralelo porque não toca os mesmos arquivos | proposta |
| D4 | O backup usa restic com destino rclone no Google Drive, cifrado no cliente | estudo |
| D5 | Banco no nível 1 (dump diário e antes de deploy). Nível 2 (WAL contínuo) só com nova decisão | proposta |

## 3. Specs

### E1 · Esteira rápida

| Item | Conteúdo |
|---|---|
| Objetivo | Cortar a etapa Test (32 min nos dois jobs, run `44769925513`) sem tirar teste. A meta final sai da medição do S1.0; a estimativa é de 10 a 14 min, porque cada job paralelo paga cerca de 4 min fixos de restore e build |
| S1.0 Medição primeiro | Do TRX e de um log temporizado: tempo de `ResetDatabaseAsync` e de init de fixture, separados do corpo dos testes, e quanto vem da coleção `PostgreSqlTestCollection` (88 classes que rodam em série, uma instância só) e quanto do `IClassFixture` (111 classes em paralelo, um contêiner cada). Sem esse número, nada do S1.1 em diante começa |
| S1.1 Banco modelo | `PostgresClusterFixture` único por assembly (singleton estático): um contêiner, o modelo `easystock_template` migrado uma vez, depois `ClearAllPools` e `datallowconn=false` (clonar com conexão aberta dá 55006). Cada classe recebe `CREATE DATABASE <nome único> TEMPLATE easystock_template`. `ResetDatabaseAsync` faz `ClearAllPools`, `DROP DATABASE ... WITH (FORCE)` e clona de novo; testes com host vivo param o host antes do reset. `PostgreSqlDatabaseFixture` vira adaptador fino com a mesma interface |
| S1.2 Roles | Role é do cluster e não vem no clone (só os GRANTs vêm). `rls_test_client` é criado uma vez no init do cluster, antes de qualquer clone. Roles criados por teste (`AcervoSegurancaE2ETests.cs:167`, `AcervoCoberturaSegurancaE2ETests.cs:381`, `PermissoesLegadasMigrationTests.cs:50`) passam a nome único com `DROP ROLE` no fim |
| S1.3 Coleção serial | Conforme o S1.0: tirar o `[Collection("PostgreSqlTestCollection")]` das 88 classes, que passam a ter banco próprio e rodar em paralelo, ou manter a coleção e aceitar que ela não ganha nada |
| S1.4 Api.IntegrationTests | Os 18 `new PostgreSqlBuilder` passam pelo mesmo cluster. O modelo é gerado pelo startup real (migração e seed da demo) uma vez. O ramo `EASYSTOCK_IT_PG` de `PostgresApiIntegrationTests.cs:49` sai e o cache estático `_seedDemoDisponivelCache` é revisto |
| S1.5 Partes paralelas | Um job compila uma vez e publica o artefato; as partes rodam `dotnet test --no-build`. Partes balanceadas pelo tempo medido no S1.0, não por namespace. Um passo de guarda compara `dotnet test --list-tests` com a soma dos `executed` dos TRX e falha se algum teste não rodou em exatamente uma parte. `Inventario.IntegrationTests` (usa o banco do service e faz `DROP SCHEMA`), os projetos de unidade e a arquitetura ficam num job próprio, sem matriz |
| S1.6 Job agregador | `ci-ok` com `needs` de todos os jobs e `if: always()`: um nome estável para o portão da E2 e para uma proteção de branch futura. Hoje não há proteção nem ruleset no master |
| S1.7 CodeQL | Na PR, só quando muda `**/*.cs`, `*.csproj` ou `.github/workflows/`; sempre em `push` no master e semanal. Troca de risco declarada: achado em PR sem código C# só aparece no master, antes do deploy. Substituído pela #3277: só semanal e `workflow_dispatch` |
| S1.8 Rascunho | Jobs .NET com `if: github.event.pull_request.draft == false`; gatilho `ready_for_review` adicionado |
| S1.9 Só docs | `paths-ignore` só para `docs/plan/**`, `docs/dev/**` e `changelog.d/**`. `**/*.md` fica de fora, porque `GrafiaDaMarcaTests.cs:23` e outras guardas leem `.md`, `.yml` e `docs/`. A arquitetura roda mesmo nesses casos, num job leve |
| Aceite | 1. Linha de base do S1.0 registrada na PR. 2. Mediana de 5 execuções do CI de PR menor ou igual à meta fixada no S1.0. 3. Soma dos `executed` igual à lista de testes e igual à do master antes da mudança. 4. Nenhum teste pulado fora do opt-in. 5. Teste do próprio harness: clonar o modelo 2 vezes seguidas e criar o role em paralelo sem erro. 6. PR em rascunho não roda .NET. 7. PR só de `docs/plan/` termina em até 3 min |
| Rollback | Reverter a PR. Se a coleção for desmontada (S1.3), testar a reversão num ramo antes do merge |
| Risco | Teste que dependia de estado de outra classe aparece ao isolar o banco. `DROP ... WITH (FORCE)` pode gerar falha intermitente em teste com host vivo. Corrigir o teste, nunca marcar skip (D2) |
| Tier | Alto (`.github/workflows/`) |

### E2 · Deploy seguro e rápido

| Item | Conteúdo |
|---|---|
| Objetivo | O deploy só publica SHA com CI verde e troca o build na VPS por download da imagem. Meta medida do início do script ao smoke; o tempo do merge ao ar depende da E1 |
| S2.0 Pré-requisito | Usa o job `ci-ok` da E1 (S1.6). `build-images.yml` passa a agrupar por SHA, sem cancelar runs do master |
| S2.1 Portão de CI | Roda na máquina local, antes do ssh (`vps-deploy.sh:259-267`). Consulta `gh api repos/:owner/:repo/commits/<SHA_FULL>/check-runs` e exige `ci-ok` e os 3 `build-push (...)` com `success`. `in_progress` recusa, ou espera com `--aguardar` até um teto; `cancelled`, ausente ou `gh` sem login recusa. Nunca há fallback silencioso. Recusa sai com código 10 (o 9 já existe no `console-deploy.sh`). `--urgente --motivo "<texto>"` pula o portão e grava `modo=urgente` no histórico. Comando injetável (`GATE_CMD`) para o teste |
| S2.2 Imagem do GHCR | `docker pull ghcr.io/michel-az-de/easystok-<svc>:<SHA_FULL>` antes do dump; confere o `GIT_SHA` da imagem; retag para `easystok-<svc>:vps-$SHA` e segue o fluxo atual (faxina e `vps-prev-<ts>` intactos). Sem imagem, recusa; build local só com `--urgente` |
| S2.3 Smoke | Depois do healthy e do SHA conferido, uma lista fechada de rotas públicas responde 200; falha volta a imagem anterior e o smoke roda de novo na imagem anterior |
| S2.4 Migração | A migração roda no startup da API (`RunMigrationsOnStartup`) e o rollback de imagem não a desfaz (`vps-deploy.sh:188`). O script lista as migrações novas entre o SHA no ar e o novo e grava `migracao=sim` no histórico. Migração nova precisa ser retrocompatível (expandir antes, contrair depois) |
| S2.5 Commit só de docs | SHA sem check-run porque só mexeu em docs: o portão aceita o último ancestral com `ci-ok` verde se o diff até ele for só caminhos ignorados |
| Fora | Deploy automático (P5). `console-deploy.sh` fica fora nesta entrega |
| Aceite | 1. Teste do script com `gh` falso (verde, vermelho, pendente, ausente): vermelho sai 10 sem tocar contêiner. 2. Tempo do início ao smoke medido em 3 deploys e fixado como teto (estimativa: até 4 min, porque a API tem `start_period` de 120 s). 3. `--urgente` sem motivo sai 2. 4. Os testes atuais de `vps-deploy.test.sh` continuam passando |
| Rollback | `vps-deploy.sh --rollback <sha> --motivo` já existe; reverter a PR volta ao build local |
| Tier | Alto (workflows) |

### E3 · Backup completo no Google Drive

| Item | Conteúdo |
|---|---|
| Objetivo | Tudo que não se reconstrói a partir do código sai da VPS todo dia, cifrado, e prova que volta |
| S3.0 Inventário | Na VPS: `docker volume ls`, `\l` do `shared-postgres` (é compartilhado: cada banco entra ou fica fora, com motivo escrito), tamanho do banco e disco livre. Já conhecidos: `uploads-data` (api e worker), `dataprotection-api-keys`, `dataprotection-web-keys`, `dataprotection-admin-keys`, `wppconnect-tokens` e `wppconnect-user-data` (sem eles o WhatsApp precisa ser pareado de novo), todos os compose e o `.env` de `/opt/stacks/easystok`. Redis fica fora: roda sem persistência (`compose.yaml:39`) |
| S3.1 Banco | `pg_dump -Fc` de cada banco que entra, mais `pg_dumpall --globals-only` (a role `easystock` NOBYPASSRLS e os grants, sem os quais a RLS não se aplica na volta) e o `initdb/30-easystok.sql`. Junto, um manifesto com a contagem das tabelas-chave no instante do dump |
| S3.2 Cofre | O `.env` com `EZ_CRYPTO_KEK` e todas as KEKs anteriores entra no backup. Sem a KEK do dia do dump, o cofre (`CredencialIntegracao`: Mercado Pago, WhatsApp, certificados) fica ilegível (`IntegrationCredentialResolver.cs:154`). Uma cópia das KEKs fica também fora do Drive, junto da senha do restic (P2) |
| S3.3 Envio | `restic` e `rclone` na VPS. Um timer systemd às 03h (Brasília), sob `flock`, faz dump, `restic backup` e `restic forget` por tag, SEM `--prune`. O dump que `vps-deploy.sh` já faz entra com a tag `pre-deploy` (10 últimos). Espelho e bundle da #2958 entram no mesmo repositório |
| S3.4 Cópia puxada | A VPS invadida pode apagar o Drive com o token do rclone, e cifrar não impede apagar. Defesa: a máquina local (ou outro agente de confiança) puxa toda semana o repositório restic para um segundo destino, com credencial que a VPS não tem. O `prune` e o `check --read-data-subset` rodam só desse lado |
| S3.5 Restauração mensal | Dia 1, na máquina local e não na VPS de produção: restaura o último snapshot num Postgres descartável, compara as contagens com o manifesto do próprio snapshot, roda uma consulta como `easystock` conferindo que a RLS filtra, decifra uma linha real do cofre com a KEK restaurada e abre um arquivo de `uploads-data` |
| S3.6 Alerta | Falha avisa, e o sucesso manda batimento (dead-man switch): timer que não rodou também alerta. O canal concreto é escolhido no S3.0 entre os que já existem |
| S3.7 Retenção e LGPD | Diário 7, semanal 4, mensal 12; rótulos com tag própria por 5 anos (ADR-0012). `backup-rotulos.yml` (R2, nunca ligado) é substituído. Dado pessoal de cliente no Drive exige conta da empresa (Workspace), nunca de pessoa física, registrado em ADR |
| Aceite | 1. Snapshot diário no Drive por 3 dias seguidos, com batimento recebido. 2. Restauração da S3.5 aprovada nos 4 itens. 3. Nomes e conteúdo ilegíveis no Drive. 4. Prova de que a credencial da VPS não alcança a cópia puxada. 5. Timer interrompido no meio não trava o seguinte (lock tratado) |
| Rollback | Desligar o timer. Nada em produção muda de comportamento |
| Risco | Senha do restic ou KEK perdidas tornam o backup ilegível. Refresh token OAuth de app em modo teste expira em 7 dias, e conta de serviço só tem cota em Drive compartilhado (P1). Limites e cotas do Drive, do restic e do rclone serão conferidos na documentação oficial no S3.0 |
| Tier | Alto (segredo novo e processo) |

## 4. Fora deste plano

Regra "uma feature, uma PR": muda ADR-0055, `CLAUDE.md` e `AGENTS.md`. Vira ADR própria depois que a E1 provar a esteira de 10 min, porque sem ela uma PR maior só esperaria mais.

## 5. Decisões pendentes do Felipe

| # | Pergunta | Opções |
|---|---|---|
| P1 | Conta do Drive | a) Drive compartilhado do Workspace FMA · b) seu usuário por OAuth (nunca a conta Eleva) |
| P2 | Onde fica a senha do restic | a) cofre de senhas + papel · b) dois cofres de senhas |
| P3 | Nível do backup do banco | a) nível 1 agora · b) nível 2 já |
| P4 | Ordem | a) E1, E2, E3 · b) E3 primeiro · c) E1 e E3 em paralelo |
| P5 | Deploy depois do verde | a) manual com portão · b) automático |
