using EasyStock.Web.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.UnitTests.Controllers;

/// <summary>
/// ADR-0032 fatia 8: a rota antiga /configuracao-fiscal redireciona para a aba
/// fiscal de Configurações. Index() é uma expressão pura (não toca nas deps).
/// </summary>
public class ConfiguracaoFiscalRedirectTests
{
    [Fact]
    public void Index_redireciona_para_aba_fiscal()
    {
        var controller = new ConfiguracaoFiscalController(null!, null!);

        var result = controller.Index();

        result.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().Be("/configuracoes?tab=fiscal");
    }
}
