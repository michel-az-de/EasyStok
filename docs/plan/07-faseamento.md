# 07 — Faseamento e Cronograma

> Parte do [Plano](README.md). Anterior: [06-testes.md](06-testes.md). Próximo: [08-riscos.md](08-riscos.md).

### H.1 Fases entregáveis

Premissas:
- Felipe trabalha **2-3h/dia úteis** + **4-6h fins de semana** = ~14-20h/semana.
- Buffer 25% para imprevistos (memory: GitHub Actions parado por billing,
  Casa da Babá pode pedir bugfix urgente).
- Nenhum paralelismo entre fases (solo dev).
- Plano premissivo: Casa da Babá é o cliente piloto; fases têm marcos com
  ela.

#### F0 — Reconhecimento, ADRs e decisões fundacionais

**Duração**: 3 dias (10–15h)
**Conteúdo**:
- Plan (este documento) revisto pelo Felipe.
- ADR-0011: "Pagamento como expansão de PedidoPagamento (aditivo) vs nova
  entidade" — documenta decisão de manter tabela física e estender colunas.
- ADR-0012: "SessaoCaixa como entidade explícita (vs agregação de
  MovimentoCaixa)" — decisão e trade-offs.
- ADR-0013: "Hash + retenção 5 anos via EntityAlteracao" — decisão
  regulatória.
- Confirmar com contador (1 reunião curta).
- Criar branch `feat/caixa-conciliado-v2` localmente.

**Critério de pronto**: 3 ADRs commitados em `docs/adr/`, contador retornou
"OK" ou ajuste pequeno.

#### F1 — Schema + entidades + interceptors

**Duração**: 4 dias (15–20h)
**Conteúdo**:
- Criar entity classes: `SessaoCaixa` + estender `PedidoPagamento`,
  `MovimentoCaixa`, `FechamentoCaixa` no Domain.
- Migrations M01-M03 (sem index concurrent ainda — só desenvolvimento).
- EF configurations + DbSet adicionados.
- `SessaoCaixaStateMachine` + state machine de pagamento (estado financeiro
  computed).
- `FechamentoCaixaImutavelInterceptor` + testes unit.
- ArchitectureTests não regridem.
- Build limpa, todos os testes existentes passando.

**Critério de pronto**:
- `dotnet build && dotnet test` verde
- ArchitectureTests verde
- Migration M01-M03 aplica em DB de dev
- `EasyStock.Domain.Tests/SessaoCaixaStateMachineTests.cs` cobre 100% das
  transições
- `FechamentoCaixaImutavelInterceptorTests.cs` verde

**Bloqueadores**: nenhum óbvio.

#### F2 — Pagamento (Módulo B): UseCases + Controller + UI

**Duração**: 6 dias (25–30h)
**Conteúdo**:
- `ConfirmarPagamentoUseCase` (substitui `RegistrarPagamentoPedidoUseCase`
  internamente; controller endpoint continua mesmo path)
- `EstornarPagamentoUseCase` (novo)
- `ListarPagamentosPedidoUseCase` (querystring para incluir estornados)
- `CancelarPedidoUseCase` estendido com handler de estorno em cascata
- `PagamentosController` (estorno endpoint POST)
- Validations FluentValidation
- Idempotency middleware whitelist atualizada + body_hash check (M01 aplicada)
- PWA: refactor aba Pagamentos (modal novo, modal estorno, badges)
- Unit + integration tests (G.1.1–G.1.3, G.2.2)

**Critério de pronto**:
- Casa da Babá em ambiente de staging consegue: criar pedido, pagar parcial,
  estornar com motivo, ver badges corretos.
- 85% cobertura nos UseCases novos.
- 0 erros no NetArchTest.

**Bloqueadores**: PWA pode demandar trabalho de design (Felipe sem
designer dedicado). Buffer 20% extra na UI.

#### F3 — Backfill de pagamentos retroativos

**Duração**: 1 dia (3–5h)
**Conteúdo**:
- M04 (índices concurrent) + M05 (backfill) + M06 (constraints)
- Script SQL standalone de validação (F.5)
- Job de validação no Worker: roda 1x após startup pós-deploy, checa todas
  as 7 queries de validação, alerta em log se algo != 0.

**Critério de pronto**:
- Em DB de staging com cópia da Casa da Babá: 7 queries retornam 0.
- Backfill idempotente: rodar 2x não duplica nada.

**Bloqueadores**: tamanho real da tabela em prod — se Casa da Babá tem mais
de 100k linhas, precisa fragmentar batch (F.2.5).

#### F4 — SessaoCaixa: abertura, movimentos manuais, listagem

**Duração**: 4 dias (15–20h)
**Conteúdo**:
- `AbrirSessaoCaixaUseCase`, `RegistrarMovimentoManualUseCase`,
  `ListarSessoesCaixaUseCase` (histórico) — **todos NOVOS, sem tocar
  legacy**.
- `SessoesCaixaController` (endpoints D.2.1–D.2.4, D.2.8) — novo controller.
- `RegistrarPagamentoPedidoUseCase` (legacy) **continua intacto** —
  comportamento exato preservado.
- `ConfirmarPagamentoUseCase` (NOVO, em pasta separada
  `Application/UseCases/Pagamentos/`) — comportamento expandido: cria
  `SessaoCaixa` + `MovimentoCaixa` linkados.
- `PedidosController.AddPagamento` (POST /api/pedidos/{id}/pagamentos)
  **passa a fazer dispatch**:

  ```csharp
  // Pseudocódigo da decisão por flag
  var flagAtiva = await featureFlagSvc.IsEnabledAsync("CaixaConciliadoV2", currentUser.EmpresaId);
  if (flagAtiva)
      return await confirmarPagamentoUseCase.ExecuteAsync(cmd);
  else
      return await registrarPagamentoPedidoUseCase.ExecuteAsync(cmd); // legacy intacto
  ```

  Mesma rota HTTP, comportamento controlado por flag por empresa. UI também
  é controlada pela mesma flag → coerência total.
- **Reuso de `TenantFeatureFlag` existente** (D-01 resolvido —
  `EasyStock.Domain/Entities/TenantFeatureFlag.cs` + tabela `TenantFeatureFlags`
  da migration `20260430205554_AddGovernancaFeatures`).
- PWA: tela Caixa renovada (E.2) renderizada apenas quando flag ativa.
- Unit + integration tests para AMBOS os caminhos (legacy e novo).
- Backfill retroativo de SessaoCaixa (F.3) executado em staging.

**Critério de pronto**:
- Casa da Babá em staging com flag ON: abre caixa, faz pedido pago, vê
  movimento de caixa linkado.
- Empresa de teste com flag OFF: comportamento idêntico ao master pré-feature
  (assert: nenhuma `SessaoCaixa` nem `MovimentoCaixa` novo criado).
- Tela mobile (≤640px) testada manualmente em DevTools + 1 device real.
- Sessão recente (<10min) bloqueia fechamento.
- Teste de rollback explícito: ligar flag → fazer pagamento → ver
  SessaoCaixa criada → desligar flag → fazer outro pagamento → confirmar
  que NÃO criou SessaoCaixa nem MovimentoCaixa novo (apenas
  PedidoPagamento, fluxo legacy).

#### F5 — Conciliação automática via eventos (PagamentoConfirmado →
MovimentoCaixa)

**Duração**: 3 dias (10–15h)
**Conteúdo**:
- Eventos de domínio `PagamentoConfirmado`, `PagamentoEstornado`,
  `MovimentoManualRegistrado` (records).
- Handlers inline (já chamados dentro dos UseCases compostos).
- Persistência via outbox (`OutboxEventoIntegracao.Criar` linha por evento).
- Worker já existente (`IntegrationOutboxBackgroundService`) consome — apenas
  registrar tipos novos.
- Testes de race (G.4 primeiros 3 cenários).
- Advisory lock implementado em `EasyStockDbContext`.

**Critério de pronto**:
- Race tests verde.
- Em staging, criar 10 pagamentos paralelos via curl → soma == total
  esperado, sem excedente.

#### F6 — Wizard de fechamento + PDF

**Duração**: 7 dias (25–35h)
**Conteúdo**:
- `IniciarFechamentoSessaoUseCase` + `CancelarConferenciaUseCase` (operador
  cancela conferência).
- `ConfirmarFechamentoSessaoUseCase` (consolida snapshot + chama renderer).
- `FechamentoCaixaPdfRenderer` (QuestPDF, layout E.5).
- `VerificacaoCodigo` generator (16 chars URL-safe via
  `RandomNumberGenerator` + base62).
- `GerarPdfFechamentoUseCase` + cache no `IFileStorage`.
- `VerificarFechamentoPublicoController` (rota pública não-`/api/`).
- Cálculo de `HashSha256` (PDF bytes + snapshot canonical-JSON).
- PWA: wizard 3 passos (E.3), download PDF, opção email contador.
- Razor page de verificação pública (`/caixa/verificar/{codigo}`).
- Snapshot test do PDF + cor/contraste teste manual.

**Critério de pronto**:
- E2E manual (G.5) executado em staging.
- PDF aprovado pelo contador (F0 critério).
- Página pública renderiza com hash correto.

**Bloqueadores**:
- `QRCoder` pacote precisa adicionar (versão compatível com .NET 9).
- QuestPDF Community license auto-aceita (já em uso).

#### F7 — Hash, auditoria, retenção 5 anos

**Duração**: 2 dias (6–10h)
**Conteúdo**:
- Configurar `EntityAlteracaoRetentionService` para incluir
  `FechamentoCaixa`, `SessaoCaixa`, `PedidoPagamento` com retention 1825 dias.
- Teste manual: criar FechamentoCaixa → verifica que `EntityAlteracao` row
  foi criada com retention correto.
- Validar que hash do PDF é determinístico (mesmo conteúdo → mesmo hash).
- Documentar como verificar publicação (procedure para fiscal).

**Critério de pronto**:
- Query SQL: `SELECT COUNT(*) FROM entity_alteracoes WHERE entity_type = 'FechamentoCaixa'`
  > 0 após teste manual.
- Doc `docs/auditoria/como-verificar-fechamento.md` escrito.

#### F8 — Polimento, E2E, observabilidade, feature flag, go-live

**Duração**: 4 dias (15–20h)
**Conteúdo**:
- Feature flag `CaixaConciliadoV2` cabeada (reuso de `TenantFeatureFlag`).
- Logs estruturados em todos os UseCases novos (Serilog padrão do projeto).
- Métricas básicas (counter `pagamentos_confirmados_total`,
  `fechamentos_caixa_total`, histograma
  `fechamento_render_upload_seconds`).
- E2E manual completo (G.5).
- Validação contábil final (assinatura do contador).
- Banner in-app para Casa da Babá (7 dias antes).
- Deploy em staging por 5 dias para "soak test".
- Deploy em produção com flag OFF.
- **Liga flag em SÁBADO DE MANHÃ (não domingo 22h)** — máxima janela de
  suporte do Felipe à frente.

**Janela real de suporte do Felipe** (declarada honestamente — sem wishful
thinking):

| Horário | Disponibilidade | Como acionar |
|---|---|---|
| Seg-sex 09h-18h | **Avanade — INDISPONÍVEL** para suporte EasyStok | (não acionar) |
| Seg-sex 19h-22h | Disponível | WhatsApp do Felipe |
| Seg-sex 22h-09h | Indisponível (sono + tempo família) | Mensagem fica para 19h do dia útil seguinte |
| Sáb 09h-22h | Disponível | WhatsApp |
| Dom 09h-22h | Disponível | WhatsApp |
| Dom 22h-seg 19h | **Janela mais longa de indisponibilidade** (15h pior caso) | Mensagem fica para 19h de segunda |

**Por isso**: liga flag para Casa da Babá em sábado de manhã. Felipe
acompanha sábado inteiro (até 22h) + domingo inteiro. Se um problema
aparecer fora da janela:

- **Fluxo automático de fallback**: flag desligada → operadora volta ao
  fluxo legacy imediatamente, sem ação do Felipe. Casa da Babá não
  perde funcionalidade — perde só os ganhos do v2 nesse intervalo.
- **Mecanismo**: endpoint `POST /api/empresa/feature-flags/desligar-emergencia`
  (admin-only, idempotente) deixa Thatiane ou suporte primeiro-nível
  desativar autônomamente. Documentar em `docs/operacao/emergencia-caixa.md`
  com prints e número do WhatsApp.
- **SLA combinado com Casa da Babá**: "Suporte por Felipe nas janelas
  acima. Fora delas, flag de emergência reverte para fluxo antigo em <1
  minuto. Tempo de resposta de Felipe a mensagens fora da janela: até 19h
  do próximo dia útil ou 09h do próximo sáb/dom."
- Documentar tudo em `docs/operacao/janela-suporte-felipe.md` e enviar à
  Casa da Babá ANTES do go-live (assinatura por escrito).

**Critério de pronto**:
- Casa da Babá fecha caixa em produção com sucesso.
- Nenhum erro 5xx nos 3 primeiros fechamentos reais.
- Operadora Thatiane confirma "uso natural".
- Casa da Babá assinou `docs/operacao/janela-suporte-felipe.md`.
- Botão de emergência `desligar-emergencia` testado em staging com
  ≥ 1 operador além de Felipe.

### H.2 Cronograma total

| Fase | Duração ideal | Buffer 25% | Calendário (start 2026-05-25) |
|---|---|---|---|
| F0 | 3 dias | 4 dias | seg 2026-05-25 → qui 2026-05-28 |
| F1 | 4 dias | 5 dias | sex 2026-05-29 → ter 2026-06-02 |
| F2 | 6 dias | 8 dias | qua 2026-06-03 → sex 2026-06-12 |
| F3 | 1 dia | 1.5 dia | sáb 2026-06-13 → dom 2026-06-14 |
| F4 | 4 dias | 5 dias | seg 2026-06-15 → sex 2026-06-19 |
| F5 | 3 dias | 4 dias | sáb 2026-06-20 → ter 2026-06-23 |
| F6 | 7 dias | 9 dias | qua 2026-06-24 → seg 2026-07-06 |
| F7 | 2 dias | 2.5 dias | ter 2026-07-07 → qui 2026-07-09 |
| F8 | 4 dias | 5 dias | sex 2026-07-10 → qua 2026-07-15 |
| **TOTAL** | **34 dias úteis** | **44 dias úteis** | **2026-05-25 → 2026-07-15 (~7,5 semanas calendar)** |

Em **semanas part-time efetivas**: **~5 semanas trabalhadas** distribuídas em
**7-8 semanas de calendário** (com fins de semana sendo as horas mais
produtivas).

### H.3 Marcos de validação com cliente

- **M1 (fim de F2)**: Casa da Babá em staging consegue registrar pagamento
  e estorno via PWA. Felipe pede confirmação da Thatiane que UI é clara.
- **M2 (fim de F6)**: Casa da Babá em staging consegue **fechar caixa
  completo**. PDF é enviado ao contador.
- **M3 (fim de F8)**: produção, flag ligada, primeiros 3 fechamentos
  vigiados. Aprovação final.

### H.4 Critério go/no-go para produção (final de F8)

**Checklist obrigatório antes de ligar flag para Casa da Babá**:

- [ ] Todos os 7 queries de validação (F.5) retornam 0 em produção
- [ ] Build em master verde (quando GitHub Actions billing voltar — caso
      contrário, build local + checksum manual)
- [ ] `dotnet test` verde (todos os tests)
- [ ] ArchitectureTests verde
- [ ] E2E manual roteiro executado com sucesso em staging
- [ ] PDF aprovado por contador (assinatura por escrito ou ata)
- [ ] PDF e página pública abrem em iOS Safari (Casa da Babá pode ter
      cliente iPhone)
- [ ] `fly status` mostra app saudável
- [ ] Teste de rollback de flag executado em staging (F4 critério): liga
      flag → cria SessaoCaixa → desliga flag → confirma que próximos
      pagamentos NÃO criam SessaoCaixa
- [ ] **Liga flag em sábado de manhã** (não domingo à noite) — máxima
      janela de suporte de Felipe à frente
- [ ] `docs/operacao/janela-suporte-felipe.md` assinado pela Casa da Babá
- [ ] Endpoint `POST /api/empresa/feature-flags/desligar-emergencia` testado
      em staging com 1 operador adicional que NÃO é Felipe
- [ ] Banner in-app exibido há ≥ 5 dias

**Aprovação**:
- Felipe (dono do produto + dev)
- Confirmação verbal da Thatiane (operadora) no staging
- Aprovação por escrito do contador (validação contábil)

### H.5 Plano de rollback (janela real, não fictícia)

**Janela de suporte ativo**: ver F8 (sáb 09h-22h, dom 09h-22h, seg-sex
19h-22h). **Fora da janela**: rollback é automático via flag de emergência.

- **Falha grave dentro da janela** (operadora não consegue registrar
  pagamento OU caixa fecha errado): Felipe desliga `CaixaConciliadoV2` flag
  por WhatsApp/SQL imediato → UI volta para versão legacy. Dados criados nas
  tabelas novas ficam (princípio aditivo). Felipe investiga em horário não-
  crítico.
- **Falha grave FORA da janela** (ex: madrugada de quarta): Thatiane (ou
  suporte primeiro-nível) usa botão "desligar emergência" no admin. Ação:
  - `POST /api/empresa/feature-flags/desligar-emergencia` (auth admin)
  - Idempotente, log no `AuditLog`, notificação para Felipe via WhatsApp/email
  - Empresa volta ao fluxo legacy em < 1 minuto
  - Felipe investiga na próxima janela útil
- **Falha em hash/PDF/upload**: rollback automático via TX (D.2.6 sequência).
  Operadora vê erro 503, refaz. Se persistir 3x, considerar problema
  estrutural → desliga flag.
- **Bug de leitura** (UI quebrada mas dados OK): hotfix rápido em próxima
  janela de Felipe + redeploy. Flag pode ficar ON enquanto bug é cosmético.
- **Banco corrompido** (ex: invariante violada): pausa flag → investiga →
  script SQL de correção pontual em próxima janela de Felipe → reativa.
  Backups Fly automáticos cobrem worst case.

**Dependência crítica para go-live**: implementar endpoint
`desligar-emergencia` + treinar operadora a usá-lo. Sem isso, F8 não
fecha (vira premissa N.11).

---
