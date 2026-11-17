using System.Reflection;
using EasyStock.Api.Authorization;
using EasyStock.Api.Controllers;
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

    [Fact]
    public void Projecao_de_estoque_segue_aberta_a_equipe()
    {
        Politicas("projecoes").Should().NotContain(PoliticasPermissao.VisualizarRelatorios);
    }
}
