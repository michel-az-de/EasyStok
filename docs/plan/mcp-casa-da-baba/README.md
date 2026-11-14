# MCPs da Casa da Baba: diagnóstico, banco e operação pelo Claude

Issue #2520. Pedido do Felipe (2026-11-13): operar e diagnosticar a Casa da Baba pelo Claude (caixa, produção, atendimento, logs, banco), com escrita só para Felipe e Thatiane.

## Fatos medidos (2026-11-13)

| Item | Estado |
|---|---|
| MCP no repo | Nenhum. Reaproveitar: SSH, scripts e endpoints abaixo |
| Saúde | `/health`, `/health/live`, `/health/ready`, `/health/api`, `/health/dispatcher`, `/health/notificacoes`, `/health/disco`, `/health/version` (`PipelineExtensions.cs`) |
| Logs | `docker logs` em `ez-api` e `ez-worker` (host SSH `hostinger`, `vps-deploy.sh`). API também grava `logs/easystock-.log`; worker só console + OTLP. Redação já existe no servidor (`SensitivePatterns.cs`) |
| Histórico de deploy | `deploy-history.log` na VPS |
| Banco | Postgres, container `shared-postgres`, base `easystock`, EF Core, RLS (ADR-0010) |
| Precedente de leitura | `scripts/diagnostico/notificacoes-s0.sql`: `BEGIN TRANSACTION READ ONLY` + `SET LOCAL app.bypass_rls` via `docker exec psql` |
| Auth da API | JWT, `POST api/auth/login`, policies SuperAdmin, Admin, Gerente, Operador. Não existe nível "Owner" nem "master" |

Premissa refutada: `DiagnosticoController` (`/api/diagnostico/logs`) citado em `.knowledge` não existe neste repo. Não contar com ele.

## Decisões

1. **Um pacote, vários servidores** em `tools/mcp-baba/` (Node 24, SDK oficial `@modelcontextprotocol/sdk`, transporte stdio). Um processo por servidor, ligados só quando usados. Custo zero, sem SaaS.
2. **"SHA privado daqui"**: o servidor roda na máquina de cada pessoa, a partir deste repo privado, em commit conhecido (`git rev-parse HEAD` impresso no `hello` de cada servidor). Acesso à VPS só por SSH com a chave da pessoa. Nenhuma porta de banco nem de MCP exposta na internet.
3. **Sem segredo no repo**: credenciais em variável de ambiente ou arquivo local em `.gitignore` (`tools/mcp-baba/.env.local`).
4. **Leitura por padrão.** Escrita em servidor separado, desligado por padrão.
5. **Escrita de negócio passa pela API**, nunca por SQL, para manter regra e auditoria do domínio. SQL de escrita só no `baba-db-admin`.

## Servidores e ferramentas

L = leitura, E = escrita.

### baba-diag (#2521) · acesso: SSH `hostinger` + HTTPS público

| Ferramenta | Tipo | O que faz |
|---|---|---|
| `saude` | L | GET dos `/health/*` e `/health/version` (SHA em produção x `origin/master`) |
| `containers` | L | estado, health, reinícios e `GIT_SHA` de `ez-api`, `ez-worker`, `ez-web` |
| `logs_api`, `logs_worker` | L | `docker logs --since/--tail`, filtro por nível, texto ou regex, redação local, teto de bytes |
| `erros_recentes` | L | exceções e `ERR` agrupados por mensagem, com contagem e primeira/última hora (Brasília) |
| `deploy_historico` | L | últimas linhas de `deploy-history.log` |
| `disco_e_fila` | L | `/health/disco`, `/health/dispatcher`, `/health/notificacoes` |

Segurança: lista fixa de comandos remotos (allowlist), alvos fixos, nenhuma string do usuário vira shell (argumentos validados por padrão estrito), timeout e teto de saída.

### baba-db (#2522) · acesso: SSH + `docker exec shared-postgres psql`

| Ferramenta | Tipo | O que faz |
|---|---|---|
| `tabelas` | L | lista tabelas e contagem aproximada |
| `descrever` | L | colunas, chaves, índices |
| `consultar` | L | só `SELECT`, `WITH`, `EXPLAIN`; um comando; `BEGIN READ ONLY`, `statement_timeout` 15 s, limite 500 linhas, `SET LOCAL app.bypass_rls` (tenant único) |
| `amostra` | L | `SELECT *` limitado de uma tabela |

Papel de banco `baba_ro`: `LOGIN`, `NOBYPASSRLS`, `default_transaction_read_only = on`, **somente `GRANT SELECT` em tabelas de negocio listadas** (sem views: view de dono superusuario ignora RLS). Segredos e sessoes ficam fora (usuarios, tokens, credenciais, OTP, push). O gate roda cada consulta em `BEGIN READ ONLY` com `statement_timeout` de 15 s e `app.empresa_id` fixo vindo de arquivo root-only na VPS (`app.bypass_rls` nao e barreira, entao nao se confia em GUC). Script: `scripts/mcp/baba_ro.sql` (senha por `-v`). **Felipe roda o script e cria `/opt/baba-mcp/baba_ro.env`**. O filtro de SQL no cliente e a segunda camada.

### baba-db-admin (#2523) · desligado por padrão, tier alto

| Ferramenta | Tipo | O que faz |
|---|---|---|
| `preparar` | E (sem efeito) | executa em transação, mostra linhas afetadas e prévia, faz rollback, devolve token de 5 min |
| `confirmar` | E | com o token, repete e comita |
| `dump_antes` | L | `pg_dump` das tabelas afetadas, obrigatório antes de DELETE/UPDATE sem WHERE e de DDL |
| `auditoria` | L | lista as escritas feitas |

Identidade nominal: um papel de banco por pessoa (`felipe_master`, `thatiane_master`), senha local de cada um, habilitação por `BABA_MCP_ADMIN=1`. Auditoria de toda escrita: JSONL local (quem, quando, SQL, linhas, hash do dump) e tabela `mcp_auditoria` (migração, por isso tier alto: PR aberta até `aprovado`). Sem WHERE, DDL e `TRUNCATE` exigem digitar o nome da tabela como confirmação.

### baba-caixa (#2524), baba-producao (#2525), baba-atendimento (#2526) · acesso: API com JWT nominal

Login com `POST api/auth/login`; e-mail e senha em `BABA_API_EMAIL` e `BABA_API_PASSWORD` locais. O nível do usuário (policy da API) já limita o que cada pessoa vê e faz.

| MCP | Leitura | Escrita (só por endpoint existente, com confirmação) |
|---|---|---|
| caixa | sessão aberta, movimentos do dia, divergência, fechamento | abrir/fechar sessão, lançar movimento |
| producao | fila, lotes, rótulos, estoque de insumos | iniciar/concluir lote |
| atendimento | conversas, pedidos, decisão do Assistente (`#2496`), janelas | só leitura nesta fase |

A lista exata de endpoints sai do `swagger` na PR de cada um, não aqui.

## Ordem

1. Esta spec.
2. `baba-diag`.
3. `baba-db` leitura.
4. `baba-caixa`, `baba-producao`, `baba-atendimento`.
5. `baba-db-admin` por último, depois de a trilha de auditoria estar aprovada.

## Riscos

| Risco | Mitigação |
|---|---|
| Log com segredo ou dado pessoal | redação local além da do servidor; teste com token, senha e connection string |
| SQL de leitura que escreve (`SELECT func()`) | papel com `default_transaction_read_only` e `GRANT SELECT` apenas; transação READ ONLY |
| Consulta pesada derruba o banco | timeout, limite de linhas, `EXPLAIN` antes |
| Chave SSH da pessoa vira ponto único | alias `hostinger` já é o canal de deploy; nenhuma credencial nova no repo |
| MCP de escrita acionado por texto injetado em log ou linha | log e dado voltam como dado; escrita sempre exige `preparar` + `confirmar` do humano |

## Depende do Felipe

- Criar `baba_ro` e a senha na VPS (antes da prova real do #2522).
- Confirmar que "SHA privado daqui" é a leitura da decisão 2.
- Papéis nominais e acesso SSH da Thatiane (antes do #2523).

## Follow-up de seguranca do baba-db (revisao opus da PR #2549)

O guard de SQL (cliente e gate) e higiene, nao barreira: GUC `app.bypass_rls` e `app.empresa_id` sao setaveis por qualquer papel. A defesa real e policy que ignore o bypass para `baba_ro` (`AND current_user <> 'baba_ro'`), migracao de tier alto, e `REVOKE TEMP ... FROM PUBLIC`. Ate la: gate recusa `;` no meio, nomes de GUC normalizados e funcoes de SQL dinamico; tabelas com payload, log ou dado global ficam fora do GRANT.
