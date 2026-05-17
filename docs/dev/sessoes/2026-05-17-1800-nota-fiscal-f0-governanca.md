# Sessao nota-fiscal-f0-governanca

Data: 2026-05-17 18:00
Worktree: C:\easy\EasyStok\.claude\worktrees\vibrant-khorana-5141bb
Branch: dev/vibrant-khorana-5141bb
Identidade Git: felipe.azevedo@gmail.com / michel-az-de
Status final: completo

## O que foi feito

- Inventario completo do codigo NFe existente em master (60+ arquivos em Domain/Application/Infra/Api/Tests)
- Criado ADR-0018 (nomenclatura Nfe* em codigo vs Nota Fiscal em UI/REST) — formaliza excecao a ADR-0011
- Criado plano docs/plan/nota-fiscal/00-README.md (indice + visao geral + cronograma F0-F5)
- Atualizado CLAUDE.md item 5 (removido bullet "PR #99 pendente", adicionado referencia ADR-0018 + plano)
- Fechado PR #99 como superseded (comment aponta ADR-0018)
- Plano operacional detalhado em ~/.claude/plans/debug-ux-copy-analise-composed-pond.md (12 secoes)

## O que ficou pendente

- F1: Backend gaps (ListarNotasFiscais UseCase, DANFE proxy, CSC handling, observabilidade, FiscalArchitectureTests)
- F2: Wizard Admin (3 Razor Pages: cert -> CSC -> serie/ambiente -> Habilitar)
- F3: Web ERP (listagem + detalhe + cancelamento + DANFE)
- F4: Hardening (E2E WireMock, concorrencia, snapshots Verify)
- F5: Soak + go-live Casa da Baba (bloqueado ate credenciais: CSC, cert A1, token FocusNFe)
- Stubs docs/plan/nota-fiscal/01-07 ainda nao criados (cada fase preenche o seu)
- ArchTest pre-existente falhando: Exceptions_De_Domain_Devem_Ficar_No_Domain (nao relacionado a Fiscal)

## Decisoes tomadas

- Nfe* em codigo (ADR-0018 Aceito) — zero refactor de naming
- NFC-e modelo 65 apenas nesta rodada (modelo 55 e PWA ficam F+1/F+2)
- PR #99 fechado (superseded por a1c27e28 + ADR-0018)
- TDD-first obrigatorio em toda nova superficie
- Principio aditivo em migrations (ADD COLUMN NULL, sem drop/rename)
- Go-live em sabado de manha (padrao caixa-conciliado)

## Commits criados

- db04dcf7: docs(fiscal): ADR-0018 nomenclatura Nfe* + plano NFC-e modelo 65

## Branches criadas/deletadas

- Operando em branch existente: dev/vibrant-khorana-5141bb
- Nenhuma branch criada ou deletada

## Proxima acao recomendada

1. Proxima sessao: abrir F1 (backend gaps) — comecar por ListarNotasFiscaisUseCase (TDD)
2. Felipe providenciar em paralelo: CSC + cert A1 + token FocusNFe sandbox
3. Considerar catalogar Exceptions_De_Domain_Devem_Ficar_No_Domain em flaky-tests.md

## Referencias

- Plano: docs/plan/nota-fiscal/00-README.md
- ADRs: docs/adr/0018-nomenclatura-nfe-prefixo-curto.md
- Incidentes: docs/dev/incidentes/2026-05-16-agentes-paralelos-trabalho-paralelo.md
- Plano operacional: ~/.claude/plans/debug-ux-copy-analise-composed-pond.md
- PR #99 (fechado): https://github.com/michel-az-de/EasyStok/pull/99
