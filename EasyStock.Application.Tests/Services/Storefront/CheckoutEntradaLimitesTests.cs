using EasyStock.Application.Services.Storefront;
using EasyStock.Application.UseCases.Atendimento.Comanda;

namespace EasyStock.Application.Tests.Services.Storefront;

/// <summary>
/// #1719: o checkout do site e o pedido do console aceitavam qualquer quantidade positiva. No laboratório,
/// um visitante sem login criou um pedido com 2.147.483.647 unidades e ocupou a vaga da janela.
/// </summary>
public class CheckoutEntradaLimitesTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public void Quantidade_acima_do_teto_e_recusada(int qtd)
    {
        var agir = () => CheckoutCoreService.ValidarEntrada("01310100", [(Guid.NewGuid(), qtd)]);

        agir.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*99*");
    }

    [Fact]
    public async Task Pratos_distintos_acima_do_teto_sao_recusados_antes_de_consultar()
    {
        var c = new CheckoutCoreServiceTests.Cenario();
        var ids = Enumerable.Range(0, CheckoutCoreService.MaximoPratosDistintos + 1).Select(_ => Guid.NewGuid()).ToList();

        var agir = () => c.Servico().CarregarItensCardapioAsync(c.Storefront.Id, ids);

        await agir.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage("*50 pratos*");
        await c.CardapioRepo.DidNotReceiveWithAnyArgs().ListarPorIdsAsync(default, default!, default);
    }

    [Fact]
    public void Quantidade_no_teto_passa()
    {
        CheckoutCoreService.ValidarEntrada("01310100", [(Guid.NewGuid(), CheckoutCoreService.QuantidadeMaximaPorItem)])
            .Should().Be("01310100");
    }

    [Fact]
    public void Prazo_das_janelas_consulta_cada_prato_uma_vez_e_ate_o_teto_da_comanda()
    {
        var repetido = Guid.NewGuid();
        var pedidos = Enumerable.Repeat(repetido, 40).Concat(Enumerable.Range(0, 200).Select(_ => Guid.NewGuid())).ToList();

        var consultados = ListarJanelasAtendimentoUseCase.ItensParaPrazo(pedidos);

        consultados.Should().OnlyHaveUniqueItems();
        consultados.Should().HaveCount(ListarJanelasAtendimentoUseCase.MaximoItens);
        consultados.Should().Contain(repetido);
    }
}
