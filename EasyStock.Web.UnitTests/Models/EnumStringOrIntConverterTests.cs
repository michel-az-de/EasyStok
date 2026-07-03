using System.Text.Json;
using EasyStock.Web.Models.Api;
using FluentAssertions;

namespace EasyStock.Web.UnitTests.Models;

/// <summary>
/// Trava o espelho literal de StatusPedidoFornecedor (issue 797): a Api serializa o
/// enum como string (JsonStringEnumConverter) e o Web, sem referencia ao Domain,
/// mapeia por nome. Antes o Map espelhava um enum inexistente ("StatusPedidoCompra")
/// e todo status virava 0 → badge "0" na UI.
/// </summary>
public class EnumStringOrIntConverterTests
{
    [Theory]
    [InlineData("Aberto", 1, "Aberto")]
    [InlineData("EmTransito", 2, "Em Trânsito")]
    [InlineData("Recebido", 3, "Recebido")]
    [InlineData("Cancelado", 4, "Cancelado")]
    [InlineData("RecebidoParcial", 5, "Recebido Parcial")]
    public void Nome_do_enum_da_Api_vira_valor_e_label_corretos(string nome, int esperado, string label)
    {
        var json = $$"""
            {"pedidoId":"p1","fornecedorId":"f1","fornecedorNome":"F","status":"{{nome}}"}
            """;
        var pedido = JsonSerializer.Deserialize<PedidoAberto>(json, Opts)!;

        pedido.Status.Should().Be(esperado);
        pedido.StatusLabel.Should().Be(label);
    }

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
