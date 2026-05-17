# Runbook — Restore PITR Postgres Azure

Procedimento de recuperacao do banco Postgres do EasyStok via PITR (Point-in-Time Recovery) Azure. Aplicar em incidente de perda de dados (deploy zerou base, migration corrompeu schema, drop acidental).

**RTO esperado:** ≤ 30min do detect ao trafego normalizado.
**RPO:** ate 5min (PITR Azure flexible-server).
**Janela de retencao:** 7 dias (default Azure flexible-server). Subir pra 35d se virar P0.

---

## Pre-requisitos

- Acesso Azure CLI (`az login`) com role `Contributor` no `rg-easystock`.
- Saber o nome do server source: `pg-easystock` (resource group `rg-easystock`).
- Saber o `restore-time` desejado (ISO 8601 UTC): minutos antes do incidente.
- Janela de manutencao acordada (Felipe avisa cliente — assumir 30min de downtime tipico).

---

## Passo 0 — Triagem (3min)

1. Confirmar incidente: comparar contagem de entidades-chave entre prod e baseline conhecido.

   ```sql
   SELECT
       (SELECT COUNT(*) FROM "Empresas") AS empresas,
       (SELECT COUNT(*) FROM "Pedidos") AS pedidos,
       (SELECT COUNT(*) FROM "Pagamentos") AS pagamentos,
       (SELECT COUNT(*) FROM "AssinaturasEmpresa") AS assinaturas;
   ```

   Se contagens cairam significativamente (> 5%) ou viraram zero, prosseguir.

2. **PARAR escrita imediatamente** — evita over-writing de dado restaurado:
   - Azure Portal → easystock-api → Configuration → adicionar env var `MAINTENANCE_MODE=read-only` (se middleware existir; senao step abaixo).
   - Alternativa: scale to 0 instances do `easystock-api` (Azure Portal → Scale).

3. Anotar `restore-time` (ex.: incidente detectado 14:32 UTC, escolher 14:25 UTC).

---

## Passo 1 — Disparar restore (5min para iniciar, 10–20min para completar)

```bash
az login

# Variaveis
RG=rg-easystock
SOURCE=pg-easystock
TARGET=pg-easystock-restore-$(date -u +%Y%m%d-%H%M)
RESTORE_TIME="2026-05-07T14:25:00Z"  # AJUSTAR

# Disparar restore
az postgres flexible-server restore \
    --resource-group "$RG" \
    --name "$TARGET" \
    --source-server "$SOURCE" \
    --restore-time "$RESTORE_TIME"
```

**O comando bloqueia ate o restore concluir (~10-20min para DB de ate 50GB).**

Em paralelo, em outro terminal, pode acompanhar:

```bash
watch -n 30 "az postgres flexible-server show \
    --resource-group $RG --name $TARGET \
    --query '{state: state, version: version}' -o table"
```

Quando `state = Ready`, restore concluido.

---

## Passo 2 — Validar dados restaurados (5min)

1. Pegar connection string do server restaurado:

   ```bash
   az postgres flexible-server show \
       --resource-group "$RG" --name "$TARGET" \
       --query "fullyQualifiedDomainName" -o tsv
   ```

2. Conectar e validar contagens:

   ```bash
   psql "host=<fqdn-do-target> port=5432 dbname=easystock_db user=psqladmin sslmode=require" \
       -c 'SELECT (SELECT COUNT(*) FROM "Empresas") AS empresas, \
                  (SELECT COUNT(*) FROM "Pedidos") AS pedidos, \
                  (SELECT COUNT(*) FROM "Pagamentos") AS pagamentos;'
   ```

3. Comparar com baseline conhecido (do dashboard de admin antes do incidente).
   - Se contagens batem: prosseguir.
   - Se nao batem: tentar `restore-time` mais cedo (passos 1 e 2 de novo). Pode ser que o estado bom seja antes da janela escolhida.

---

## Passo 3 — Swap connection string (3min)

1. Azure Portal → `easystock-api` App Service → Configuration → Application settings.
2. Editar `DB_HOST` (ou similar conforme appsettings) para apontar para o server restaurado (`<target-fqdn>` em vez de `<source-fqdn>`).
3. **Salvar e reiniciar**.
4. Repetir para `easystock-admin` e `easystock-web` se conectam direto ao Postgres.

---

## Passo 4 — Validacao smoke pos-restore (3min)

1. `curl https://easystock-api-dfdjgsfwaqhkgvf9.brazilsouth-01.azurewebsites.net/health/ready` → esperar 200.
2. Abrir painel admin, login com SuperAdmin → conferir dashboards.
3. Login com 1 cliente teste → conferir Pedidos/Caixa/Estoque.
4. Reativar trafego: remover `MAINTENANCE_MODE=read-only` (ou subir replicas de volta).

---

## Passo 5 — Cleanup pos-incidente (24h depois, fora da janela de stress)

1. **Manter `pg-easystock-restore-XXX` rodando por 24-48h** para validacao prolongada e backup adicional. NAO deletar imediatamente.
2. Apos validacao, decidir:
   - **Opcao A**: tornar o restored server o novo source. Renomear via `az postgres flexible-server update --new-name pg-easystock`.
   - **Opcao B** (recomendada): manter ambos por 7 dias, depois deletar o restored server.
3. Deletar quando seguro:

   ```bash
   az postgres flexible-server delete \
       --resource-group "$RG" --name "$TARGET" --yes
   ```

4. **Post-mortem**: registrar incidente em `.knowledge/recent-evolution.md` com timeline + RTO real + RCA.

---

## Exercicio mensal (preventivo)

Dia 1 de cada mes, executar drill SEM trafego real:

1. Criar restore para snapshot do dia anterior (passo 1).
2. Conectar e contar entidades (passo 2). Comparar com prod.
3. **NAO trocar connection string** — exercicio nao impacta prod.
4. Cronometrar tempo total e registrar em `.knowledge/stability-roadmap.md` Bloco 1.
5. Deletar o restored server (passo 5).

**Se RTO > 30min**, abrir P1 em `.knowledge/stability-roadmap.md` para investigar gargalos (otimizar SQL, paralelizar steps, aumentar SKU temporariamente).

---

## Pontos de atencao

- **Connection string e senha do server restaurado**: server restaurado preserva users e senhas. Usar mesma senha do source.
- **Network/Firewall rules**: restore PRESERVA firewall rules. Nao deveria ter problema, mas se conexao falhar, conferir Azure Portal → Networking.
- **SKU**: restore vai pro mesmo SKU do source. Custo dobra durante drill (paga 2 servers).
- **Logs**: DURANTE o restore, source continua aceitando escrita. Por isso passo 0 (parar escrita).
- **Replication slots / changefeeds**: nenhum hoje. Se adicionar (Debezium/eventos), revalidar runbook.

---

## Quando NAO usar este runbook

- **Drop accidental de tabela**: PITR funciona, mas talvez seja mais rapido `pg_dump` da tabela especifica + `psql` restore. Pular ao passo 1 mas com `--time` na ferramenta de dump.
- **Corrupcao logica em poucos rows**: identificar pelo Audit log (`PedidoAlteracao`, `PagamentoAlteracao` etc.) e restaurar manualmente.

---

## Telefones / contatos

- Azure Portal: https://portal.azure.com → resource group `rg-easystock`.
- Felipe: <preencher>
- Comunicar cliente afetado em vez de comunicar lista geral. Status update por chat/email com tempo estimado.
