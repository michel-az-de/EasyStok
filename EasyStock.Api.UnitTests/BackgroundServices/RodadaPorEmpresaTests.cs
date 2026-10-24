using EasyStock.Api.BackgroundServices;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Api.UnitTests.BackgroundServices;

/// <summary>
/// #2016: os jobs de estoque rodam uma vez por empresa, cada uma num escopo de DI próprio com o tenant ligado antes do
/// trabalho. Sem tenant o filtro global zerava toda leitura (#1588).
/// </summary>
public class RodadaPorEmpresaTests
{
    private static async IAsyncEnumerable<Empresa> Empresas(params Guid[] ids)
    {
        foreach (var id in ids) yield return new Empresa { Id = id, Nome = "Empresa" };
        await Task.CompletedTask;
    }

    private sealed class TenantRegistrado : ITenantContextAccessor
    {
        public Guid? Atual { get; private set; }
        public void SetCurrentTenant(Guid empresaId) => Atual = empresaId;
    }

    private static ServiceProvider Provider(params Guid[] empresas)
    {
        var repositorio = Substitute.For<IEmpresaRepository>();
        repositorio.StreamAllAsync(Arg.Any<CancellationToken>()).Returns(_ => Empresas(empresas));
        return new ServiceCollection()
            .AddSingleton(repositorio)
            .AddScoped<TenantRegistrado>()
            .AddScoped<ITenantContextAccessor>(sp => sp.GetRequiredService<TenantRegistrado>())
            .BuildServiceProvider();
    }

    [Fact]
    public async Task Cada_empresa_roda_no_proprio_escopo_com_o_tenant_ligado_antes_do_trabalho()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await using var provider = Provider(a, b);
        var vistos = new List<(Guid Empresa, Guid? Tenant, TenantRegistrado Escopo)>();

        var resultado = await RodadaPorEmpresa.ExecutarAsync(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ILogger>(),
            "teste",
            (sp, empresaId, _) =>
            {
                var tenant = sp.GetRequiredService<TenantRegistrado>();
                vistos.Add((empresaId, tenant.Atual, tenant));
                return Task.CompletedTask;
            },
            CancellationToken.None);

        resultado.Should().Be(new ResultadoRodadaPorEmpresa(2, 0));
        vistos.Select(v => v.Empresa).Should().Equal(a, b);
        vistos.Should().OnlyContain(v => v.Tenant == v.Empresa, "o tenant da empresa já está ligado quando o trabalho começa");
        vistos[0].Escopo.Should().NotBeSameAs(vistos[1].Escopo, "um escopo por empresa: nenhuma empresa herda o contexto da outra");
    }

    [Fact]
    public async Task Falha_numa_empresa_e_registrada_e_nao_para_as_outras()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await using var provider = Provider(a, b);
        var logger = Substitute.For<ILogger>();
        var rodadas = new List<Guid>();

        var resultado = await RodadaPorEmpresa.ExecutarAsync(
            provider.GetRequiredService<IServiceScopeFactory>(),
            logger,
            "teste",
            (_, empresaId, _) =>
            {
                rodadas.Add(empresaId);
                return empresaId == a ? throw new InvalidOperationException("quebrou") : Task.CompletedTask;
            },
            CancellationToken.None);

        resultado.Should().Be(new ResultadoRodadaPorEmpresa(2, 1));
        rodadas.Should().Equal(a, b);
        logger.ReceivedCalls().Should().Contain(c => c.GetMethodInfo().Name == nameof(ILogger.Log));
    }

    [Fact]
    public async Task Cancelamento_do_host_sobe_e_nao_vira_falha_da_empresa()
    {
        await using var provider = Provider(Guid.NewGuid());
        using var cancelamento = new CancellationTokenSource();

        var act = () => RodadaPorEmpresa.ExecutarAsync(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ILogger>(),
            "teste",
            (_, _, ct) =>
            {
                cancelamento.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            },
            cancelamento.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
