# 06 — Testes e Validação

> Parte do [Plano](README.md). Anterior: [05-migracao.md](05-migracao.md). Próximo: [07-faseamento.md](07-faseamento.md).

### G.1 Unit tests por componente

Cobertura mínima: **85%** linhas por UseCase novo (xUnit + NSubstitute,
padrão do projeto). Justificativa: 95% gera ruído em código boilerplate de
mapping; 85% garante invariantes críticas cobertas. **100% obrigatório**
para state machines (`PedidoStateMachine`, `SessaoCaixaStateMachine`,
detecção de `EstadoFinanceiroPedido`).

#### G.1.1 `ConfirmarPagamentoUseCase` (`EasyStock.Application.Tests/UseCases/Pagamentos/ConfirmarPagamentoUseCaseTests.cs`)

Cenários:
- Pagamento integral em pedido aguardando → cria pagamento, abre sessão se
  não há, cria movimento caixa
- Pagamento parcial → cria pagamento, recalcula `TotalPago`
- Pagamento que zera saldo (split final) → estado vira `Pago`
- `cmd.Valor + TotalPagoConfirmado > Total` → `UseCaseValidationException`
  com código `PAGAMENTO_EXCEDE_TOTAL`
- `cmd.Valor <= 0` → exceção
- Método inválido → exceção
- Pedido cancelado → exceção `PEDIDO_CANCELADO`
- Sessão em conferência hoje → exceção `SESSAO_EM_CONFERENCIA`
- Sessão fechada hoje → cria pagamento com `MovimentoCaixaId = NULL` + log warning
- Pagamento sem sessão aberta → abre sessão automaticamente + cria movimento
- Pedido inexistente → retorna null
- TX rollback em falha de SaveChanges → nenhum pagamento, nenhum movimento, nenhuma sessão criada

#### G.1.2 `EstornarPagamentoUseCase`

Cenários:
- Estorna pagamento confirmado → cria registro estornado + marca original
- Tentar estornar 2x → exceção `PAGAMENTO_JA_ESTORNADO`
- Estornar pagamento de sessão já fechada → cria pagamento de estorno SEM
  movimento de caixa (sem reabrir fechamento)
- Estornar com sessão em conferência → exceção
- Motivo vazio/curto → exceção `MOTIVO_OBRIGATORIO`
- Pedido vira estado correto após estorno (parcialmente_pago, aguardando, etc.)

#### G.1.3 `CancelarPedidoUseCase` (estendido)

Cenários:
- Cancelar pedido sem pagamentos → `CanceladoEstornado` direto
- Cancelar pedido com 1 pagamento confirmado → estorno automático em cascata,
  resultado `CanceladoEstornado`
- Cancelar pedido com pagamento já estornado parcialmente → estorna o resto
- Idempotência: cancelar duas vezes → segundo no-op

#### G.1.4 `AbrirSessaoCaixaUseCase`

- Abrir com saldo inicial 0
- Abrir com saldo inicial > 0
- Duas tentativas concorrentes → segundo recebe `unique_violation` → 409
- Reabertura no mesmo dia após fechamento prévio → 409
- Loja null vs loja específica → tratados separadamente

#### G.1.5 `RegistrarMovimentoManualUseCase`

- Sangria, reforço, despesa, entrada → todos OK em sessão aberta
- Tipo inválido → exceção
- Sessão em conferência → 423
- Sessão fechada → 423
- Data fora da sessão (sem `permitirRetroativo`) → 422

#### G.1.6 `IniciarFechamentoSessaoUseCase` / `ConfirmarFechamentoSessaoUseCase`

- Iniciar fechamento muda status → `em_conferencia`
- Sessão recente (<10min) → 422
- Movimento criado nos últimos 30s → 409
- Confirmar fechamento gera PDF + hash + verificacaoCodigo único
- Falha de PDF → rollback completo
- Divergência sem justificativa → 422
- Idempotência: 2 cliques mesmo Idempotency-Key → mesmo resultado

#### G.1.7 `FechamentoCaixaImutavelInterceptor`

- Tentar `Update` em FechamentoCaixa existente → `RegraDeDominioVioladaException`
- Tentar `Delete` em FechamentoCaixa → exceção
- Modificar SessaoCaixa fechada → exceção
- Modificar SessaoCaixa em conferência → OK (estado intermediário)

#### G.1.8 Property-based tests (FsCheck ou Hedgehog se disponíveis; senão xUnit `Theory`)

```csharp
[Property]
public void SomaPagamentosConfirmados_NuncaExcedeTotalDoPedido(Pedido pedido, List<decimal> valores)
{
    foreach (var v in valores)
    {
        if (pedido.TotalPagoConfirmado + v <= pedido.Total.Valor)
            pedido.AdicionarPagamentoConfirmado(v);
    }
    Assert.True(pedido.TotalPagoConfirmado <= pedido.Total.Valor);
}
```

Pacote sugerido: **FsCheck.Xunit** 3.x. **Não está em uso no repo** (grep
em todos os `.csproj`, incluindo worktrees, retornou zero matches em
2026-05-16). Adicionar como `<PackageReference>` diretamente em
`EasyStock.Application.Tests/EasyStock.Application.Tests.csproj` quando
F2/F4 começar a escrever property-based tests. **Repo não usa Central
Package Management (`Directory.Packages.props` inexistente)** — versão
fica no `.csproj` do projeto consumidor, espelhando o padrão existente do
projeto. Decidir versão exata no momento de adicionar (3.x estável é a
recomendação default).

### G.2 Integration tests (`EasyStock.Api.IntegrationTests`)

Usa `WebApplicationFactory` + Postgres TestContainer (padrão do projeto).

Cenários ponta-a-ponta:

#### G.2.1 Fluxo completo do dia

```
Abrir sessão (R$100 inicial)
→ Criar pedido R$ 120
→ Pagar R$ 50 PIX → pedido = ParcialmentePago, sessão tem 1 mov pagamento
→ Pagar R$ 70 dinheiro → pedido = Pago, sessão tem 2 movs
→ Iniciar fechamento (aguardar 10min OU mockClock skip)
→ Confirmar fechamento com conferência exata
→ GET PDF → 200, content-type pdf
→ GET /caixa/verificar/{codigo} → 200, hash ok
→ Sessão = fechada, FechamentoCaixa imutável
```

#### G.2.2 Fluxo de estorno

```
Sessão aberta + pedido pago integral
→ POST /pagamentos/{id}/estornar com motivo → 200
→ GET /pedidos/{id}/pagamentos → mostra 1 confirmado + 1 estornado (auto-ref)
→ Pedido = ParcialmentePago (TotalPagoConfirmado = 0 + Valor original)
→ MovimentoCaixa tipo estorno_pagamento criado linkado
→ Fechar sessão → resumo mostra net = 0 para esse pagamento
```

#### G.2.3 Fluxo de cancelamento

```
Pedido pago integral
→ POST /pedidos/{id}/cancelar → 200
→ Todos pagamentos confirmados foram estornados em cascata
→ Pedido.Status = "cancelado", EstadoFinanceiro = CanceladoEstornado
→ MovimentosCaixa de estorno criados
```

#### G.2.4 Race conditions

- 2 POST /pagamentos paralelos no mesmo pedido (`Task.WhenAll`) →
  ambos sucesso OU um 422 PAGAMENTO_EXCEDE_TOTAL (nunca soma errada)
- 2 POST /caixa/abrir paralelos → um 201, outro 409
- 2 POST /caixa/sessoes/{id}/confirmar-fechamento paralelos → um 200,
  outro 409 (advisory lock funcionando)
- Idempotency-Key duplicada com mesmo body → segundo retorna 200 cacheado
- Idempotency-Key duplicada com body diferente → 409 `IDEMPOTENCY_BODY_MISMATCH`

### G.3 Snapshot tests

#### G.3.1 PDF de FechamentoCaixa (Verify.QuestPDF ou bytes raw)

```csharp
[Fact]
public async Task FechamentoCaixa_GerarPdf_Layout()
{
    var fechamento = TestData.FechamentoExemplo();
    var bytes = await renderer.RenderAsync(fechamento);

    // Snapshot dos bytes do PDF (ou texto extraído via QuestPDF.Companion)
    await Verify(bytes).UseExtension("pdf");
}
```

Pacote: **Verify.Xunit** 24.x. **Não está em uso no repo** (grep em todos
os `.csproj`, incluindo worktrees, retornou zero matches em 2026-05-16).
Adicionar como `<PackageReference>` diretamente em
`EasyStock.Api.IntegrationTests/EasyStock.Api.IntegrationTests.csproj`
(ou no projeto de teste que conter os snapshot tests do PDF) quando F6
começar. **Repo não usa Central Package Management** — versão por
`.csproj`. Alternativa de menor dependência: "comparação estrutural" via
`PdfPig` extraindo texto e validando lista de seções esperadas — evita
instabilidade de bytes em CI e não requer Verify.

#### G.3.2 Snapshot JSON

`FechamentoCaixa.SnapshotJson` é um jsonb canonicalizado. Teste:

```csharp
[Fact]
public void Snapshot_Json_IsCanonical()
{
    var s1 = SnapshotBuilder.Build(sessao, movimentos);
    var s2 = SnapshotBuilder.Build(sessao, movimentos);
    Assert.Equal(s1, s2); // mesmo input → mesmo bytes (chaves ordenadas)
}
```

Hash depende de canonical-JSON estável (chaves ordenadas alfabeticamente,
sem whitespace, datas em ISO-8601 Z).

### G.4 Concurrency tests dedicados

Pasta `EasyStock.Api.IntegrationTests/Concurrency/`:

- `PagamentoConcorrenteTests.cs` — `Parallel.ForEachAsync` com 10 threads
  tentando criar pagamento que excede total → exatamente 1 sucesso esperado
- `FechamentoConcorrenteTests.cs` — 5 threads chamando confirmar-fechamento →
  exatamente 1 cria FechamentoCaixa
- `IdempotencyBodyHashTests.cs` — retry com body diferente → 409

### G.5 E2E manual (Playwright opcional, roteiro escrito obrigatório)

Roteiro `docs/plan/e2e/dia-da-thatiane.md`:

1. Login como Thatiane
2. Verifica que aba "Caixa" mostra "nenhum caixa aberto"
3. Clica "Abrir caixa" → modal com saldo inicial R$ 100
4. Confirma → tela passa para "Sessão aberta"
5. Cria pedido novo via PWA: 1x bolo de chocolate R$ 120
6. Aba Pagamentos → adiciona PIX R$ 50 → vê banner "Parcialmente pago"
7. Adiciona dinheiro R$ 70 → vê banner "Pago"
8. Volta para Caixa → vê resumo: vendas R$ 120, esperado dinheiro R$ 170,
   esperado adquirente R$ 50
9. Cria movimento manual: sangria R$ 100 → resumo atualiza esperado dinheiro
   para R$ 70
10. Estorna o pagamento PIX R$ 50 com motivo "lançado em duplicidade"
11. Volta ao caixa → esperado adquirente cai para R$ 0; pedido vira parcial
12. Aguarda 10min (ou skip via flag dev), clica "Fechar caixa"
13. Wizard passo 1: conta R$ 68,50 dinheiro (diferença -R$ 1,50)
14. Wizard passo 2: justificativa "Troco a menos — operadora dropou moeda"
15. Wizard passo 3: marca checkbox, NÃO envia email contador, clica
    "Fechar definitivamente"
16. Sucesso → baixa PDF → confere visualmente (hash visível, QR code escaneável)
17. Tenta fechar de novo → botão sumiu, mostra "Caixa fechado"
18. Tenta POST API /confirmar-fechamento via curl → 409
19. Acessa `/caixa/verificar/{codigo}` em aba anônima → vê página pública
    com hash igual ao PDF

Quaisquer divergências entre snapshot do PDF e tela = bug.

### G.6 Validação contábil

- **Antes do go-live**: enviar 1 PDF de fechamento de teste para o contador
  da Casa da Babá (Felipe coordena).
- **Pergunta ao contador**: "Esse documento é suficiente para anexar à
  contabilidade mensal? Falta CFOP, NCM, alíquota, alguma coisa?"
- **Critério go**: contador aprova ou aponta 1-3 ajustes pequenos. Se
  contador pedir reestruturação grande → volta para F0/F+1 (rever escopo).
- **Documentar feedback** em `docs/plan/validacao-contabil.md` antes de
  primeiro deploy real.

---
