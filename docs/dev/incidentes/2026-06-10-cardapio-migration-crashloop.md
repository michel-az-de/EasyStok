# Postmortem — API em crash loop por migration do cardápio (#567)

**Data:** 2026-06-10
**Duração estimada do impacto:** ~algumas horas (noite 2026-06-09 / manhã 2026-06-10) até ~11:30 BRT 2026-06-10
**Severidade:** SEV2 (API pública degradada → site público da Casa da Babá fora do ar durante o crash loop)
**Status:** Resolvido (auto-recuperado via deploy do fix); postmortem blameless
**Autor:** Sessão Claude Code (verificação ao vivo na VM `easystok-vm`)

---

## Resumo

A migration `20260609182615_CardapioItemProdutoAgnostico` (ADR-0031) escrevia nomes de coluna em **snake_case** (`produto_id`, `nome_publico`) no SQL cru do filtro de índice, do CHECK e do índice de avulso, mas a tabela `cardapio_item` usa colunas em **PascalCase citado** (`"ProdutoId"`, `"NomePublico"`). No startup, o EF aplicava a migration e o Postgres falhava com `SqlState 42703` (coluna inexistente). Como a API roda com `RunMigrationsOnStartup=true` + `MigrationsFailFast=true`, a falha derrubava o processo no boot → **crash loop** na VM (RestartCount observado 15 no momento do diagnóstico do fix). Com a API fora, o site público da Casa da Babá (que consome `GET /api/storefront/{slug}/menu`) ficou indisponível.

O fix `18c135da` (closes #567) trocou o SQL para PascalCase citado, trocou `ADD CONSTRAINT IF NOT EXISTS` (sintaxe inválida no PG) por DROP+ADD idempotente, e alinhou `HasFilter` na config e no snapshot. O deploy automático (cron `*/5` → `vm-deploy.sh`) aplicou o fix ~15min após o push; a API subiu estável e o site recuperou.

---

## Impacto

- **Quem:** visitantes do site público da Casa da Babá (storefront) e qualquer consumidor da API durante a janela de crash loop.
- **Duração:** estimada em algumas horas (da publicação da migration quebrada até ~11:30 BRT de 2026-06-10).
- **Negócio:** storefront público indisponível na janela; sem perda de dados (a falha era no boot, antes de servir tráfego).

---

## Timeline (BRT)

| Hora | Evento |
|------|--------|
| 2026-06-09 ~15:26 | Commit `ce77d699` — migration `20260609182615` com SQL em snake_case (fatia 2 ADR-0031). |
| 2026-06-09 noite / 2026-06-10 manhã | Deploy automático aplica a migration quebrada → API falha no startup (`42703`) → crash loop (`MigrationsFailFast=true`). Site público cai junto. |
| 2026-06-10 11:14 | Commit `18c135da` (fix #567): SQL em PascalCase citado + DROP/ADD idempotente + HasFilter alinhado. DDL validado contra schema real da VM em transação+rollback. |
| 2026-06-10 ~11:30 | Cron `*/5` (`vm-deploy.sh`) rebuilda a stack com `18c135da`; API sobe, migration aplica, containers estabilizam. |
| 2026-06-10 ~21:33 | Verificação ao vivo: api healthy (restarts=0, up ~10h), `/health` Healthy, `GET /menu` 200, site público nginx 200. Incidente confirmado resolvido. |

---

## Causa raiz (5 Whys)

1. **Por que a API entrou em crash loop?** A migration falhava no startup com `42703` (coluna inexistente).
2. **Por que `42703`?** O SQL cru referenciava colunas em snake_case (`produto_id`/`nome_publico`), mas o schema usa PascalCase citado (`"ProdutoId"`/`"NomePublico"`).
3. **Por que o snake_case passou?** A migration foi escrita à mão (SQL cru para CHECK + índices parciais CONCURRENTLY) sem espelhar a convenção de citação do mapeamento EF, e o erro só aparece ao aplicar contra Postgres real.
4. **Por que não foi pego antes do deploy?** Não havia teste de integração que **aplicasse a migration contra um Postgres real** no CI. O host de integração `EasyStock.Api.IntegrationTests` (Testcontainers PG17, migrations no startup) existe, mas roda **fora do `EasyStok.CI.slnf`** (acionado por path no `azure-pipelines.yml`) — e não havia caso cobrindo o caminho avulso/migration nova.
5. **Por que o impacto chegou a prod?** `MigrationsFailFast=true` (correto — falha barulhenta > corrupção silenciosa) combinado com deploy automático levou a migration quebrada direto ao boot em produção, sem gate de integração entre o merge e o deploy.

**Causa raiz:** convenção de citação de identificadores divergente em SQL cru de migration, sem cobertura de integração que aplicasse a migration contra Postgres real antes do deploy automático.

---

## O que foi bem

- `MigrationsFailFast=true` falhou barulhento no boot em vez de servir um schema meio-aplicado (sem corrupção de dados).
- O deploy automático (cron `*/5`) aplicou o fix sem intervenção manual minutos após o push — recuperação rápida e sem toque.
- `/health` expõe o commit servido e o estado das dependências, tornando a verificação ao vivo trivial.

## O que foi mal

- Migration de SQL cru chegou a prod sem nenhum teste aplicando-a contra Postgres real.
- A divergência `EasyStok.CI.slnf` × pipeline (integração não roda no slnf local) escondeu a lacuna.
- O incidente foi reportado como "ativo" horas depois de já ter se auto-resolvido — faltou um sinal claro de recuperação além do `/health` (ex.: alerta de RestartCount).

---

## Ações (rastreadas no plano ADR-0031)

| Ação | Prioridade | Onde |
|------|-----------|------|
| Teste de integração que aplica a migration e valida `GET /menu` com item avulso contra PG real | P1 | Fase 2 do plano — `EasyStock.Api.IntegrationTests/Storefront/Menu/MenuControllerTests.cs` |
| Teste `POST_ItemAvulsoNomeDuplicado_Returns400` (exercita índice único parcial → 23505) | P1 | Fase 2 — `AdminCardapioControllerTests.cs` (novo) |
| Documentar/alinhar a divergência `EasyStok.CI.slnf` × integração por path no pipeline | P2 | Issue de testes (decisão: manter como está, documentar) |
| (Opcional) Alerta de RestartCount/health na VM para detectar crash loop em minutos | P3 | Backlog infra |

---

## Lição

SQL cru em migration precisa de cobertura de integração contra o banco real **antes** do deploy automático: a convenção de citação de identificadores (PascalCase citado vs snake_case) não é validada por build nem por teste unitário com mock. A Fase 2 do plano fecha exatamente essa lacuna.
