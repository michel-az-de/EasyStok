# Banca revisora do plano #2965

- Data: 2026-11-20
- Formato: três revisores independentes, um por entrega, cada um tentando refutar a spec contra o código real e o GitHub
- Resultado: 36 achados (7 Críticos, 14 Altos), todos aceitos e incorporados na v2 do README. Na E1, a meta de 10 min não tinha linha de base e a coleção serial não ganharia paralelismo. Na E2, os nomes de check-run não existiam, o `admin` nem entra no deploy e 2 min era inalcançável. Na E3, faltavam a cópia fora do alcance da VPS, o `prune` estava do lado errado e a KEK do cofre não estava coberta

## E2 · Deploy

| # | Sev. | Achado | Decisão |
|---|---|---|---|
| 1 | Crítico | Não existem check-runs `CI` nem `build-images`; os nomes reais são os dos jobs (`Build + dotnet test`, `Api.IntegrationTests (Postgres real)`, `build-push (...)`) | Aceito: job agregador `gate` de nome estável (S2.0) |
| 2 | Crítico | `admin` não tem Dockerfile, está no profile `admin-legado` e não entra no deploy (`vps-deploy.sh:57`) | Aceito: `admin` saiu da spec e da tabela de dores |
| 3 | Alto | O portão só abre cerca de 35 min depois do merge | Aceito: meta medida do início do script; o fim a fim depende da E1 |
| 4 | Alto | `build-images` cancela runs por ref; SHAs do master ficam sem imagem | Aceito: grupo por SHA (S2.0) e recusa explícita para `cancelled` (S2.1) |
| 5 | Alto | Não estava dito onde o `gh` roda | Aceito: na máquina local, antes do ssh, com `GATE_CMD` injetável |
| 6 | Alto | O teste do script não tem `gh` falso | Aceito: aceite 1 com `gh` falso em 4 estados |
| 7 | Alto | Migração aplicada no startup não volta com o rollback de imagem | Aceito: S2.4 (lista as migrações, `migracao=sim`, regra de retrocompatibilidade) |
| 8 | Médio | Tag do GHCR usa SHA de 40 caracteres; o script usa 8 | Aceito: `pull` por `SHA_FULL`, retag para `vps-$SHA` |
| 9 | Médio | 2 min é inalcançável com `start_period` de 120 s | Aceito: teto medido em 3 deploys, estimativa de até 4 min |
| 10 | Médio | Commit só de docs fica sem check-run | Aceito: S2.5 (último ancestral verde com diff só ignorado) |
| 11 | Baixo | O código 9 já existe no `console-deploy.sh` | Aceito: código 10 |
| 12 | Baixo | `--motivo` hoje só vale com `--rollback` | Aceito: parser e histórico ajustados na implementação |

Confirmado pela banca: os pacotes do GHCR são públicos (manifest 200 sem login); a imagem do CI usa o mesmo Dockerfile e o mesmo `GIT_SHA` da VPS; a guarda #1336 já recusa SHA anterior ao master; `master` não tem proteção, então nenhum check é exigido por nome no servidor. Arquitetura amd64 dos dois lados ainda não conferida.

## E3 · Backup

| # | Sev. | Achado | Decisão |
|---|---|---|---|
| 1 | Crítico | A cópia puxada pela máquina local só aparecia como mitigação, sem item nem aceite | Aceito: S3.4 e aceite 4 |
| 2 | Crítico | A lixeira do Drive não protege: o `prune` apaga e o token roubado esvazia a lixeira | Aceito: sem `--prune` na VPS; `prune` só do lado da cópia puxada |
| 3 | Crítico | A KEK do cofre (`EZ_CRYPTO_KEK` e anteriores) só estava implícita no `.env` | Aceito: S3.2 e decifração real no teste mensal |
| 4 | Alto | `pg_dump` de um banco não leva roles nem grants; a RLS depende da role `easystock` | Aceito: `pg_dumpall --globals-only` e consulta como `easystock` no teste |
| 5 | Alto | Faltavam os volumes do wppconnect e os demais compose | Aceito: inventário ampliado e `docker volume ls` no S3.0 |
| 6 | Alto | `shared-postgres` é compartilhado; escopo dos bancos indefinido | Aceito: S3.0 decide banco a banco, por escrito |
| 7 | Alto | Comparar contagens com a produção que já mudou não prova nada | Aceito: manifesto gravado no instante do dump |
| 8 | Alto | Restaurar na VPS compete com a produção | Aceito: restauração na máquina local |
| 9 | Médio | Refresh token OAuth em modo teste expira em 7 dias; conta de serviço só tem cota em Drive compartilhado | Aceito: entra como risco e pesa a favor do P1 a |
| 10 | Médio | Lock preso e `prune` concorrente | Aceito: `flock` e aceite 5 |
| 11 | Médio | LGPD e conflito entre `keep-monthly 12` e os 5 anos dos rótulos | Aceito: S3.7, tag própria e ADR |
| 12 | Baixo | Alerta só de falha não pega timer que não rodou | Aceito: batimento de sucesso (S3.6) |

Confirmado pela banca: Redis sem persistência, fora do backup; um volume de DataProtection por serviço; `uploads-data` compartilhado por api e worker; o dump pré-deploy já existe; ADR-0012 nunca foi ligada, então substituí-la não quebra nada em uso. Limites do Drive, do restic e do rclone vieram de conhecimento prévio e serão conferidos na documentação oficial no S3.0.

## E1 · Esteira

| # | Sev. | Achado | Decisão |
|---|---|---|---|
| 1 | Crítico | 88 classes da `PostgreSqlTestCollection` dividem uma instância e rodam em série; banco por classe não as paraleliza | Aceito: S1.0 mede e S1.3 decide desmontar a coleção |
| 2 | Crítico | 10 min não fecha: cada parte paga cerca de 4 min de restore e build, e não havia linha de base | Aceito: S1.0 mede antes, compilação única com artefato, meta derivada da medição (estimativa de 10 a 14 min) |
| 3 | Alto | Não há required check nem ruleset no master; o job espelho resolvia um problema inexistente | Aceito: job agregador `ci-ok` (S1.6), também usado pela E2 |
| 4 | Alto | Role é do cluster e não vem no clone; criação concorrente dá 23505; roles de teste com nome fixo colidem | Aceito: S1.2 |
| 5 | Alto | `Inventario.IntegrationTests` usa o banco do service e faz `DROP SCHEMA`; estava fora da spec | Aceito: job próprio sem matriz (S1.5) |
| 6 | Alto | Lista fechada de namespaces perde teste novo em silêncio | Aceito: guarda `--list-tests` contra a soma dos TRX |
| 7 | Médio | Clonar com conexão aberta no modelo dá 55006 | Aceito: `ClearAllPools`, `datallowconn=false` e teste do harness |
| 8 | Médio | `DROP ... WITH (FORCE)` mata conexões de hosts vivos | Aceito: `ClearAllPools` no reset e host parado antes |
| 9 | Médio | `paths-ignore: **/*.md` pula guardas que leem `.md` e `docs/` | Aceito: lista restrita e arquitetura sempre roda |
| 10 | Médio | Api.IntegrationTests migra e semeia no startup; 18 construtores próprios | Aceito: S1.4 |
| 11 | Baixo | CodeQL fora da PR é troca de risco, não ganho gratuito | Aceito: na PR só com mudança de C#, projeto ou workflow; troca declarada |
| 12 | Baixo | Rollback por "mesma interface" é otimista | Aceito: adaptador fino e reversão testada num ramo |

Confirmado pela banca: o reset com `EnsureDeleted` e migração é o custo; a etapa Test levou 32,2 e 32,5 min no run `44769925513`; `DROP DATABASE ... WITH (FORCE)` existe nas versões 16 e 17 usadas; os GRANTs vêm no clone; `MigracaoForaDeOrdemTests` sobe o próprio contêiner; `checar-testes-pulados.py` aceita vários TRX; nenhum script depende do nome do job de CI.
