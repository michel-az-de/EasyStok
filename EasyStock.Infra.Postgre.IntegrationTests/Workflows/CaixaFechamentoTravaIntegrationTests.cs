using EasyStock.Application.Common;
using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.FecharCaixa;
using EasyStock.Application.UseCases.RegistrarMovimentoCaixa;
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
/// #2613: o fechamento e o lançamento manual disputam a mesma trava do caixa da loja (#1547). Sem ela, uma entrada
/// que passou pela guarda de dia fechado antes do retrato era gravada depois dele, num dia já fechado e fora do
/// fechamento. Aqui a trava é tomada por outra conexão, como faria um fechamento em curso.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class CaixaFechamentoTravaIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task Fechamento_espera_a_trava_do_caixa_da_loja()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (empresaId, lojaId) = await SemearCaixaAbertoAsync();
        await using var provider = BuildProductionProvider();

        await using var segura = fixture.CreateDbContext();
        await using var tx = await segura.Database.BeginTransactionAsync();
        await TravarAsync(segura, empresaId, lojaId);

        var fechar = Task.Run(() => Executar<FecharCaixaUseCase>(provider, empresaId,
            uc => uc.ExecuteAsync(new FecharCaixaCommand(empresaId, LojaId: lojaId))));
        var esperouATrava = await AlguemEsperaATravaAsync(fechar, empresaId, lojaId);
        await tx.RollbackAsync();
        await fechar;

        esperouATrava.Should().BeTrue("o retrato do dia só sai depois de quem segura o caixa da loja");
    }

    [SkippableFact]
    public async Task Entrada_que_esperou_um_fechamento_e_recusada()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (empresaId, lojaId) = await SemearCaixaAbertoAsync();
        await using var provider = BuildProductionProvider();

        // Outra conexão faz o papel do fechamento em curso: segura a trava e grava o retrato do dia.
        await using var segura = fixture.CreateDbContext();
        segura.SetMobileTenantContext(empresaId);
        await using var tx = await segura.Database.BeginTransactionAsync();
        await TravarAsync(segura, empresaId, lojaId);

        var entrada = Task.Run(() => Executar<RegistrarMovimentoCaixaUseCase>(provider, empresaId,
            uc => uc.ExecuteAsync(new RegistrarMovimentoCaixaCommand(empresaId, "entrada", 10m,
                Descricao: "Troco", LojaId: lojaId, Metodo: "dinheiro"))));
        var esperouATrava = await AlguemEsperaATravaAsync(entrada, empresaId, lojaId);

        segura.FechamentosCaixa.Add(FechamentoCaixa.Criar(empresaId, HorarioBrasil.Hoje(), 100m, 0m, 0m, 0m, 0m, lojaId));
        await segura.SaveChangesAsync();
        await tx.CommitAsync();

        await entrada.Awaiting(t => t).Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*já foi fechado*");
        await using var conferir = fixture.CreateDbContext();
        conferir.SetMobileTenantContext(empresaId);
        (await conferir.MovimentosCaixa.CountAsync(m => m.EmpresaId == empresaId && m.Tipo == "entrada"))
            .Should().Be(0, "o dinheiro não pode entrar num dia que o fechamento já retratou");
        esperouATrava.Should().BeTrue();
    }

    // Determinístico, sem cronômetro: a tarefa terminou sem esperar (sem trava) ou outra conexão ficou na fila
    // da trava do caixa desta loja. pg_locks guarda a chave de 64 bits partida em classid (alta) e objid (baixa).
    private async Task<bool> AlguemEsperaATravaAsync(Task tarefa, Guid empresaId, Guid? lojaId)
    {
        var chave = $"caixa-saida:{empresaId:N}:{lojaId:N}";
        await using var db = fixture.CreateDbContext();
        var limite = DateTime.UtcNow.AddSeconds(30);
        while (!tarefa.IsCompleted && DateTime.UtcNow < limite)
        {
            var esperando = await db.Database.SqlQuery<int>($@"SELECT count(*)::int AS ""Value"" FROM pg_locks
                WHERE locktype = 'advisory' AND NOT granted
                  AND objid::bigint = (hashtext({chave})::bigint & 4294967295)").SingleAsync();
            if (esperando > 0) return true;
            await Task.Delay(50);
        }
        return false;
    }

    [SkippableFact]
    public async Task Entrada_da_loja_espera_o_fechamento_da_empresa_e_e_recusada()
    {
        // #2629: o fechamento da empresa inteira (loja nula) soma os lançamentos de todas as lojas, então a entrada de
        // uma loja tem de esperar por ele também, não só pelo fechamento da própria loja.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (empresaId, lojaId) = await SemearCaixaAbertoAsync();
        await using var provider = BuildProductionProvider();

        await using var segura = fixture.CreateDbContext();
        segura.SetMobileTenantContext(empresaId);
        await using var tx = await segura.Database.BeginTransactionAsync();
        await TravarAsync(segura, empresaId, null);

        var entrada = Task.Run(() => Executar<RegistrarMovimentoCaixaUseCase>(provider, empresaId,
            uc => uc.ExecuteAsync(new RegistrarMovimentoCaixaCommand(empresaId, "entrada", 10m,
                Descricao: "Troco", LojaId: lojaId, Metodo: "dinheiro"))));
        var esperouATrava = await AlguemEsperaATravaAsync(entrada, empresaId, null);

        segura.FechamentosCaixa.Add(FechamentoCaixa.Criar(empresaId, HorarioBrasil.Hoje(), 100m, 0m, 0m, 0m, 0m));
        await segura.SaveChangesAsync();
        await tx.CommitAsync();

        await entrada.Awaiting(t => t).Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*já foi fechado*");
        esperouATrava.Should().BeTrue("a entrada da loja cai no retrato da empresa");
    }

    private static Task TravarAsync(EasyStockDbContext db, Guid empresaId, Guid? lojaId)
    {
        var chave = $"caixa-saida:{empresaId:N}:{lojaId:N}";
        return db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({chave}))");
    }

    private static async Task Executar<T>(ServiceProvider provider, Guid empresaId, Func<T, Task> acao) where T : notnull
    {
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
        await acao(scope.ServiceProvider.GetRequiredService<T>());
    }

    private async Task<(Guid EmpresaId, Guid LojaId)> SemearCaixaAbertoAsync()
    {
        var empresaId = Guid.NewGuid();
        var loja = Loja.Criar(empresaId, "Loja trava");
        await using var seed = fixture.CreateDbContext();
        seed.Empresas.Add(new Empresa { Id = empresaId, Nome = "Trava do caixa", Documento = Guid.NewGuid().ToString("N")[..14],
            CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        seed.Lojas.Add(loja);
        seed.SetMobileTenantContext(empresaId);
        seed.MovimentosCaixa.Add(MovimentoCaixa.Criar(empresaId, "abertura", 100m, DateTime.UtcNow.AddMinutes(-5), loja.Id));
        await seed.SaveChangesAsync();
        return (empresaId, loja.Id);
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
