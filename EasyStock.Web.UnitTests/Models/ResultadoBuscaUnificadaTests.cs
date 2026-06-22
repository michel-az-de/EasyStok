using System.Text.Json;
using EasyStock.Web.Models.Api;
using FluentAssertions;

namespace EasyStock.Web.UnitTests.Models;

/// <summary>
/// QA v1.10 BUG-01: a busca global (/busca) voltava vazia porque o corpo da API trazia
/// quantidadeAtual com escala decimal (itens_estoque.QuantidadeAtual / movimentacoes_estoque.Quantidade
/// sao numeric(18,3) -> JSON "25.000"), e o DTO declarava int? -> JsonException ao desserializar
/// o List&lt;&gt; inteiro -> ApiClient devolvia PARSE_ERROR -> BuscaController mascarava como [].
/// Estes testes travam o contrato com as MESMAS opcoes do ApiClient (camelCase, case-insensitive).
/// </summary>
public class ResultadoBuscaUnificadaTests
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void Desserializa_item_de_estoque_com_quantidade_escalada_sem_lancar()
    {
        // Corpo realista de um item com saldo: quantidadeAtual vem com escala 3 (numeric(18,3)).
        const string json =
            "[{\"tipo\":\"ItemEstoque\",\"id\":\"11111111-1111-1111-1111-111111111111\"," +
            "\"produtoId\":\"22222222-2222-2222-2222-222222222222\",\"produtoVariacaoId\":null," +
            "\"titulo\":\"Vassoura\",\"subtitulo\":null,\"chaveExibicao\":\"VASS-001\",\"score\":100," +
            "\"sku\":\"VASS-001\",\"quantidadeAtual\":25.000,\"status\":\"Ok\"," +
            "\"fornecedorNome\":null,\"loja\":null}]";

        var lista = JsonSerializer.Deserialize<List<ResultadoBuscaUnificada>>(json, Opts);

        lista.Should().ContainSingle();
        lista![0].Titulo.Should().Be("Vassoura");
        lista[0].QuantidadeAtual.Should().Be(25m);
    }

    [Fact]
    public void Desserializa_resultado_de_produto_com_quantidade_nula()
    {
        const string json =
            "[{\"tipo\":\"Produto\",\"id\":\"33333333-3333-3333-3333-333333333333\"," +
            "\"produtoId\":\"33333333-3333-3333-3333-333333333333\",\"titulo\":\"Mesa\"," +
            "\"chaveExibicao\":\"MESA-1\",\"score\":60,\"quantidadeAtual\":null}]";

        var lista = JsonSerializer.Deserialize<List<ResultadoBuscaUnificada>>(json, Opts);

        lista.Should().ContainSingle();
        lista![0].QuantidadeAtual.Should().BeNull();
    }
}
