using EasyStock.Domain.Sales;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Sales;

/// <summary>#3244: o estágio do cliente pela quantidade de compras, uma regra só para a API.</summary>
public class RegraDeEstagioTests
{
    [Theory]
    [InlineData(0, "sem_pedido")]
    [InlineData(1, "primeira_compra")]
    [InlineData(2, "recorrente")]
    [InlineData(4, "recorrente")]
    [InlineData(5, "fiel")]
    [InlineData(40, "fiel")]
    public void Estagio_vem_da_quantidade_de_compras(int compras, string esperado) =>
        RegraDeEstagio.De(compras).Should().Be(esperado);
}
