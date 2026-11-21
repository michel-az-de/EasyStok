using System.Reflection;
using EasyStock.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>#2200, banca de DoS: sem teto de corpo o Kestrel aceita 30 MB de JSON para um layout que só aceita 40 elementos.</summary>
public class EtiquetasPdfControllerLimitesTests
{
    [Fact]
    public void Gerar_LimitaOCorpoDaRequisicaoEmAte2MB()
    {
        var acao = typeof(EtiquetasPdfController).GetMethod(nameof(EtiquetasPdfController.Gerar))!;

        var limite = acao.GetCustomAttribute<RequestSizeLimitAttribute>();

        limite.Should().NotBeNull("sem teto o Kestrel aceita 30 MB de JSON");
        EtiquetasPdfController.LimiteCorpoBytes.Should().BeInRange(1, 2 * 1024 * 1024);
        BytesDoFiltro(limite!).Should().Be(EtiquetasPdfController.LimiteCorpoBytes);
    }

    /// <summary>O atributo não expõe o valor e o filtro é interno: lê o `Bytes` do filtro que o atributo cria para a requisição.</summary>
    private static long BytesDoFiltro(RequestSizeLimitAttribute atributo)
    {
        var tipoDoFiltro = typeof(RequestSizeLimitAttribute).Assembly.GetType("Microsoft.AspNetCore.Mvc.Filters.RequestSizeLimitFilter")!;
        var servicos = new ServiceCollection().AddLogging().AddTransient(tipoDoFiltro).BuildServiceProvider();
        var filtro = ((IFilterFactory)atributo).CreateInstance(servicos);
        return Convert.ToInt64(tipoDoFiltro.GetProperty("Bytes", BindingFlags.Public | BindingFlags.Instance)!.GetValue(filtro));
    }
}
