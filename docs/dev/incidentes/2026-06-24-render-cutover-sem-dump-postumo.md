# Incidente — Render descomissionado sem dump póstumo (perda formalizada)

**Data:** 2026-06-24 (formalizado 2026-06-25)
**Severidade:** média (perda de dados histórica aceita, sem impacto na operação atual)
**Status:** fechado — perda formalizada, irrecuperável

## Resumo

O ambiente Render (`easystok-*.onrender.com`), que foi produção até a virada para a VM Azure (2026-06-06), está **desligado**. Não há dump póstumo do Postgres do Render. Dados que viviam **só server-side** no Render foram perdidos.

## Medições

- `curl https://easystok-api.onrender.com/health` → **404**
- `curl https://easystok-web.onrender.com/` → **503**
- `curl https://easystok-admin.onrender.com/` → **503**
- Busca por dump em todos os destinos conhecidos: **NENHUM ARTEFATO**
  - `rclone` não instalado localmente; sem R2 configurado
  - OneDrive Avanade sem `EasyStok/snapshots`
  - Repo sem `*.dump`/`*.sql.gz`
  - `C:\easy\snapshots\` não existia antes de 2026-06-25
- Dashboard Render: Felipe confirmou **desligado** (sem acesso ao Postgres para dump póstumo).

## Como o cutover Render→VM aconteceu (medido)

Commit `357ba507` (2026-06-06, `ci(deploy): script idempotente de deploy da VM Azure`) **só criou o `vm-deploy.sh`** — zero import/restore/seed-from-render. A VM **começou com banco vazio** e populou por:
1. Seed demo (opt-in, `SEED_DEMO_DATA`, default off)
2. Uso direto via Web/Admin contra a VM

**Não houve transferência server→server de Render para VM.** Confirmado por medição: `mobile_*` = 0 em todas as tabelas na VM (nenhum device PWA jamais sincronizou contra a VM — o APK pareava no Fly, também morto).

## O que foi perdido (server-side-only do Render)

Dados que existiam apenas no Postgres do Render e nunca tocaram um device nem foram recriados na VM:
- Relatórios gerados / exports
- Auditorias / logs históricos pré-cutover
- NF-e emitidas no período Render (se houver)
- Eventos de notificação processados
- Pagamentos Pix processados que não geraram entidade persistida na VM

## O que NÃO foi perdido

- Operação atual da VM (2 empresas, 3 lojas, 23 produtos, 8 pedidos, 178 entity_alteracoes, 438 audit_logs em 2026-06-25) — íntegra, agora com backup (`docs/runbook/vm-baseline-snapshot-2026-06-25.md`).
- Dados que tocaram device PWA: na fila IndexedDB local do device; ressincronizam quando o APK reparear contra a VM (idempotência via `mobile_processed_mutations` protege contra duplicação).

## Lições

1. **Cutover sem reconciliação = perda silenciosa.** Migração de ambiente deve dumpar o antigo ANTES de desligar e reconciliar contagens. Não foi feito no cutover Render→VM.
2. **Backup desde o dia zero.** A VM rodou ~19 dias (06→25/jun) como cópia única sem backup. Corrigido: baseline manual + hook pré-deploy (`vm-deploy.sh`) + automação recorrente pendente (F1).
3. **Um ambiente "antigo" vivo é uma janela que fecha.** Render reteve o DB por um tempo após desligar o app; a janela de dump póstumo passou despercebida.
