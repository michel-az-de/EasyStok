using System.Reflection;
using EasyStock.Api.Authorization;
using EasyStock.Api.Controllers;
using EasyStock.Api.Mobile.Controllers;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;

namespace EasyStock.Api.UnitTests.Authorization;

/// <summary>
/// #2448: cada rota de escrita da gestão mobile exige o nível e o módulo da mesma ação no web. A matriz fica
/// presa à ação web equivalente: se o web mudar, este teste aponta a rota mobile que ficou para trás.
/// </summary>
public class EscritaGestaoMobileAutorizacaoTests
{
    public static TheoryData<Type, string, Type, string> AcoesEEquivalenteWeb => new()
    {
        { typeof(MobileCashController), nameof(MobileCashController.Promover), typeof(CaixaController), nameof(CaixaController.RegistrarMovimento) },
        { typeof(MobileCashController), nameof(MobileCashController.Unlink), typeof(CaixaController), nameof(CaixaController.RegistrarMovimento) },
        { typeof(MobileProductsController), nameof(MobileProductsController.Approve), typeof(ProdutoController), nameof(ProdutoController.Create) },
        { typeof(MobileProductsController), nameof(MobileProductsController.Link), typeof(ProdutoController), nameof(ProdutoController.Create) },
        { typeof(MobileProductsController), nameof(MobileProductsController.Unlink), typeof(ProdutoController), nameof(ProdutoController.Create) },
        { typeof(MobileOrdersController), nameof(MobileOrdersController.Link), typeof(PedidosController), nameof(PedidosController.Create) },
        { typeof(MobileOrdersController), nameof(MobileOrdersController.Unlink), typeof(PedidosController), nameof(PedidosController.Create) },
        { typeof(MobileBatchesController), nameof(MobileBatchesController.Link), typeof(LotesController), nameof(LotesController.Create) },
        { typeof(MobileBatchesController), nameof(MobileBatchesController.Unlink), typeof(LotesController), nameof(LotesController.Create) },
        { typeof(MobileClientsController), nameof(MobileClientsController.Link), typeof(ClientesController), nameof(ClientesController.Create) },
        { typeof(MobileClientsController), nameof(MobileClientsController.Unlink), typeof(ClientesController), nameof(ClientesController.Create) },
        { typeof(MobileCalculadoraController), nameof(MobileCalculadoraController.CriarCompra), typeof(ListasComprasController), nameof(ListasComprasController.GerarPedidos) },
    };

    [Theory]
    [MemberData(nameof(AcoesEEquivalenteWeb))]
    public void Acao_mobile_exige_o_nivel_e_o_modulo_da_acao_web(Type mobile, string acao, Type web, string acaoWeb)
    {
        Nivel(mobile, acao).Should().NotBeNull().And.Be(Nivel(web, acaoWeb));
        ModulosConvention.ModulosDe(mobile, acao).Should().NotBeNull()
            .And.Equal(ModulosConvention.ModulosDe(web, acaoWeb));
    }

    [Fact]
    public void Backfill_de_vendas_exige_gerente_no_modulo_caixa()
    {
        // Gera vendas em lote (até 500 por chamada): mesmo módulo da venda no web, nível de quem estorna.
        Nivel(typeof(MobileOrdersController), nameof(MobileOrdersController.BackfillVendas)).Should().Be("Gerente");
        ModulosConvention.ModulosDe(typeof(MobileOrdersController), nameof(MobileOrdersController.BackfillVendas))
            .Should().Equal(ModulosConvention.ModulosDe(typeof(VendaController), nameof(VendaController.CriarVenda)))
            .And.Equal(Modulo.Caixa);
    }

    [Fact]
    public void Reconciliar_estoque_exige_o_modulo_do_ajuste_de_desacerto()
    {
        // A permissão GerenciarEstoque é conferida no corpo, como em EstoqueDesacertosController.Ajustar.
        ModulosConvention.ModulosDe(typeof(MobileProductsController), nameof(MobileProductsController.ReconcileStock))
            .Should().Equal(ModulosConvention.ModulosDe(typeof(EstoqueDesacertosController), nameof(EstoqueDesacertosController.Ajustar)))
            .And.Equal(Modulo.Producao);
    }

    [Theory]
    [InlineData(typeof(MobileCashController), nameof(MobileCashController.List))]
    [InlineData(typeof(MobileProductsController), nameof(MobileProductsController.List))]
    [InlineData(typeof(MobileBatchesController), nameof(MobileBatchesController.UploadPhoto))]
    [InlineData(typeof(MobileCalculadoraController), nameof(MobileCalculadoraController.Calcular))]
    public void Leitura_e_rotas_do_aparelho_seguem_transversais(Type controller, string acao) =>
        ModulosConvention.ModulosDe(controller, acao).Should().BeNull();

    private static string? Nivel(Type controller, string acao) =>
        controller.GetMethod(acao)!.GetCustomAttributes<AuthorizeAttribute>(true)
            .Select(a => a.Policy).SingleOrDefault(p => p is "Operador" or "Gerente" or "Admin");
}
