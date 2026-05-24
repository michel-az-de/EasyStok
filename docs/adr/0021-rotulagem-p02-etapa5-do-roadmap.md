# ADR-0021 — Rotulagem P-02 como Etapa 5 do ROADMAP (Caixa Conciliado V2 diferido)

**Status:** Aceito
**Data:** 2026-05-24
**Autores:** Felipe Azevedo
**Task ETK:** [ETK-0005](../tasks/done/ETK-0005-decisao-modulo-novo.yaml)

## Contexto

`CLAUDE.md` §6 Etapa 5 do ROADMAP listava: *"Modulo novo (Caixa Conciliado V2 OU Rotulagem P-02). Decisao pendente, depende de validacao premissas."*

Ambos os módulos têm:

- **Plano consolidado pronto.** Caixa V2 em [`docs/plan/00..08-*.md`](../plan/) (9 documentos); Rotulagem P-02 em [`docs/plan/p-02-rotulagem-nutricional.md`](../plan/p-02-rotulagem-nutricional.md) (1 documento gigante).
- **ADRs preparatórios.** Caixa V2: [0014](0014-pagamento-aditivo-em-pedido-pagamento.md), [0015](0015-sessao-caixa-como-entidade-explicita.md), [0016](0016-fechamento-hash-retencao-cinco-anos.md). Rotulagem P-02: [0011](0011-nomenclatura-pt-br-rotulagem.md), [0012](0012-backup-rotulos-storage.md), [0017](0017-comprovante-aprovacao-interna-rt.md).
- **Mesmo cliente piloto:** Casa da Babá (massas frescas artesanais; operadora Thatiane).
- **Tasks ETK materializadas no bootstrap** ([ADR-0020](0020-tdd-tasks-numeradas-multitarefa.md)): 7 tasks de Caixa (ETK-0006 a ETK-0012, todas P1) + 1 architecture test (ETK-0023); 2 tasks de Rotulagem (ETK-0016, ETK-0017, ambas P2).

A pendência era a escolha estratégica entre dois perfis distintos de módulo, não a falta de plano técnico.

## Decisão

**Etapa 5 = Rotulagem P-02.** Caixa Conciliado V2 é diferido para Etapa 6+ (sem cronograma fixo).

## Critérios da decisão

Validados em sessão Felipe ↔ Claude em 2026-05-24:

| Dimensão | Caixa Conciliado V2 | **Rotulagem P-02 (vencedora)** |
|---|---|---|
| Dor real hoje na Casa da Babá | Thatiane fecha caixa manualmente; trilha fiscal frágil para extrato de 2022. Operacional, diária. | **Rótulos irregulares circulando = risco multa Anvisa R$6k-1,5M por unidade comercializada.** Regulatória, latente, alta severidade. |
| Objetivo estratégico EasyStok | Retention do cliente atual ("feijão-com-arroz" ERP). | **Diferencial competitivo defensável (IA extrai ficha + Anvisa compliance) vendável a qualquer cliente Alimentos.** |
| Esforço part-time | 5-8 semanas | 10-12 semanas |
| Bloqueadores externos | Contador (1 reunião) + treino Thatiane | Contador (F0 + F6) + política privacidade do QR público |
| Bloqueadores destraváveis em paralelo com F0? | Sim | **Sim** (1-2 itens, sem bloquear início) |
| Complexidade técnica nova | Média (state machine + hash + PDF) | Alta (LLM, validador RDC, snapshot imutável, RT) |

Critérios decisivos: **dor regulatória ativa** + **diferencial competitivo defensável** + **bloqueadores contornáveis em paralelo**.

## Trade-offs explícitos

**Positivas:**

- Casa da Babá protegida do risco fiscal mais crítico (multa Anvisa por unidade comercializada, não por SKU)
- EasyStok ganha narrativa de venda concreta para o segmento Alimentos (gatilho regulatório vendável)
- Reuso de infra já feita: `ClaudeHttpBase` (extractor IA), `QuestPDF` (renderer rótulo), `TenantFeatureFlag` (gate do módulo), Resend (e-mails contador/RT), RLS Postgres (defesa por tenant)
- Aproveita o refactor IA já feito em worktree `feat/claude-extractor-refactor` (commit `5e0aa622`)
- Foco arquitetural único nos próximos 2-3 meses — não pulveriza atenção

**Negativas:**

- Caixa V2 fica em backlog. Casa da Babá continua fechando caixa manualmente. Mitigação: planilha funciona; risco fiscal de extrato de 2022 fica em standby — não piora.
- Maior complexidade técnica (LLM extraction, validador RDC com regras Anvisa, hash de aprovação RT)
- Bloqueadores externos exigem coordenação humana (contador + política privacidade) antes de F6
- 10-12 semanas part-time = ~1 trimestre comprometido com 1 módulo

## Tasks ETK afetadas

**Sobem para P1** (foco da Etapa 5):

- [ETK-0016](../tasks/backlog/ETK-0016-entity-rotulo-nutricional.yaml) — Entity RotuloNutricional + tests. **P2 → P1**
- [ETK-0017](../tasks/backlog/ETK-0017-usecase-gerar-rotulo.yaml) — Use case GerarRotuloNutricional. **P2 → P1**

**Diferidas (permanecem P1 no backlog, sem trabalho ativo)**:

- ETK-0006 a ETK-0012 (7 tasks Caixa) + ETK-0023 (architecture tests Caixa) — retomáveis em Etapa 6+

**Materialização adicional**: Felipe escolheu execução minimalista nesta sessão — ETK-0016/0017 já cobrem a entrada. Novas tasks Rotulagem (F0 setup, PerfilNutricional, etc.) serão materializadas conforme avança, a partir da próxima sessão.

## Bloqueadores externos a destravar em paralelo

Felipe destrava enquanto F0 começa:

1. **Contador da Casa da Babá** — 1 reunião em F0 (validar layout PDF) + 1 revisão em F6 (PDF real). Bloqueia F6 se não resolvido. Não bloqueia F0.
2. **Política de privacidade / QR público** — slug opaco reservado no MVP; página renderizada em F+1. Conteúdo da página (empresa + data + hash + nome operador) precisa OK legal antes de F+1, não antes de F0.

## Consequências

**Becomes easier:**

- Foco arquitetural único nos próximos 2-3 meses (sem pulverização Caixa/Rotulagem)
- Backlog Rotulagem desbloqueado (ETK-0016/0017 são portas de entrada P1)
- Narrativa comercial concreta para próximos clientes EasyStok do segmento Alimentos (LGPD multa + Anvisa multa = gatilho de venda)

**Becomes harder:**

- Estabilização operacional do Caixa fica em standby
- Risco fiscal de extrato 2022 da Casa da Babá não resolve nesta etapa
- Mais ~1 trimestre antes da próxima reavaliação Caixa V2

**To revisit:**

- Após F2 da Rotulagem (modelo de dados + migrations aplicadas), reavaliar se Caixa V2 pode entrar em paralelo
- Se Felipe receber cliente Alimentos novo antes de F5, reavaliar cronograma Rotulagem para entregar valor parcial mais cedo
- Se Casa da Babá tiver incidente fiscal de caixa antes de F4, reavaliar urgência relativa

## Reversão

Para reverter (escolher Caixa V2 em vez de Rotulagem):

1. Reescrever este ADR com decisão oposta
2. Reverter prioridade ETK-0016/0017 (P1 → P2)
3. Re-priorizar ETK-0006 a ETK-0012 conforme novo cronograma
4. Reverter mudança em `CLAUDE.md` §6

Custo: 30min. Reversão fácil **antes** de F2 (migrations não aplicadas, nenhum código de domínio escrito).

**Não recomendado depois de F2** (migrations aplicadas em staging/produção dificultam retorno).

## Caminho futuro

- **Hoje (2026-05-24):** ETK-0005 → done. Handoff escrito. `CLAUDE.md` §5 §6 atualizados. ETK-0016/0017 sobem para P1.
- **Próxima sessão:** claim ETK-0016 → TDD red/green/refactor (Entity RotuloNutricional).
- **Após F2 Rotulagem (~6 semanas):** avaliar paralelismo Caixa V2.
- **Trigger externo (cliente Alimentos novo):** revisar cronograma Rotulagem para priorizar caminho até MVP.

## Referências

- [ETK-0005](../tasks/done/ETK-0005-decisao-modulo-novo.yaml) — task que materializa esta decisão
- [docs/plan/p-02-rotulagem-nutricional.md](../plan/p-02-rotulagem-nutricional.md) — plano completo Rotulagem
- [docs/plan/README.md](../plan/README.md) — plano consolidado Caixa V2 (diferido)
- [ADR-0011](0011-nomenclatura-pt-br-rotulagem.md) — nomenclatura PT-BR (regra herdada)
- [ADR-0012](0012-backup-rotulos-storage.md) — storage + backup de rótulos
- [ADR-0017](0017-comprovante-aprovacao-interna-rt.md) — hash de aprovação RT
- [ADR-0020](0020-tdd-tasks-numeradas-multitarefa.md) — sistema de tasks ETK-NNNN + TDD obrigatório
- [ADRs diferidos com Caixa V2]: [0014](0014-pagamento-aditivo-em-pedido-pagamento.md), [0015](0015-sessao-caixa-como-entidade-explicita.md), [0016](0016-fechamento-hash-retencao-cinco-anos.md)
