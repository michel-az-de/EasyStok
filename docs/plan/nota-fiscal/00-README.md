# Plano — Nota Fiscal Eletrônica (NFC-e modelo 65)

> Avanço da feature **Nota Fiscal Eletrônica** no EasyStok, com foco em entregar **UI Web ERP + UI Admin** sobre a fundação backend `Nfe*` já existente em master. Cliente piloto: **Casa da Babá**. Escopo: apenas **NFC-e modelo 65** (varejo direto consumidor). NF-e modelo 55 (B2B) e PWA NFC-e ficam fora desta rodada.

## Índice

| # | Arquivo | Conteúdo |
|---|---|---|
| 00 | [00-README.md](00-README.md) | **(este)** Visão geral, decisões críticas, cronograma, premissas |
| 01 | [01-inventario.md](01-inventario.md) | O que já existe em master (Domain, Application, Infra, API, Tests, UI=zero) |
| 02 | [02-arquitetura.md](02-arquitetura.md) | Decisões arquiteturais herdadas (state machine, RLS, idempotência, contingência, certificado A1) + novas |
| 03 | [03-api.md](03-api.md) | Endpoints HTTP novos (`GET /api/notas-fiscais`, DANFE proxy, CSC, série/ambiente, habilitar/desabilitar) |
| 04 | [04-ux-copy.md](04-ux-copy.md) | Glossário PT-BR, microcopy (labels, erros, empty states), status badges, wizard Admin |
| 05 | [05-migracao.md](05-migracao.md) | Migration aditiva CSC + validação SQL pós-migração |
| 06 | [06-testes.md](06-testes.md) | TDD-first: unit, integration, snapshot (Verify), concorrência, E2E sandbox |
| 07 | [07-faseamento.md](07-faseamento.md) | F0-F5, critérios de pronto, cronograma realista, marcos com Casa da Babá |
| 08 | [08-riscos.md](08-riscos.md) | 10 riscos com prob/impacto/mitigação/gatilho, decisões abertas |

> O plano operacional vivo (em iteração nesta sessão) está em `~/.claude/plans/debug-ux-copy-analise-composed-pond.md` e é absorvido para os arquivos acima conforme cada fase fecha.

## Contexto

A fundação backend de NF foi construída em master (commit `a1c27e28` em 2026-05-16 + estabilização posterior documentada no incidente `2026-05-16-agentes-paralelos-trabalho-paralelo.md`). Hoje o sistema:
- **Emite** NFC-e via FocusNFe (sandbox/produção)
- **Cancela** NFCes dentro do prazo SEFAZ (24h)
- **Inutiliza** faixas de numeração
- **Consulta** status na SEFAZ
- **Recebe webhooks** validados HMAC do FocusNFe
- **Reprocessa contingência** via job batch (FalhaTransiente até 24h)
- **Gerencia certificado A1** cifrado via Data Protection (KEK rotacionável)
- **Reporta** 5 relatórios fiscais (LivroSaidas, MapMensal, Totalizadores, Cancelamentos, XmlBulkDownload)

**Lacuna crítica**: nenhuma das três superfícies de UI (Web ERP, Admin, PWA mobile) tem fluxo funcional para o operador interagir com NF. Web ERP tem só placeholder textual (`SaidaFormViewModel.NotaFiscal`); Admin tem zero pages de configuração fiscal; PWA tem só CSS de DANFE.

Resultado prático: **Casa da Babá não consegue configurar a empresa nem emitir NFC-e via UI** — só por curl direto na API. Sem isso, o sistema perde contra "voltar pra planilha" no primeiro fiscal sério.

## Sumário Executivo

Esta rodada entrega:
- **Wizard Admin** (3 passos: certificado A1 → CSC → série+ambiente) — habilita empresa para emitir
- **Listagem + detalhe + cancelamento + DANFE no Web ERP** — operador gerencia notas emitidas
- **Backend gaps menores** (listagem com filtros, proxy DANFE, CSC handling em `EmpresaConfiguracaoFiscal`, métricas, health check, ArchitectureTests)
- **Hardening** (testes E2E sandbox FocusNFe, snapshot PayloadMapper, concorrência numeração)
- **Soak + go-live piloto** em produção real na Casa da Babá

**Não inclui** (vão para F+1/F+2):
- PWA mobile (emissão NFC-e no PDV)
- NF-e modelo 55 (B2B)
- Carta de Correção (CC-e)
- EPEC (contingência síncrona)
- Pagamentos múltiplos por NFC-e (`NotaFiscalPagamento` do PR #99)
- Reforma Tributária (IBS/CBS/IS NT 2025.002)

## Top 5 Decisões Críticas

1. **Manter nomenclatura `Nfe*` no código** (zero refactor de naming) — [ADR-0018](../../adr/0018-nomenclatura-nfe-prefixo-curto.md). PR #99 (que tinha `NotaFiscal*`) é fechado como superseded.
2. **TDD-first em toda nova superfície** (UI, controller, use case) — teste vermelho antes do código, cobertura ≥80% em arquivos novos.
3. **Princípio aditivo na migration de CSC** — `ADD COLUMN ... NULL` em `empresa_configuracao_fiscal`, sem rename, sem drop. Backfill via job idempotente.
4. **Manter `Nfe*` em código + "Nota Fiscal" em UI PT-BR + "notas-fiscais" em rotas REST** (ADR-0018). Microcopy consistente — vide [04-ux-copy.md](04-ux-copy.md).
5. **Go-live em SÁBADO DE MANHÃ** — máxima janela de suporte do Felipe à frente, com endpoint emergência `desabilitar-emergencia` para Thatiane acionar fora da janela.

## Cronograma

Esforço: 56-78h (Felipe solo, 2-3h/dia útil + 4-6h fins). Calendar: ~3-4 semanas. Detalhe em [07-faseamento.md](07-faseamento.md).

| Fase | Esforço | Marcos |
|---|---|---|
| F0 | 4-6h | ADR-0018 + PR #99 fechado + plano oficial |
| F1 | 8-12h | Backend gaps + observabilidade |
| F2 | 16-22h | Wizard Admin funcional |
| F3 | 18-24h | Web ERP NFs gerenciáveis |
| F4 | 10-14h | Testes E2E + snapshots |
| F5 | 5-7 dias | Soak staging + go-live Casa da Babá |

## Premissas que precisam validação

1. **CSC** da Casa da Babá: **pendente — Felipe vai providenciar em paralelo**
2. **Certificado A1** (.pfx + senha): **pendente — Felipe vai providenciar**
3. **Token FocusNFe sandbox**: **pendente — Felipe vai providenciar**
4. Working tree master dirty (2026-05-17): **confirmado pelo Felipe — é trabalho dele, ignorar nesta sessão**
5. PR #99 fechamento: **autorizado pelo Felipe — fechar em F0**

Sem premissas 1-3, F2 e F3 podem ser implementados com **dados de teste / mocks**. F4 usa **WireMock simulando FocusNFe**. F5 trava até credenciais reais.

## ADRs relacionados

- [ADR-0018](../../adr/0018-nomenclatura-nfe-prefixo-curto.md) — Nomenclatura `Nfe*` em código vs "Nota Fiscal" em UI (esta rodada)
- [ADR-0011](../../adr/0011-nomenclatura-pt-br-rotulagem.md) — PT-BR para substantivos de negócio (regra geral)
- [ADR-0013](../../adr/0013-cancellation-token-iusecase.md) — `CancellationToken` adiado em `IUseCase` (referência; refactor fica para próxima onda de Application Layer)

## Referências

- Incidente [2026-05-16-agentes-paralelos-trabalho-paralelo](../../dev/incidentes/2026-05-16-agentes-paralelos-trabalho-paralelo.md) — origem da dual implementação Nfe* vs NotaFiscal*
- [PR #99](https://github.com/michel-az-de/EasyStok/pull/99) (a fechar em F0 desta rodada)
- Padrão de plano: [docs/plan/README.md](../README.md) (caixa-conciliado-v2) — referência de estrutura

## Próximos passos

1. **F0 completo** nesta sessão (ADR-0018 ✓, este README ✓, CLAUDE.md update, gh pr close 99)
2. Próxima sessão abre F1: backend gaps + observabilidade (arquivos 01-06 acima ficam stub até a fase consumir cada um)
3. F2 quando credenciais 1-3 estiverem disponíveis OU com mocks/dados de teste se Felipe quiser destravar fluxo de UI antes
