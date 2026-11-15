using System.Text.Json;
using EasyStock.Domain.Enums;
using EasyStock.Web.Models.Api;
using FluentAssertions;

namespace EasyStock.Web.UnitTests.Models;

/// <summary>
/// Trava o espelho literal dos enums do Domain no Web (issues 797 e 828): a Api serializa
/// enum como string (JsonStringEnumConverter) e o Web, sem referência ao Domain em produção,
/// mapeia por nome em <c>EnumStringOrIntConverter</c>. Nome fora do mapa vira 0 em silêncio
/// (badge "0" na UI). Os casos iteram o enum canônico do Domain, então um valor novo sem
/// espelho no Web deixa o teste vermelho em vez de virar 0 em produção.
/// </summary>
public class EnumStringOrIntConverterTests
{
    public static IEnumerable<object[]> TodosOsStatusPedidoFornecedor()
        => Enum.GetValues<StatusPedidoFornecedor>().Select(s => new object[] { s });

    public static IEnumerable<object[]> TodosOsStatusProduto()
        => Enum.GetValues<StatusProduto>().Select(s => new object[] { s });

    public static IEnumerable<object[]> TodosOsTipoProduto()
        => Enum.GetValues<TipoProduto>().Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(TodosOsStatusPedidoFornecedor))]
    public void Todo_StatusPedidoFornecedor_do_Domain_vira_valor_e_rotulo_proprios(StatusPedidoFornecedor status)
    {
        var json = $$"""
            {"pedidoId":"p1","fornecedorId":"f1","fornecedorNome":"F","status":"{{status}}"}
            """;
        var pedido = JsonSerializer.Deserialize<PedidoAberto>(json, Opts)!;
        var historico = JsonSerializer.Deserialize<FornecedorHistoricoItem>(json, Opts)!;

        pedido.Status.Should().Be((int)status, $"'{status}' precisa de entrada no mapa do EnumStringOrIntConverter");
        pedido.StatusLabel.Should().NotBe(((int)status).ToString(), $"'{status}' precisa de rótulo em PedidoAberto.StatusLabel");
        historico.StatusLabel.Should().NotBe(((int)status).ToString(), $"'{status}' precisa de rótulo em FornecedorHistoricoItem.StatusLabel");
    }

    [Theory]
    [InlineData(StatusPedidoFornecedor.EmTransito, "Em Trânsito")]
    [InlineData(StatusPedidoFornecedor.RecebidoParcial, "Recebido Parcial")]
    public void Rotulo_com_espaco_e_acento(StatusPedidoFornecedor status, string label)
    {
        var json = $$"""
            {"pedidoId":"p1","fornecedorId":"f1","fornecedorNome":"F","status":"{{status}}"}
            """;
        JsonSerializer.Deserialize<PedidoAberto>(json, Opts)!.StatusLabel.Should().Be(label);
    }

    [Theory]
    [MemberData(nameof(TodosOsStatusProduto))]
    public void Todo_StatusProduto_do_Domain_vira_o_ordinal(StatusProduto status)
        => JsonSerializer.Deserialize<ProdutoResumo>($$"""{"status":"{{status}}"}""", Opts)!
            .Status.Should().Be((int)status, $"'{status}' precisa de entrada no mapa do EnumStringOrIntConverter");

    [Theory]
    [MemberData(nameof(TodosOsTipoProduto))]
    public void Todo_TipoProduto_do_Domain_vira_o_ordinal(TipoProduto tipo)
        => JsonSerializer.Deserialize<ProdutoResumo>($$"""{"tipo":"{{tipo}}"}""", Opts)!
            .Tipo.Should().Be((int)tipo, $"'{tipo}' precisa de entrada no mapa do EnumStringOrIntConverter");

    [Fact]
    public void Valor_numerico_passa_direto()
    {
        var json = """{"pedidoId":"p1","fornecedorId":"f1","fornecedorNome":"F","status":3}""";
        JsonSerializer.Deserialize<PedidoAberto>(json, Opts)!.Status.Should().Be(3);
    }

    [Fact]
    public void Nome_desconhecido_nao_quebra_a_deserializacao()
    {
        var json = """{"pedidoId":"p1","fornecedorId":"f1","fornecedorNome":"F","status":"NovoStatusFuturo"}""";
        JsonSerializer.Deserialize<PedidoAberto>(json, Opts)!.Status.Should().Be(0);
    }

    private static readonly JsonSerializerOptions Opts = new(JsonSerializerDefaults.Web);
}
