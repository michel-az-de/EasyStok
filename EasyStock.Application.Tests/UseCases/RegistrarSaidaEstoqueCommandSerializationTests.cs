using System.Text.Json;
using System.Text.Json.Serialization;
using EasyStock.Application.UseCases.RegistrarSaidaEstoque;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// Regressao #507 (QA EZ-STK-001): o record <see cref="RegistrarSaidaEstoqueItemCommand"/>
/// tinha 3 construtores e nenhum [JsonConstructor], entao o System.Text.Json lancava
/// NotSupportedException ao desserializar o body de POST /api/estoque/saida — 500 em TODA
/// saida via HTTP. Os testes que constroem o command em C# nunca cobriram a borda de
/// desserializacao; estes cobrem, usando as MESMAS opcoes JSON da Api (CamelCase +
/// case-insensitive + JsonStringEnumConverter, ver CoreMvcExtensions.AddEasyStockCoreMvc).
/// </summary>
public class RegistrarSaidaEstoqueCommandSerializationTests
{
    // Espelha EasyStock.Api.DependencyInjection.CoreMvcExtensions.
    private static JsonSerializerOptions ApiLikeOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };

    [Fact]
    public void Desserializa_payload_de_saida_por_lote_do_web()
    {
        // Payload identico ao que o Web (SaidasService.CriarAsync) posta para a Api.
        const string json = """
        {
          "empresaId": "6972cc4e-f8ae-4ef5-b682-5209aa4db1aa",
          "itens": [
            {
              "itemEstoqueId": "7747654d-fe99-4005-8412-e39ca8211b54",
              "produtoId": "00000000-0000-0000-0000-000000000000",
              "produtoVariacaoId": null,
              "quantidade": 1,
              "valorVendaUnitario": 11.76,
              "descricao": null
            }
          ],
          "dataVenda": "2026-06-06T00:00:00Z",
          "dataSaida": "2026-06-06T00:00:00Z",
          "dataEnvio": null,
          "notaFiscal": null,
          "natureza": "Venda",
          "canal": "LojaPropria",
          "observacoes": null
        }
        """;

        var cmd = JsonSerializer.Deserialize<RegistrarSaidaEstoqueCommand>(json, ApiLikeOptions());

        Assert.NotNull(cmd);
        Assert.Equal(NaturezaMovimentacaoEstoque.Venda, cmd!.Natureza);
        Assert.Equal(CanalVenda.LojaPropria, cmd.Canal);
        var item = Assert.Single(cmd.Itens);
        Assert.Equal(Guid.Parse("7747654d-fe99-4005-8412-e39ca8211b54"), item.ItemEstoqueId);
        Assert.Equal(1m, item.Quantidade);
        Assert.Equal(11.76m, item.ValorVendaUnitario);
        Assert.Null(item.Descricao);
    }

    [Fact]
    public void Desserializa_saida_por_produto_fifo_sem_itemEstoqueId()
    {
        // Caminho FIFO/FEFO: sem itemEstoqueId, so produtoId.
        const string json = """
        {
          "empresaId": "6972cc4e-f8ae-4ef5-b682-5209aa4db1aa",
          "itens": [
            { "produtoId": "11111111-1111-1111-1111-111111111111", "quantidade": 3, "valorVendaUnitario": 9.9 }
          ],
          "dataVenda": "2026-06-06T00:00:00Z",
          "dataSaida": "2026-06-06T00:00:00Z",
          "natureza": "Perda",
          "canal": "LojaPropria"
        }
        """;

        var cmd = JsonSerializer.Deserialize<RegistrarSaidaEstoqueCommand>(json, ApiLikeOptions());

        Assert.NotNull(cmd);
        var item = Assert.Single(cmd!.Itens);
        Assert.Null(item.ItemEstoqueId);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), item.ProdutoId);
        Assert.Equal(3m, item.Quantidade);
        Assert.Equal(NaturezaMovimentacaoEstoque.Perda, cmd.Natureza);
    }
}
