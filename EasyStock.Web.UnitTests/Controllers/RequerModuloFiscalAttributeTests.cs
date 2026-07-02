using EasyStock.Web.Infrastructure;
using EasyStock.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EasyStock.Web.UnitTests.Controllers;

/// <summary>
/// issue #770: com o módulo fiscal fora do escopo v1.0, o gate faz /notas-fiscais/*
/// responder 404 enquanto Features:FiscalHabilitado está off, e passar quando on.
/// </summary>
public class RequerModuloFiscalAttributeTests
{
    private static ActionExecutingContext BuildContext(bool fiscalHabilitado)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<FeaturesOptions>>(
            Options.Create(new FeaturesOptions { FiscalHabilitado = fiscalHabilitado }));

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new ActionExecutingContext(actionContext, new List<IFilterMetadata>(),
            new Dictionary<string, object?>(), controller: null!);
    }

    [Fact]
    public async Task FlagOff_Retorna404_SemExecutarAction()
    {
        var context = BuildContext(fiscalHabilitado: false);
        var nextChamado = false;

        await new RequerModuloFiscalAttribute().OnActionExecutionAsync(context, () =>
        {
            nextChamado = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        context.Result.Should().BeOfType<NotFoundResult>();
        nextChamado.Should().BeFalse("a action não deve rodar com o módulo fiscal desabilitado");
    }

    [Fact]
    public async Task FlagOn_ExecutaAction_SemCurtoCircuitar()
    {
        var context = BuildContext(fiscalHabilitado: true);
        var nextChamado = false;

        await new RequerModuloFiscalAttribute().OnActionExecutionAsync(context, () =>
        {
            nextChamado = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        context.Result.Should().BeNull("o gate não interfere quando o módulo fiscal está habilitado");
        nextChamado.Should().BeTrue();
    }
}
