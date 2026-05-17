# ADR 0012 — Backup e storage de rótulos publicados

**Status:** Accepted (2026-05-16)
**Contexto do plano:** P-02 (Módulo Incremental de Rotulagem Nutricional) — [docs/plan/p-02-rotulagem-nutricional.md](../plan/p-02-rotulagem-nutricional.md). Aborda os "3 furos reais" levantados em revisão: storage de PDFs/PNGs/snapshots, backup com retenção legal, processo de restore-test.

## Decisão

**Storage MVP**: Fly volume montado em `/data/rotulos/{empresaId}/{rotuloId}.{pdf|png|json}`. Migra para Cloudflare R2 quando volume passar 50GB (~150k rótulos publicados).

**Backup**: `pg_dump` diário do Postgres + snapshot incremental do Fly volume para **Cloudflare R2** (free tier 10GB + zero egress). Retenção mínima de **5 anos** (alinhada ao requisito de auditoria sanitária Anvisa).

**Cron**: GitHub Actions (mesmo provider do CI). Dois jobs:
- `SincronizacaoBackupRotuloJob` — diário às 03:00 BRT. `pg_dump` + sync incremental do volume.
- `TesteRestoreBackupRotuloJob` — mensal, dia 1 às 04:00 BRT. Restore em DB dev efêmero. Valida pelo menos 1 PDF lido + 1 snapshot JSON parseado. Falha → email crítico via Resend ao admin.

## Por quê

`Rotulo` é entidade **imutável** com PDF arquivado, snapshot JSON completo e referência ao lote produzido — material de auditoria sanitária com retenção legal. Sem política de backup + restore-test documentada, dois problemas:

1. **Perda silenciosa**: se o Fly volume falha (storage caro vs storage durável), a empresa perde 5 anos de rótulos e fica exposta a multa Anvisa de R$ 6k a R$ 1,5M por rótulo não-comprovável.
2. **Backup não-testado = backup inexistente**: dump diário que ninguém testa pode estar corrompido por semanas sem que se saiba. Restore-test mensal automatizado obriga validação.

**Por que Fly volume no MVP, não R2 desde já?**
- Casa da Babá (cliente principal hoje): 50 lotes/mês × 5 anos = 3000 PDFs. PDF médio 80KB + PNG 200KB + snapshot 8KB = ~300KB/rótulo. Total ≈ 900MB. Fly volume mínimo 1GB (US$ 0,15/GB/mês ≈ R$ 0,75/mês). Latência local de leitura.
- R2 entra quando volume passar 50GB (≈ 150k rótulos publicados) — sinal de sucesso de mercado, momento natural para migrar.
- Migração futura sem dor: storage é abstraído por `IRotuloBlobStorage` (interface no Application; impl Fly volume no MVP, impl R2 quando precisar).

**Por que Cloudflare R2, não S3/B2 para backup?**
- Free tier de 10GB suficiente para anos de backup (`pg_dump` comprimido + delta do volume).
- **Zero egress fee** — restore-test mensal não vira custo crescente.
- API S3-compatible — biblioteca AWS SDK existente reusa.

**Por que GitHub Actions para cron, não serviço terceiro?**
- Mesmo provider do CI já configurado em F0.5 (billing pago como parte do mesmo plano).
- Mesmo lugar de gerenciar secrets (`R2_ACCESS_KEY`, `R2_SECRET_KEY`, `RESEND_API_KEY`).
- Único dashboard de ops. Solo dev part-time não esquece onde olhar.

## Mudanças aplicadas

- Documento ADR criado.
- Especificação dos dois jobs incluída no plano P-02 (seção "Pontos de Atenção / Riscos", item 5).
- Pendente (F0.5):
  - Workflows GitHub Actions `.github/workflows/backup-rotulos.yml` e `.github/workflows/restore-test-rotulos.yml`.
  - Secrets `R2_ACCESS_KEY`, `R2_SECRET_KEY`, `RESEND_API_KEY` no GitHub repo (ação manual do Felipe).
  - Bucket R2 `easystok-rotulos-backup` criado no Cloudflare (ação manual do Felipe).
  - Conta Resend criada + domínio verificado (ação manual do Felipe).
  - Interface `IRotuloBlobStorage` + impl `FlyVolumeRotuloBlobStorage` em `EasyStock.Infra.*` (entregue em F7 junto com geração de rótulo).

## Custos estimados (Casa da Babá no ano 1)

| Item | Custo mensal |
|---|---|
| Fly volume 1GB | ~R$ 0,75 |
| GitHub Actions billing (CI + crons) | ~R$ 50-200 |
| Cloudflare R2 (free tier — backup < 10GB) | R$ 0,00 |
| Resend (free tier — < 3k emails/mês) | R$ 0,00 |
| **Total** | **~R$ 50-200/mês** |

Comparação: 1 multa Anvisa por rótulo irregular = R$ 6.000 mínimo. ROI do investimento de backup é trivial.

## Consequências

**Positivas:**
- Auditoria sanitária de 5 anos preservada com backup testado mensalmente.
- Custo trivial no MVP (provavelmente <R$ 60/mês total).
- Migração futura para R2 sem refator do código (abstração `IRotuloBlobStorage`).
- Restore-test automatizado pega corrupção de backup em até 30 dias (não em 5 anos).

**Negativas:**
- Dependência de 4 serviços externos no path crítico (Fly, GitHub, Cloudflare, Resend). Falha simultânea de 2+ deles deixa o sistema parcialmente cego.
- Restore-test mensal consome ~10 min de GitHub Actions runner (within free tier mas conta).

## Reversão

- Pausar workflows GitHub Actions de backup → backup para de rodar (não destrutivo).
- Backup local apenas (sem R2) é fallback, mas viola requisito de durabilidade externa.

## Caminho futuro

- Quando volume passar 50GB: migrar `IRotuloBlobStorage` para impl R2. Backup já está em R2.
- Avaliar Backblaze B2 como alternativa se Cloudflare mudar política do free tier.
- Considerar replicação cross-region quando empresa cliente passar de SP para múltiplas regiões.
- F+2: catálogo público com SLA — rever política se houver compromisso contratual de uptime do QR público.
