using System.Security.Claims;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NSubstitute;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Api.UnitTests.Http;

public class TenantDoStorefrontFilterTests
{
    private static readonly Guid Empresa = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Anonimo_com_slug_maiusculo_e_espaco_consulta_normalizado_e_fixa_tenant()
    {
        var repo = Substitute.For<IStorefrontRepository>();
        var tenant = Substitute.For<ITenantContextAccessor>();
        repo.GetBySlugAsync("loja", Arg.Any<CancellationToken>())
            .Returns(StorefrontEntity.Criar(Empresa, "loja", "Loja", 0m));

        var nextChamado = await Executar(repo, tenant, slug: "LOJA ", autenticado: false);

        await repo.Received(1).GetBySlugAsync("loja", Arg.Any<CancellationToken>());
        tenant.Received(1).SetCurrentTenant(Empresa);
        nextChamado.Should().BeTrue();
    }

    [Fact]
    public async Task Slug_inexistente_nao_fixa_tenant_e_segue_para_a_action()
    {
        var repo = Substitute.For<IStorefrontRepository>();
        var tenant = Substitute.For<ITenantContextAccessor>();
        repo.GetBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((StorefrontEntity?)null);

        var nextChamado = await Executar(repo, tenant, slug: "nao-existe", autenticado: false);

        tenant.DidNotReceive().SetCurrentTenant(Arg.Any<Guid>());
        nextChamado.Should().BeTrue();
    }

    [Fact]
    public async Task Usuario_autenticado_nao_consulta_repositorio_nem_troca_tenant()
    {
        var repo = Substitute.For<IStorefrontRepository>();
        var tenant = Substitute.For<ITenantContextAccessor>();

        var nextChamado = await Executar(repo, tenant, slug: "loja", autenticado: true);

        await repo.DidNotReceive().GetBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        tenant.DidNotReceive().SetCurrentTenant(Arg.Any<Guid>());
        nextChamado.Should().BeTrue();
    }

    private static async Task<bool> Executar(
        IStorefrontRepository repo, ITenantContextAccessor tenant, string slug, bool autenticado)
    {
        var httpContext = new DefaultHttpContext();
        if (autenticado)
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "u") }, "Test"));

        var routeData = new RouteData();
        routeData.Values["slug"] = slug;

        var ctx = new ActionExecutingContext(
            new ActionContext(httpContext, routeData, new ActionDescriptor()),
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: new object());

        // Filter é internal e a Api não expõe InternalsVisibleTo: reflection, como em ValidateEmpresaIdAttributeTests.
        var filterType = typeof(TenantDoStorefrontAttribute).Assembly
            .GetType("EasyStock.Api.Http.TenantDoStorefrontFilter")!;
        var filter = (IAsyncActionFilter)Activator.CreateInstance(filterType, repo, tenant)!;

        var chamado = false;
        await filter.OnActionExecutionAsync(ctx, () =>
        {
            chamado = true;
            return Task.FromResult(Substitute.For<ActionExecutedContext>(
                ctx, new List<IFilterMetadata>(), ctx.Controller));
        });
        return chamado;
    }
}
