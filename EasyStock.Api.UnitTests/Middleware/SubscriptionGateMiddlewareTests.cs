using EasyStock.Api.Middleware;
using EasyStock.Application.Ports.Output.Caching;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Caching;
using EasyStock.Infra.Postgre.Configuration;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using System.Security.Claims;

namespace EasyStock.Api.UnitTests.Middleware;

public class SubscriptionGateMiddlewareTests
{
    private static (SubscriptionGateMiddleware middleware,
                    ISubscriptionStatusCache cache,
                    IAssinaturaEmpresaRepository repo,
                    int[] nextCalled)
        Build(RequestDelegate? next = null)
    {
        var calledBox = new int[1];
        next ??= _ => { calledBox[0]++; return Task.CompletedTask; };
        var middleware = new SubscriptionGateMiddleware(next, NullLogger<SubscriptionGateMiddleware>.Instance);
        var cache = new SubscriptionStatusCache(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new CacheOptions { SubscriptionStatusDuration = TimeSpan.FromSeconds(60) }));
        var repo = Substitute.For<IAssinaturaEmpresaRepository>();
        return (middleware, cache, repo, calledBox);
    }

    private static HttpContext ContextoAutenticado(Guid empresaId, string nivel = "Operador", string path = "/api/produtos")
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("empresaId", empresaId.ToString()),
            new Claim("nivel", nivel),
        }, "test-auth");
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
        };
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    [Fact]
    public async Task Cache_hit_evita_segunda_query_no_repo()
    {
        var (mw, cache, repo, called) = Build();
        var empresa = Guid.NewGuid();
        repo.GetAtivaAsync(empresa).Returns(new AssinaturaEmpresa
        {
            EmpresaId = empresa,
            Status = StatusAssinatura.Ativa,
            DataInicio = DateTime.UtcNow.AddDays(-30),
            DataFim = DateTime.UtcNow.AddDays(30),
        });

        await mw.InvokeAsync(ContextoAutenticado(empresa), repo, cache);
        await mw.InvokeAsync(ContextoAutenticado(empresa), repo, cache);

        await repo.Received(1).GetAtivaAsync(empresa);
        called[0].Should().Be(2);
    }

    [Fact]
    public async Task Suspensa_retorna_402_e_nao_chama_next()
    {
        var (mw, cache, repo, called) = Build();
        var empresa = Guid.NewGuid();
        repo.GetAtivaAsync(empresa).Returns(new AssinaturaEmpresa
        {
            EmpresaId = empresa,
            Status = StatusAssinatura.Suspensa,
            DataInicio = DateTime.UtcNow.AddDays(-30),
        });

        var ctx = ContextoAutenticado(empresa);
        await mw.InvokeAsync(ctx, repo, cache);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status402PaymentRequired);
        called[0].Should().Be(0);
    }

    [Fact]
    public async Task Sem_assinatura_retorna_402_NO_SUBSCRIPTION()
    {
        var (mw, cache, repo, called) = Build();
        var empresa = Guid.NewGuid();
        repo.GetAtivaAsync(empresa).Returns((AssinaturaEmpresa?)null);

        var ctx = ContextoAutenticado(empresa);
        await mw.InvokeAsync(ctx, repo, cache);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status402PaymentRequired);
        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        body.Should().Contain("NO_SUBSCRIPTION");
        called[0].Should().Be(0);
    }

    [Fact]
    public async Task Trial_expirado_sem_plano_retorna_402_TRIAL_EXPIRED()
    {
        var (mw, cache, repo, called) = Build();
        var empresa = Guid.NewGuid();
        repo.GetAtivaAsync(empresa).Returns(new AssinaturaEmpresa
        {
            EmpresaId = empresa,
            Status = StatusAssinatura.Ativa,
            DataInicio = DateTime.UtcNow.AddDays(-30),
            TrialFim = DateTime.UtcNow.AddDays(-1),
            DataFim = null,
        });

        var ctx = ContextoAutenticado(empresa);
        await mw.InvokeAsync(ctx, repo, cache);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status402PaymentRequired);
        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        body.Should().Contain("TRIAL_EXPIRED");
        called[0].Should().Be(0);
    }

    [Fact]
    public async Task Trial_expirado_com_plano_pago_passa()
    {
        var (mw, cache, repo, called) = Build();
        var empresa = Guid.NewGuid();
        repo.GetAtivaAsync(empresa).Returns(new AssinaturaEmpresa
        {
            EmpresaId = empresa,
            Status = StatusAssinatura.Ativa,
            DataInicio = DateTime.UtcNow.AddDays(-30),
            TrialFim = DateTime.UtcNow.AddDays(-10),
            DataFim = DateTime.UtcNow.AddDays(20),
        });

        await mw.InvokeAsync(ContextoAutenticado(empresa), repo, cache);

        called[0].Should().Be(1);
    }

    [Fact]
    public async Task SuperAdmin_passa_sem_consultar_cache()
    {
        var (mw, cache, repo, called) = Build();
        var ctx = ContextoAutenticado(Guid.NewGuid(), nivel: "SuperAdmin");

        await mw.InvokeAsync(ctx, repo, cache);

        await repo.DidNotReceiveWithAnyArgs().GetAtivaAsync(default);
        called[0].Should().Be(1);
    }

    [Fact]
    public async Task Rota_em_whitelist_passa_sem_consultar_cache()
    {
        var (mw, cache, repo, called) = Build();
        var ctx = ContextoAutenticado(Guid.NewGuid(), path: "/api/auth/login");

        await mw.InvokeAsync(ctx, repo, cache);

        await repo.DidNotReceiveWithAnyArgs().GetAtivaAsync(default);
        called[0].Should().Be(1);
    }

    [Fact]
    public async Task Anonimo_passa_sem_consultar_cache()
    {
        var (mw, cache, repo, called) = Build();
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };
        ctx.Request.Path = "/api/produtos";
        ctx.Response.Body = new MemoryStream();

        await mw.InvokeAsync(ctx, repo, cache);

        await repo.DidNotReceiveWithAnyArgs().GetAtivaAsync(default);
        called[0].Should().Be(1);
    }

    [Fact]
    public async Task Invalidate_apos_hit_forca_nova_query()
    {
        var (mw, cache, repo, _) = Build();
        var empresa = Guid.NewGuid();
        repo.GetAtivaAsync(empresa).Returns(new AssinaturaEmpresa
        {
            EmpresaId = empresa,
            Status = StatusAssinatura.Ativa,
            DataInicio = DateTime.UtcNow.AddDays(-30),
            DataFim = DateTime.UtcNow.AddDays(30),
        });

        await mw.InvokeAsync(ContextoAutenticado(empresa), repo, cache);
        cache.Invalidate(empresa);
        await mw.InvokeAsync(ContextoAutenticado(empresa), repo, cache);

        await repo.Received(2).GetAtivaAsync(empresa);
    }
}
