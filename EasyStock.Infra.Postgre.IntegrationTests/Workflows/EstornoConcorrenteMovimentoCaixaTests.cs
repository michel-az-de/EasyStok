using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.EstornarMovimentoCaixa;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Workflows;

/// <summary>
/// #2763: dois estornos do mesmo lançamento ao mesmo tempo (duplo clique, duas telas). Os dois leem o movimento antes
/// da trava do Caixa; quem chega depois tem de ver o estorno do primeiro e responder com ele, sem regravar autoria e
/// motivo e sem acusar erro.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class EstornoConcorrenteMovimentoCaixaTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableTheory]
    [InlineData("saida")]
    [InlineData("entrada")]
    public async Task Segundo_estorno_simultaneo_responde_com_o_primeiro(string tipo)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (empresaId, lojaId, movId) = await SemearAsync(tipo);
        await using var provider = BuildProductionProvider();

        // Outra conexão segura o Caixa, como um fechamento em curso, para os dois estornos lerem o movimento antes.
        await using var segura = fixture.CreateDbContext();
        segura.SetMobileTenantContext(empresaId);
        var tx = await segura.Database.BeginTransactionAsync();
        await TravaDoCaixaTeste.TomarComoOFechamentoAsync(segura, empresaId, lojaId);

        var ana = Task.Run(() => Estornar(provider, empresaId, movId, "Ana", "primeiro"));
        var bia = Task.Run(() => Estornar(provider, empresaId, movId, "Bia", "segundo"));
        var osDoisNaFila = await TravaDoCaixaTeste.EsperamATravaAsync(fixture, empresaId, 2, ana, bia);
        await tx.RollbackAsync();
        await tx.DisposeAsync();

        osDoisNaFila.Should().BeTrue("os dois estornos leram o movimento antes da trava");
        var respostas = await Task.WhenAll(ana, bia);
        respostas.Should().AllSatisfy(r => r.Should().NotBeNull());
        respostas.Select(r => (r!.EstornadoPorNome, r.MotivoEstorno)).Distinct()
            .Should().ContainSingle("quem chega depois responde com o estorno que venceu");

        await using var conferir = fixture.CreateDbContext();
        conferir.SetMobileTenantContext(empresaId);
        var gravado = await conferir.MovimentosCaixa.AsNoTracking().SingleAsync(m => m.Id == movId);
        gravado.EstornadoEm.Should().NotBeNull();
        (gravado.EstornadoPorNome, gravado.MotivoEstorno)
            .Should().Be((respostas[0]!.EstornadoPorNome, respostas[0]!.MotivoEstorno));
    }

    private static async Task<MovimentoCaixaResult?> Estornar(
        ServiceProvider provider, Guid empresaId, Guid movId, string nome, string motivo)
    {
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
        return await scope.ServiceProvider.GetRequiredService<EstornarMovimentoCaixaUseCase>()
            .ExecuteAsync(new EstornarMovimentoCaixaCommand(empresaId, movId, motivo, Guid.NewGuid(), nome, "web"));
    }

    private async Task<(Guid EmpresaId, Guid LojaId, Guid MovId)> SemearAsync(string tipo)
    {
        var empresaId = Guid.NewGuid();
        var loja = Loja.Criar(empresaId, "Loja estorno");
        await using var seed = fixture.CreateDbContext();
        seed.Empresas.Add(new Empresa { Id = empresaId, Nome = "Estorno simultaneo", Documento = Guid.NewGuid().ToString("N")[..14],
            CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        seed.Lojas.Add(loja);
        seed.SetMobileTenantContext(empresaId);
        seed.MovimentosCaixa.Add(MovimentoCaixa.Criar(empresaId, "abertura", 100m, DateTime.UtcNow.AddMinutes(-5), loja.Id));
        var mov = MovimentoCaixa.Criar(empresaId, tipo, 30m, DateTime.UtcNow.AddMinutes(-1), loja.Id);
        mov.Descricao = "Lançamento a estornar";
        mov.Metodo = "dinheiro";
        mov.Origem = "web";
        seed.MovimentosCaixa.Add(mov);
        await seed.SaveChangesAsync();
        return (empresaId, loja.Id, mov.Id);
    }

    private ServiceProvider BuildProductionProvider()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>());
        services.AddMemoryCache();
        services.AddSingleton(Substitute.For<ICacheService>());
        services.AddEasyStockPostgreInfrastructure(fixture.ConnectionString, config);
        services.AddEasyStockApplication();
        return services.BuildServiceProvider();
    }
}
