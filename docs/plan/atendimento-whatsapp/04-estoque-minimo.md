# Onda 4 — Estoque mínimo necessário (S22–S23)

Objetivo: o desacerto de estoque vira alerta em texto com ajuste rápido, e o dia de produção gera
estoque em porções na mesma ação. É a correção do defeito que mais dói (doc 03, UC-09) e a resposta à
Q4 (produção gera estoque; o cardápio lê disponibilidade; sem amarração rígida).
Cobre US-060, US-061, US-062, US-063, US-064, US-065 (parte), RN-44 a RN-51.

O que já funciona e não se toca: FEFO por validade (`RegistrarSaidaEstoqueUseCase.cs:122`), status
`Vencido` por job, notificações `ProdutoVencendo`/`ProdutoVencido`, contagem física (`Contagem`,
`AjusteInventario`, #704), reposição por limiar (ADR-0039), composição/receita.

---

### S22 · Alerta de desacerto e ajuste rápido

**Problema.** #886: `QuantidadeDescoberta` é gravada (S17 amplia para o caminho do pedido) mas nenhuma consulta a lê. RN-48, RN-49, UC-09: alerta persistente em texto, fecha quando a dona conta e ajusta.
**Abordagem.** Sem tabela nova: o alerta é a projeção `SUM(QuantidadeDescoberta) > 0` por produto, com texto montado a partir das `MovimentacaoEstoque` de saída a descoberto (referência = pedido). O ajuste rápido reusa a lógica de contagem (`RegistrarItemContagemUseCase` zera o descoberto ao contar) sem obrigar uma sessão de contagem completa.
**Escopo.**
- Criar `App/UseCases/Inventario/Desacertos/ListarDesacertosEstoqueUseCase.cs` → `[{ ProdutoId, Nome, QuantidadeDescoberta, QuantidadeAtual, Texto ("Vendeu 2 porções de Lasanha sem produção lançada em 22/09 (pedidos #a1b2c3, #d4e5f6)"), Pedidos[], PrimeiroEm, UltimoEm }]`; query em `Postgre/Queries/` com `EmpresaId` no `WHERE`.
- Criar `AjustarSaldoRapidoUseCase.cs` input `(EmpresaId, ProdutoId, LojaId?, QuantidadeContada, Motivo)`: cria `AjusteInventario` + `MovimentacaoEstoque(Tipo=Ajuste, Natureza=Ajuste)` por lote afetado, zera `QuantidadeDescoberta`, fecha o alerta por construção. Reusar o método de `ItemEstoque` chamado por `RegistrarItemContagemUseCase` (é o que "pisa em 0 e reconcilia", `ItemEstoque.cs:240-253`); não duplicar.
- `Api/Controllers/EstoqueDesacertosController.cs`: `GET api/estoque/desacertos`, `POST api/estoque/desacertos/{produtoId}/ajustar` (policy da permissão de estoque existente, `Permissao.GerenciarEstoque`).
- Publicar `estoque.desacerto` no SSE ao listar mudar? Não: S17 já publica na origem; o ajuste publica `estoque.desacerto_resolvido`.
**Fora.** Contagem completa (existe em #704); relatório histórico.
**Aceite.**
- [ ] Produto com descoberto 2 aparece na lista com texto legível citando os pedidos.
- [ ] Ajuste com contagem 5 → `QuantidadeAtual=5`, `QuantidadeDescoberta=0`, `AjusteInventario` gravado com motivo, produto some da lista.
- [ ] Ajuste em produto sem descoberto também funciona (é um ajuste comum).
- [ ] #772 continua verdadeiro: `quantidade_atual` nunca negativa.
**Testes (Red).** `ListarDesacertosEstoqueUseCaseTests.TextoCitaPedidos`, `AjustarSaldoRapidoUseCaseTests.ZeraDescobertoEGravaAjuste`, `...SemDescobertoAjustaNormal`.
**Rollback.** Remover controller e use cases; nada de schema.
**Depende de.** S17.
**Leitura mínima.** `Domain/Entities/ItemEstoque.cs` (linhas 25-45 e 195-260); `App/UseCases/Inventario/RegistrarItemContagemUseCase.cs`; `Domain/Entities/AjusteInventario.cs`; `Domain/Entities/MovimentacaoEstoque.cs` (factory de ajuste); `App/Ports/Output/Persistence/IItemEstoqueRepository.cs`.
**Tamanho.** M. **Tier.** alto.

---

### S23 · Registrar produção em porções gerando estoque

**Problema.** `FinalizarLoteUseCase` gera etiquetas e não cria `ItemEstoque`; a entrada de estoque é ação separada. A Tatiana produz 1 kg e vende porções de 500 g; registra o peso e o sistema nega a venda. RN-44 a RN-47, US-060, US-061, UC-08.
**Abordagem.** Um use case transacional que faz as duas coisas com a porção como unidade: cria o `Lote` (com `LoteItem.Quantidade = porções`, `PesoG = peso real por porção`, `ValidadeDias`) e a entrada de estoque (`Natureza=Producao`, `Quantidade = porções`, `ValidadeEm = ExpiraEm`, `CodigoLote = Lote.Codigo`). Produto vendável fica com `UnidadeMedidaBase = Un`. Insumo intermediário (molho, massa) é produto com `EhInsumo=true` e entra pelo mesmo caminho; a montagem a partir de insumos fica para depois (US-067, Could).
**Escopo.**
- Criar `App/UseCases/Producao/RegistrarProducaoUseCase.cs` input `(EmpresaId, LojaId?, DataProducao, Itens[(ProdutoId, Porcoes, PesoPorPorcaoG?, ValidadeDias, CustoUnitario?)], Observacao?, IdempotencyKey?)`. Passos na mesma transação: `CriarLoteUseCase` (ou o repositório direto se o use case abrir transação própria: verificar `ITransactionManager`/`IUnitOfWork` usado em `FinalizarVendaBalcaoUseCase`, que já compõe vários use cases numa transação) → `AdicionarItemLote` por item → `RegistrarEntradaEstoqueUseCase` por item → `FinalizarLote` (etiquetas). Falha em qualquer passo desfaz tudo.
- `Api/Controllers/ProducaoController.cs`: `POST api/producao` (idempotente via `IdempotencyMiddleware`, prefixo em `IdempotencyOptions.Add("/api/producao")`), `GET api/producao/lotes?vencendoEmDias=3` (lotes de estoque com `ValidadeEm` próxima e saldo, para RN-51) reaproveitando a consulta de validade existente (`ItemEstoqueController` / `EstoqueAnalyticsQueries`).
- Regra: produto com `TipoEmbalagem.Embalado` sem `PesoPorPorcaoG` → erro de validação antes de gravar (hoje `FinalizarLote` já barra; antecipar).
- Cardápio: `ListarCardapioPublicoUseCase` já projeta `estoqueAtual`/`disponivel` para item vinculado; confirmar que a entrada reflete (teste de integração).
**Fora.** Montagem a partir de insumos (US-067); etiqueta nutricional.
**Aceite.**
- [ ] Registrar 2 porções de 500 g com validade 5 dias → `Lote` finalizado com 2 etiquetas, `ItemEstoque` com `QuantidadeAtual=2`, `ValidadeEm = data + 5`, `CodigoLote` igual ao do lote, `MovimentacaoEstoque(Entrada, Producao)`.
- [ ] Falha na entrada de estoque → nenhum lote fica gravado.
- [ ] Segunda chamada com a mesma `IdempotencyKey` não duplica.
- [ ] Cardápio público do item vinculado passa a `disponivel=true` com `estoqueAtual=2`.
**Testes (Red).** `RegistrarProducaoUseCaseTests.CriaLoteEEstoqueNaMesmaTransacao`, `...FalhaDesfazTudo`, `...EmbaladoSemPesoRejeita`, `ProducaoControllerTests.IdempotentePorChave`, `RegistrarProducaoIntegrationTests.CardapioRefleteDisponibilidade` (IntegrationTests).
**Rollback.** Remover controller e use case; lotes e entradas criados ficam válidos.
**Depende de.** Nada (S22 complementa).
**Leitura mínima.** `Domain/Entities/Lote.cs`; `App/UseCases/CriarLote/*.cs`; `App/UseCases/AdicionarItemLote/*.cs`; `App/UseCases/FinalizarLote/FinalizarLoteUseCase.cs`; `App/UseCases/RegistrarEntradaEstoque/RegistrarEntradaEstoqueUseCase.cs` (só o command e o fluxo principal); `App/UseCases/FinalizarVendaBalcao/FinalizarVendaBalcaoUseCase.cs` (padrão de composição transacional).
**Tamanho.** M. **Tier.** alto.
