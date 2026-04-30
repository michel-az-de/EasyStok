# Mobile Module — Runbook de Deploy

Guia para subir o módulo Mobile (Casa da Baba PWA) no EasyStock em produção.

## 1. Pré-requisitos

- **PostgreSQL** acessível pela API. SQLite local funciona pra dev mas o
  `MobileSchemaInitializer` só roda em PG (SQL é Postgres-specific).
- ASP.NET Core 9 + Capacitor 6 (já no repo).
- Para APK: Android SDK + JDK 17+ (script `build-wsl.sh` cuida do rest).

## 2. Deploy do backend

### 2.1 Migrations
- EF migrations padrão rodam normal (`dotnet ef database update`).
- Schema mobile_* é **separado** — aplicado pelo `MobileSchemaInitializer`
  no startup, em ordem alfabética dos arquivos `Mobile/Schema/NNN_*.sql`.
- Os 6 arquivos atuais (`001` a `006`) são idempotentes (`IF NOT EXISTS`).

### 2.2 Configuração — `appsettings.json` (ou ENV)

```json
{
  "Mobile": {
    "ApiKey": "",
    "RequireApiKey": false
  }
}
```

| Campo | Onda | Quando flippar |
|---|---|---|
| `Mobile:RequireApiKey` | 1 | `false` no primeiro deploy (aceita anônimo + pareado, transição segura). Vire `true` depois que TODOS os APKs em campo estiverem pareados via `/dispositivos`. |
| `Mobile:ApiKey` | legacy | Não usado no fluxo atual (era chave única global, substituída por per-device api_key). Pode ficar vazio. |

### 2.3 CORS

PWA é servido pela própria API em `/pwa/`. Não precisa configurar CORS
extra para o app rodar. Se for hospedar PWA em domínio separado, adicione
a origem em `Cors:AllowedOrigins` (já existente).

### 2.4 Capacidades necessárias

- Postgres com `CREATE TABLE IF NOT EXISTS` e `ADD COLUMN IF NOT EXISTS`
  (PG 9.6+). Pra produção use 13+.
- `now() AT TIME ZONE 'utc'` é função padrão.
- Não precisa de extensions especiais.

## 3. Endpoints novos do módulo

### Mobile-facing (chamados pelo PWA)

| Método | Rota | Auth | Onda |
|---|---|---|---|
| `GET` | `/api/mobile/version` | Anônimo (rate-limit 30/min/IP) | 0 |
| `POST` | `/api/mobile/diagnostics/errors` | Anônimo (rate-limit) | 0 |
| `POST` | `/api/mobile/devices/pair` | Anônimo (rate-limit) | 1 |
| `POST` | `/api/mobile/sync` | `[MobileApiKey]` | 1 |
| `GET` | `/api/mobile/sync/pull` | `[MobileApiKey]` | 1 |
| `GET` | `/api/mobile/devices/me/lojas-disponiveis` | `[MobileApiKey]` | 6 |
| `POST` | `/api/mobile/devices/me/switch-loja` | `[MobileApiKey]` | 6 |
| `POST` | `/api/mobile/devices/me/backup` | `[MobileApiKey]` | 8 |
| `GET` | `/api/mobile/operation/pending-commands` | `[MobileApiKey]` | 4 |
| `GET` | `/api/mobile/operation/stream` | apiKey query | 5 |

### Web-facing (Authorize via JWT do EasyStock)

| Método | Rota | Função | Onda |
|---|---|---|---|
| `POST` | `/api/mobile/devices/pair-codes` | Gera código de 6 dígitos | 1 |
| `GET` | `/api/mobile/devices?empresaId=` | Lista devices | 1 |
| `DELETE` | `/api/mobile/devices/{id}` | Revoga | 1 |
| `POST` | `/api/mobile/devices/{id}/commands` | Envia comando remoto | 4 |
| `GET` | `/api/mobile/devices/{id}/backups` | Lista backups | 8 |
| `GET` | `/api/mobile/devices/{id}/backups/{backupId}` | Baixa JSON | 8 |
| `GET` | `/api/mobile/products?empresaId=` | Lista produtos custom | 2 |
| `POST` | `/api/mobile/products/{id}/approve` | Aprova mobile-only | 2 |
| `POST` | `/api/mobile/products/{id}/link` | Linka a Produto ERP | 2 |
| `POST` | `/api/mobile/products/{id}/unlink` | Desfaz | 2 |
| `POST` | `/api/mobile/products/{id}/reconcile-stock` | Força sync stock | 2.2 |
| `GET` | `/api/mobile/stock/divergences` | Lista divergências | 2.2 |
| `GET` | `/api/mobile/operation/dashboard` | KPIs ao vivo | 4 |
| `GET` | `/api/mobile/operation/devices-health` | Saúde por device | 7 |

### Páginas Web (Razor)

- `/dispositivos` — lista, parear, revogar, ver saúde, abrir backups
- `/dispositivos/{id}/backups` — listagem + download JSON
- `/produtos-mobile` — aprovar/linkar produtos custom
- `/produtos-mobile/divergencias` — corrigir divergências de estoque
- `/operacao` — dashboard ao vivo + comandos remotos

## 4. Roteiro de go-live

### Passo 1 — Subir backend
```
dotnet publish -c Release -o publish/
# deploy
```

Verificar logs:
```
[MobileSchemaInitializer] Mobile schema aplicado: 001_CreateMobileSchema.sql
[MobileSchemaInitializer] Mobile schema aplicado: 002_AddMobileDevicesAndTenancy.sql
[MobileSchemaInitializer] Mobile schema aplicado: 003_AddProductErpLink.sql
[MobileSchemaInitializer] Mobile schema aplicado: 004_AddOrderErpVendaLink.sql
[MobileSchemaInitializer] Mobile schema aplicado: 005_AddDeviceCommands.sql
[MobileSchemaInitializer] Mobile schema aplicado: 006_AddDeviceBackups.sql
```

Smoke test rápido:
```bash
curl https://API/api/mobile/version
# deve retornar 200 com mobileSchemaVersion=2 e features.pairing=true
```

### Passo 2 — Distribuir APK

`builds/easy-stock-mobile.apk` — instale no celular do operador:
- ADB: `adb install -r easy-stock-mobile.apk`
- Manual: copiar pro celular + abrir o arquivo.

### Passo 3 — Parear cada dispositivo

1. Gestor abre `/dispositivos` no painel web → "Parear novo dispositivo".
2. Preenche label (ex: "iPhone Cozinha") + operador padrão.
3. Recebe código de 6 dígitos (válido 10 min).
4. Operador no app: Ajustes → Diagnóstico → "Parear dispositivo" → digita o código.
5. App passa a sincronizar com API key per-device.

### Passo 4 — Aprovar produtos custom

Se o app já tem produtos criados no campo (`IsCustom=true`):
1. Gestor abre `/produtos-mobile`.
2. Cada produto pendente: clica "Linkar ao ERP" e escolhe o `Produto` correspondente, OU "Aprovar (mobile-only)".

### Passo 5 — Verificar `/operacao`

Dashboard live deve mostrar:
- Vendas do dia
- Saldo de caixa
- Pedidos abertos
- Devices ativos (verde = LastSeen <30min)

Se algum device aparece "warn" / "err", clica em `/dispositivos` e verifica.

### Passo 6 — Quando todos pareados, flippar `RequireApiKey=true`

```json
"Mobile": { "RequireApiKey": true }
```

Reinicia API. Requests anônimos pra `/sync` passam a ser rejeitados (401).
Apps pareados continuam funcionando normalmente.

## 5. Operação contínua

### Backup automático
- App envia snapshot do localStorage 1×/24h pro servidor.
- Mantém os 7 mais recentes por device (rotação FIFO).
- Acesso: `/dispositivos/{id}/backups`.

### Realtime
- App conecta SSE em `/operation/stream`.
- Quando outro device da mesma loja sincroniza, recebe push e faz pull imediato.
- Polling 30s continua como fallback.

### Conflict detection
- Se 2 devices editam o mesmo recurso, server detecta via `UpdatedAt` e
  rejeita o que tem timestamp mais antigo.
- App mostra modal explicativo e força pull.

### Comandos remotos
- Em `/operacao` ou `/dispositivos`, gestor pode enfileirar:
  - `flush_now`: força app a enviar mutations pendentes
  - `pull_now`: força app a buscar atualizações
  - `reload`: reinicia o app
  - `message`: notifica operador via toast
- Comando expira em 24h se device não buscar.

## 6. Troubleshooting

### "no such table: mobile_devices" em SQLite local
**Causa**: `MobileSchemaInitializer` só roda em PostgreSQL. SQLite é só
fallback dev sem suporte ao módulo mobile. Use Postgres em produção.

### Device aparece "inativo há X horas"
**Causa**: app não consegue sincronizar. Verifica:
- Pareamento ainda válido? (`Diagnostico → Servidor → Pareamento`)
- Rede do celular OK?
- Servidor acessível? (`curl https://API/api/mobile/version`)

### Conflito constante entre 2 devices
- Veja `/produtos-mobile/divergencias` se for stock.
- Se for outro recurso, considere ajustar `LastDeviceId` manualmente no
  banco pra forçar reconciliação.

### Comando remoto não chega no app
- Verifica que device está pareado e LastSeen recente.
- Comando pode estar expirado (24h TTL). Re-enfileira.
- Cheque se SSE está conectado em `Diagnostico → Tempo real`.

## 7. Checklist final

- [ ] Postgres acessível e migrations rodaram
- [ ] `Mobile/Schema/*.sql` aplicados (logs)
- [ ] `/api/mobile/version` responde 200
- [ ] Gestor consegue gerar pairing code em `/dispositivos`
- [ ] App pareia com código (`apiKey` salva em `cdb-pairing` localStorage)
- [ ] App envia mutation → aparece em `mobile_orders` no DB
- [ ] Outro device da mesma loja recebe pull em <2s (SSE)
- [ ] Pedido entregue cria `Venda` no ERP (analytics atualizado)
- [ ] Backup automático envia em <24h da primeira sync
- [ ] `/operacao` mostra dashboard com dados reais
- [ ] Após 1-2 semanas estável, flippar `RequireApiKey=true`

---

**Última atualização:** Onda 8 (commit `f8b9fa7`)  
**Versão schema mobile:** 2 (apiKeyEnforced false, pairing true)  
**APK validado:** 67 testes passando, sha256 match em build pipeline.
