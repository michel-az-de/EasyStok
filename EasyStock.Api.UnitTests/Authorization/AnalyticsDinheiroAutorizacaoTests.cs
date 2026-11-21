using System.Reflection;
using EasyStock.Api.Authorization;
using EasyStock.Api.Controllers;
using EasyStock.Api.Mobile.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.UnitTests.Authorization;

/// <summary>
/// #2744 (decisão B do Felipe): faturamento do dia, ticket médio e valor em Pix são relatório. Quem não tem a permissão
/// de relatórios (Atendimento, Cozinha) não lê dinheiro por nenhum caminho de analytics; a projeção de estoque segue aberta.
/// </summary>
public class AnalyticsDinheiroAutorizacaoTests
{
    private static MethodInfo Acao(string rota) =>
        typeof(AnalyticsController).GetMethods()
            .Single(m => m.GetCustomAttributes<HttpGetAttribute>().Any(a => a.Template == rota));

    private static IEnumerable<string?> Politicas(string rota) =>
        Acao(rota).GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy);

    [Theory]
    [InlineData("dashboard-full")]
    [InlineData("dia")]
    [InlineData("movimentacoes")]
    [InlineData("dashboard-extras")]
    [InlineData("vendas-por-canal")]
    public void Caminho_com_dinheiro_exige_a_permissao_de_relatorios(string rota)
    {
        Politicas(rota).Should().Contain(PoliticasPermissao.VisualizarRelatorios);
    }

    // #2744: a lista de vendas, o detalhe e os itens trazem preço e total; o histórico de um item traz o valor de cada saída.
    [Theory]
    [InlineData(typeof(VendaController), nameof(VendaController.GetAll))]
    [InlineData(typeof(VendaController), nameof(VendaController.GetById))]
    [InlineData(typeof(VendaController), nameof(VendaController.GetItens))]
    [InlineData(typeof(MovimentacaoController), nameof(MovimentacaoController.GetByItem))]
    public void Leitura_de_venda_e_historico_do_item_exigem_a_permissao_de_relatorios(Type controller, string acao)
    {
        controller.GetMethod(acao)!.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy)
            .Should().Contain(PoliticasPermissao.VisualizarRelatorios);
    }

    // #2823: os relatórios rápidos do app seguem a mesma regra do site para valores de venda.
    [Theory]
    [InlineData(nameof(MobileQuickReportsController.VendasHoje))]
    [InlineData(nameof(MobileQuickReportsController.CaixaTurno))]
    [InlineData(nameof(MobileQuickReportsController.VendasVendedorTurno))]
    public void Relatorio_rapido_com_valor_de_venda_exige_a_permissao_de_relatorios(string acao)
    {
        PoliticasDaAcao(typeof(MobileQuickReportsController), acao)
            .Should().Contain(PoliticasPermissao.VisualizarRelatorios);
    }

    // Busca de estoque traz custo, e NFC-e do dia só contagens: seguem abertas à equipe.
    [Theory]
    [InlineData(nameof(MobileQuickReportsController.EstoqueBusca))]
    [InlineData(nameof(MobileQuickReportsController.NfceHoje))]
    public void Relatorio_rapido_sem_valor_de_venda_segue_aberto_a_equipe(string acao)
    {
        PoliticasDaAcao(typeof(MobileQuickReportsController), acao)
            .Should().NotContain(PoliticasPermissao.VisualizarRelatorios);
    }

    private static IEnumerable<string?> PoliticasDaAcao(Type controller, string acao) =>
        controller.GetMethod(acao)!.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy);

    [Fact]
    public void Projecao_de_estoque_segue_aberta_a_equipe()
    {
        Politicas("projecoes").Should().NotContain(PoliticasPermissao.VisualizarRelatorios);
    }
}
