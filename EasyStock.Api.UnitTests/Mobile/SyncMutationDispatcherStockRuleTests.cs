using EasyStock.Api.Mobile.Services;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Mobile;

public class SyncMutationDispatcherStockRuleTests
{
    [Theory]
    [InlineData("pronto", true)]
    [InlineData("saiu_para_entrega", true)]
    [InlineData("entregue", true)]
    [InlineData("preparando", false)]
    [InlineData("aguardando", false)]
    [InlineData("cancelado", false)]
    public void StatusDescontaEstoque(string status, bool esperado)
        => SyncMutationDispatcher.StatusDescontaEstoque(status).Should().Be(esperado);
}
