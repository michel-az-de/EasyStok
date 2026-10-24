using EasyStock.Application.Services.Storefront;

namespace EasyStock.Application.Tests.Services.Storefront;

/// <summary>
/// #2007 (item 11): o retrato "[Entrega] ..." gravado nas observações do pedido é da equipe. O rascunho do link
/// mostra ao cliente só o que ele escreveu, e aplicar a alteração devolve o retrato ao pedido sem duplicar.
/// </summary>
public class EnderecoCheckoutObservacoesTests
{
    private static readonly EnderecoCheckout Endereco = new("05416000", "Rua Fradique Coutinho", "50", "Pinheiros", "São Paulo", "SP");

    [Theory]
    [InlineData("Sem cebola", "Sem cebola")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void Observacao_do_cliente_sai_sem_o_retrato_da_entrega(string? doCliente, string? esperado)
    {
        var gravada = Endereco.Snapshot(doCliente);

        gravada.Should().Contain("[Entrega]");
        EnderecoCheckout.SemRetratoDaEntrega(gravada).Should().Be(esperado);
    }

    [Fact]
    public void Alteracao_aplicada_devolve_o_retrato_ao_pedido_uma_vez()
    {
        var gravada = Endereco.Snapshot("Sem cebola");

        var aplicada = EnderecoCheckout.ComRetratoDaEntregaDe(gravada, "Sem cebola e sem alho");

        aplicada.Should().Be(Endereco.Snapshot("Sem cebola e sem alho"));
        EnderecoCheckout.ComRetratoDaEntregaDe(gravada, null).Should().Be(Endereco.Snapshot(null));
        EnderecoCheckout.ComRetratoDaEntregaDe("Sem cebola", "Sem alho").Should().Be("Sem alho");
    }
}
