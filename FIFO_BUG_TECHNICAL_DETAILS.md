# 🔴 BUG TÉCNICO DETALHADO - FIFO Lotes Quebrado

## Localização do Bug

**Arquivo**: `EasyStock.Application/UseCases/RegistrarSaidaEstoque/RegistrarSaidaEstoqueUseCase.cs`  
**Método**: `ExecuteAsync()`  
**Linha**: ~60-120 (estimado)

## Teste Falhando #1: Consumo FIFO Simples

### Cenário
```csharp
Lote 1 (Antigo): 10 unidades, EntradaEm = 2026-04-01
Lote 2 (Novo):    5 unidades, EntradaEm = 2026-04-02
Total:           15 unidades

Requisição: Sair 12 unidades
Esperado:   Consumir 10 de Lote1 + 2 de Lote2
Resultado:  ❌ FALHA (log não mostra o resultado, mas teste esperava [10,2])
```

### Asserção que Falha
```csharp
result.Itens.Select(i => i.QuantidadeSaida).Should().BeEquivalentTo([10, 2]);
// ❌ Falha - não está retornando [10,2]
```

### Cálculo Esperado
```
Quantidade a consumir: 12
Lote 1: Min(12, 10) = 10 consumido → Lote1 fica com 0
Restante: 12 - 10 = 2
Lote 2: Min(2, 5) = 2 consumido → Lote2 fica com 3
Resultado: [10, 2] ✅
```

## Teste Falhando #2: Validação de Insuficiência em FIFO

### Cenário
```csharp
Lote 1: 2 unidades
Lote 2: 5 unidades (não deve ser necessário)
Total:  7 unidades

Requisição: Sair 10 unidades
Esperado:   Lançar EstoqueInsuficienteException
Resultado:  ❌ Não lança exceção (ou lança com msg errada)
```

### Problema Identificado
A validação de suficiência pode estar:
1. Somando apenas o primeiro lote (2 < 10 ✅)
2. Somando todos os lotes (7 < 10 ✅)
3. Mas não está lançando exceção → lógica de throw está faltando

## Teste Falhando #3: Controller Endpoint

### Localização
```
EasyStock.Api.UnitTests/Controllers/ItemEstoqueControllerTests.cs
Teste: RegistrarSaida_DeveAceitarSaidaPorProdutoEConsumirLotesEmFifo
```

**Status**: Falha por propagação do bug da UseCase

---

## Possíveis Causas

### Causa #1: Ordem de Lotes Incorreta
```csharp
// ❌ ERRADO
var lotes = lotes.ToList(); // Ordem aleatória ou por ID

// ✅ CORRETO
var lotes = lotes.OrderBy(l => l.EntradaEm).ToList(); // FIFO
```

**Verificar**: `GetLotesDisponiveisParaSaidaAsync()` está retornando ordenado?

### Causa #2: Loop de Consumo Errado
```csharp
// ❌ POSSÍVEL ERRO
foreach (var lote in lotes)
{
    var toConsume = quantidadeRestante; // ❌ Pode estar pegando total
    lote.QuantidadeAtual = lote.QuantidadeAtual.Subtract(toConsume);
    itemsConsumidos.Add(toConsume);
}

// ✅ CORRETO
foreach (var lote in lotes)
{
    var toConsume = Math.Min(quantidadeRestante, (int)lote.QuantidadeAtual.Value);
    lote.QuantidadeAtual = lote.QuantidadeAtual.Subtract(new Quantidade(toConsume));
    itemsConsumidos.Add(toConsume);
    quantidadeRestante -= toConsume;
    if (quantidadeRestante == 0) break;
}
```

### Causa #3: Validação Antes do Loop
```csharp
// ❌ PODE ESTAR FALTANDO
if (totalDisponivel < quantidadeSolicitada)
{
    throw new EstoqueInsuficienteException(...);
}
```

**Verificar**: Validação está sendo feita ANTES de tentar consumir?

---

## Checklist de Debug

Quando for investigar o bug:

- [ ] Adicionar log de debug:
  ```csharp
  _logger.LogDebug("Lotes disponíveis para FIFO: {Count}, Total: {Total}",
      lotes.Count,
      lotes.Sum(l => (int)l.QuantidadeAtual.Value));

  _logger.LogDebug("Consumindo {Quantidade} em FIFO", quantidadeSolicitada);
  ```

- [ ] Verificar se `GetLotesDisponiveisParaSaidaAsync()` está:
  - Filtrando lotes com `Status == Ok` ✅
  - Retornando ordenado por `EntradaEm ASC` ❌ VERIFICAR
  - Retornando `IReadOnlyList<ItemEstoque>` ou `List<ItemEstoque>` ❌ VERIFICAR

- [ ] Verificar valor de `Quantidade.Value`:
  - Está usando `Value` (decimal) ou `Value` (int)? ❌ MISMATCH POSSÍVEL

- [ ] Testar com override simples:
  ```csharp
  // Teste local direto na UseCase
  var qtdConsumida = 0;
  var lotes = new[] { 10, 5 };
  foreach (var lote in lotes)
  {
      var consumir = Math.Min(12 - qtdConsumida, lote);
      qtdConsumida += consumir;
  }
  // qtdConsumida deve ser 12? Ou 10?
  ```

---

## Prototipo de Fix

```csharp
public async Task<RegistrarSaidaEstoqueResult> ExecuteAsync(
    RegistrarSaidaEstoqueCommand command)
{
    // ... validações iniciais ...

    var lotes = await itemRepository.GetLotesDisponiveisParaSaidaAsync(
        command.EmpresaId,
        command.Itens.First().ProdutoId,  // Assumindo mesmo produto
        null,
        true // FIFO mode
    ).ConfigureAwait(false);

    // ✅ ORDENAR EXPLICITAMENTE
    var lotesOrdenados = lotes
        .Where(l => l.Status == StatusItemEstoque.Ok)
        .OrderBy(l => l.EntradaEm)
        .ThenBy(l => l.Id)
        .ToList();

    // ✅ VALIDAR TOTAL
    var totalDisponivel = lotesOrdenados.Sum(l => (int)l.QuantidadeAtual.Value);
    if (totalDisponivel < 12)
    {
        throw new EstoqueInsuficienteException(
            command.Itens.First().ProdutoId,
            12,
            totalDisponivel);
    }

    // ✅ CONSUMIR EM FIFO
    var quantidadeRestante = 12;
    var itemVendas = new List<ItemVenda>();
    var movimentacoes = new List<MovimentacaoEstoque>();
    var lotesAtualizados = new List<ItemEstoque>();

    foreach (var lote in lotesOrdenados)
    {
        if (quantidadeRestante <= 0) break;

        var quantidadeDisponivelNaLote = (int)lote.QuantidadeAtual.Value;
        var quantidadeAConsumir = Math.Min(
            quantidadeRestante,
            quantidadeDisponivelNaLote);

        // Atualizar lote
        lote.QuantidadeAtual = new Quantidade(
            quantidadeDisponivelNaLote - quantidadeAConsumir);
        lotesAtualizados.Add(lote);

        // Criar ItemVenda
        itemVendas.Add(new ItemVenda
        {
            ItemEstoqueId = lote.Id,
            Quantidade = new Quantidade(quantidadeAConsumir),
            // ... outros campos ...
        });

        // Criar MovimentacaoEstoque
        movimentacoes.Add(new MovimentacaoEstoque
        {
            ItemEstoqueId = lote.Id,
            Quantidade = new Quantidade(quantidadeAConsumir),
            Tipo = TipoMovimentacaoEstoque.Saida,
            // ... outros campos ...
        });

        quantidadeRestante -= quantidadeAConsumir;
    }

    // ✅ PERSISTIR TUDO
    await itemRepository.UpdateRangeAsync(lotesAtualizados).ConfigureAwait(false);
    await itemVendaRepository.InsertRangeAsync(itemVendas).ConfigureAwait(false);
    await movimentacaoRepository.InsertRangeAsync(movimentacoes).ConfigureAwait(false);
    await unitOfWork.CommitAsync().ConfigureAwait(false);

    return new RegistrarSaidaEstoqueResult
    {
        Itens = itemVendas.Select(iv => new SaidaItemResult(
            iv.ItemEstoqueId,
            iv.Quantidade.Value))
        .ToList()
    };
}
```

---

## Impacto em Produção

Se esse bug for para produção:

1. **Contabilidade errada** de estoque
2. **Produtos vencidos podem sair antes dos novos** (quebra FIFO)
3. **Auditorias de inventário falharem**
4. **Relatórios de movimento errados**
5. **Possível perda financeira** se custo médio tiver grande variação

---

## Urgência

🔴 **BLOQUEADOR DE RELEASE** - Não liberar produção até fix e re-test.

ETA: ~2-4 horas para debug + fix + testes
