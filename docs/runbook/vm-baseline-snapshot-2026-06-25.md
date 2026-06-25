# Runbook — Baseline snapshot da VM Azure (2026-06-25)

> Primeiro backup da história desta VM. A VM `easystok-vm` nunca teve backup
> (medido: `az snapshot list` vazio, `az backup vault list` vazio, nenhum cron/timer
> de dump). Este snapshot é o ponto-zero de recuperação, tirado ANTES de qualquer
> mudança de código que dispare migration in-place no boot da API
> (`RunMigrationsOnStartup: "true"` + `StartupMigrationsAndSeed.RunAsync`).

## MANIFEST

| Campo | Valor |
|---|---|
| Capturado em (UTC) | 2026-06-25 01:34:36 |
| Origem | VM Azure `easystok-vm` (RG `easystok-app_group`), container `easystok-postgres` |
| Postgres | 17.10 (`postgres:17-alpine`), DB `easystock`, user `easystock` |
| GIT_SHA repo (HEAD no momento) | `bfae15fb2dab6d87b48cfb0037eca599f12031e8` |
| GIT_SHA servido pela VM (API container, último medido) | `a66b5b789b10a70e30cd5c3b519f9f7602e069c3` (ancestral linear de HEAD; VM não redeployada — `SEM_SCHEDULER`) |
| Método de dump | `pg_dump -Fc -Z9` (Postgres) + `tar | zstd -19` (3 volumes não-pg) via `az vm run-command` |
| Método de exfil | SSH efêmero (chave ed25519 anexada à `authorized_keys`, scp, **chave revogada imediatamente**) |
| Local durável | `C:\easy\snapshots\baseline-20260625-013436\` (disco da máquina do Felipe, fora da VM) |
| Cópia cloud pessoal | **PENDENTE** — OneDrive pessoal não configurado nesta máquina (só "OneDrive - Avanade" = proibido) |

### SHA256 dos artefatos

```
075553aabb915b44957566cc5565ff59853e763cc73fb3bee085d5330c05b24c  pg.dump
b4ff994844dbf271e08b5c7cb588fae821fac005f2ab13ff34ee35e87a457659  easystok_uploads-data.tar.zst
e15536eb52faebb9bcd26719bc96de76bd15ce330cab5e5f5e59a0628d7fd5be  easystok_dataprotection-admin-keys.tar.zst
ae5b8dcc812ac49bbad67d8133b44917456283fb6fbd64029268e92ce89f4c5f  easystok_dataprotection-web-keys.tar.zst
```

Tar combinado (`easystok-baseline-20260625-013436.tar`): `23d6fb260ff7675a2592e42c2ef51353e2deaaccc76c2edae518366be8e86884`

### Conteúdo do snapshot (contagens medidas, validadas no restore)

| Tabela | Contagem | Nota |
|---|---|---|
| empresas | 2 | |
| lojas | 3 | |
| usuarios | 6 | |
| produtos | 23 | |
| pedidos | 8 | último 2026-06-16 16:43 UTC |
| cardapio_item | 19 | |
| entity_alteracoes | 178 | |
| audit_logs | 438→442 | cresce ao vivo; restore capturou +4 (consistência MVCC) |
| mobile_* (devices/orders/mutations/cash/batches) | **0** | nenhum APK pareou contra esta VM |

## Por que os 3 volumes não-pg são parte da baseline (não opcional)

- `uploads-data` — arquivos servidos por `/files/...`. `pg_dump` não cobre. Sem isso, restore perde imagens/anexos.
- `dataprotection-web-keys` / `dataprotection-admin-keys` — chaves de DataProtection do ASP.NET. Sem isso, restore em ambiente novo **invalida todos os cookies de auth assinados** (Web + Admin) e antiforgery tokens → logout geral.

## Validação do restore (executada 2026-06-25)

```
postgres:17-alpine efêmero no WSL → pg_restore --no-owner --no-acl → contagens batem com a origem
empresas=2 lojas=3 usuarios=6 produtos=23 pedidos=8 cardapio_item=19 entity_alteracoes=178 audit_logs=442
```

Backup não-testado = backup inexistente. Este foi testado.

## Procedimento de restore (em emergência)

```bash
# 1. extrair
cd C:\easy\snapshots\baseline-20260625-013436
tar -xf easystok-baseline-20260625-013436.tar

# 2. conferir integridade
sha256sum -c SHA256SUMS   # (ajustar paths se necessário)

# 3. restaurar Postgres num DB limpo
docker run -d --name es-restore -e POSTGRES_PASSWORD=x postgres:17-alpine
docker cp pg.dump es-restore:/tmp/pg.dump
docker exec es-restore createdb -U postgres easystock
docker exec es-restore pg_restore -U postgres -d easystock --no-owner --no-acl /tmp/pg.dump

# 4. restaurar volumes não-pg (descompactar zstd → tar → volume)
for V in uploads-data dataprotection-admin-keys dataprotection-web-keys; do
  zstd -d easystok_$V.tar.zst -o easystok_$V.tar
  docker run --rm -v easystok_$V:/data -v "$PWD":/in alpine sh -c "cd /data && tar -xf /in/easystok_$V.tar"
done
```

## PENDENTE (ação do Felipe)

- [ ] Cópia cloud pessoal: copiar `C:\easy\snapshots\baseline-20260625-013436\` para OneDrive/cloud **pessoal** (não Avanade). OneDrive pessoal não está sincronizado nesta máquina (folder `~/OneDrive` existe mas vazio; env aponta só p/ Avanade).

## Notas de segurança (tenant)

- Exfil usou SSH efêmero com revogação imediata — **nenhuma chave persistente** deixada na VM (confirmado: `authorized_keys` voltou só com a `ssh-rsa` original do Felipe).
- Nenhum storage persistente criado no tenant Avanade `cf36141c` (decisão travada).
