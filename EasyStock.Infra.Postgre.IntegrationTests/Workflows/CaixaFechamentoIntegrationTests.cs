using EasyStock.Application.Common;
using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.AbrirCaixa;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.FecharCaixa;
using EasyStock.Application.UseCases.ObterCaixaDia;
using EasyStock.Application.UseCases.RegistrarMovimentoCaixa;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Workflows;

/// <summary>
/// Fechamento de caixa contra Postgres real (issue #615). Garante que o marcador
/// MovimentoCaixa "fechamento" persiste numa coluna timestamptz: antes do fix ele gravava
/// DataMovimento com Kind=Unspecified (de DateOnly.ToDateTime(TimeOnly.MaxValue)), que o
/// Npgsql 9 rejeita, abortando o CommitAsync inteiro e deixando o caixa "fechado" na verdade
/// aberto. Tambem valida que GetAberturaPendenteAsync (#596) ve a sessao encerrada.
///
/// Mesmo wiring de producao do FinalizarVendaBalcaoIntegrationTests: DI real
/// (AddEasyStockApplication + AddEasyStockPostgreInfrastructure) e EasyStockDbContext scoped.
/// </summary>
public class CaixaFechamentoIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Caixa_agregado_reflete_a_sessao_apos_fechamento_de_loja_e_nova_abertura(bool aberturaOntem)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var lojaA = Guid.NewGuid();
        var lojaB = Guid.NewGuid();
        var agora = DateTime.UtcNow;
        var hoje = HorarioBrasil.DataOperacional(agora);
        var inicioDoDia = HorarioBrasil.JanelaDiaUtc(hoje).IniUtc;
        var instante = inicioDoDia.AddTicks((agora - inicioDoDia).Ticks / 2).AddDays(aberturaOntem ? -1 : 0);
        var instanteDaLoja = agora.AddDays(aberturaOntem ? -1 : 0);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Set<Empresa>().Add(new Empresa
            {
                Id = empresaId, Nome = "Empresa Caixa Agregado",
                Documento = $"{Random.Shared.Next(100000, 999999)}",
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
            });
            foreach (var lojaId in new[] { lojaA, lojaB })
                seed.Set<Loja>().Add(new Loja
                {
                    Id = lojaId, EmpresaId = empresaId, Nome = "Loja Caixa Agregado", Ativa = true,
                    CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
                });
            seed.MovimentosCaixa.Add(MovimentoCaixa.Criar(empresaId, "abertura", 100m, instante));
            seed.MovimentosCaixa.Add(MovimentoCaixa.Criar(empresaId, "abertura", 20m, instanteDaLoja, lojaA));
            await seed.SaveChangesAsync();
        }

        await using var provider = BuildProductionProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
            var obter = scope.ServiceProvider.GetRequiredService<ObterCaixaDiaUseCase>();
            var antes = await obter.ExecuteAsync(new ObterCaixaDiaQuery(empresaId, hoje));
            antes.Aberto.Should().BeTrue();
            antes.AberturaPendenteCrossDay.Should().Be(aberturaOntem);
            antes.SaldoInicial.Should().Be(120m, "sem loja continua sendo o agregado da empresa");
            await scope.ServiceProvider.GetRequiredService<FecharCaixaUseCase>()
                .ExecuteAsync(new FecharCaixaCommand(empresaId, LojaId: lojaA));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(empresaId);
            var repo = new CaixaRepository(db);
            (await repo.GetAberturaPendenteAsync(empresaId)).Should().BeNull();
            var registrar = scope.ServiceProvider.GetRequiredService<RegistrarMovimentoCaixaUseCase>();
            var lancar = () => registrar.ExecuteAsync(new RegistrarMovimentoCaixaCommand(
                empresaId, "entrada", 1m, ExigirCaixaAberto: true));
            await lancar.Should().ThrowAsync<UseCaseValidationException>()
                .WithMessage(RegistrarMovimentoCaixaUseCase.CaixaAbertoMensagem);

            var obter = scope.ServiceProvider.GetRequiredService<ObterCaixaDiaUseCase>();
            var agregado = await obter.ExecuteAsync(new ObterCaixaDiaQuery(empresaId, hoje));
            agregado.Aberto.Should().BeFalse("o GET nao pode oferecer lancamento recusado pela guarda da mesma sessao");
            agregado.Fechado.Should().BeFalse("fechar a loja nao cria snapshot global");
            agregado.AberturaPendenteCrossDay.Should().BeFalse();
            agregado.SaldoInicial.Should().Be(aberturaOntem ? 0m : 120m, "a correcao nao muda a soma financeira");
            var daLoja = await obter.ExecuteAsync(new ObterCaixaDiaQuery(empresaId, hoje, lojaA));
            daLoja.Aberto.Should().BeFalse();
            daLoja.Fechado.Should().Be(!aberturaOntem);
        }

        // Uma abertura nova VALIDA (outra loja, sem reabrir o mesmo caixa fechado) muda o ultimo evento.
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(empresaId);
            await scope.ServiceProvider.GetRequiredService<AbrirCaixaUseCase>()
                .ExecuteAsync(new AbrirCaixaCommand(empresaId, 5m, LojaId: lojaB));
            var obter = scope.ServiceProvider.GetRequiredService<ObterCaixaDiaUseCase>();
            var agregado = await obter.ExecuteAsync(new ObterCaixaDiaQuery(empresaId, hoje));
            agregado.Aberto.Should().BeTrue();
            agregado.Fechado.Should().BeFalse();
            agregado.SaldoInicial.Should().Be(aberturaOntem ? 5m : 125m);
            (await obter.ExecuteAsync(new ObterCaixaDiaQuery(empresaId, hoje, lojaB))).Aberto.Should().BeTrue();
            (await obter.ExecuteAsync(new ObterCaixaDiaQuery(empresaId, hoje, lojaA))).Aberto.Should().BeFalse();
            await scope.ServiceProvider.GetRequiredService<RegistrarMovimentoCaixaUseCase>()
                .ExecuteAsync(new RegistrarMovimentoCaixaCommand(empresaId, "entrada", 1m, ExigirCaixaAberto: true));
            (await new CaixaRepository(db).GetMovimentosDoDiaAsync(empresaId, hoje))
                .Count(m => m.Tipo == "entrada" && m.Valor == 1m).Should().Be(1);
        }
    }

    [SkippableFact]
    public async Task FecharCaixa_persiste_marcador_timestamptz_e_encerra_sessao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();

        // ── Seed: empresa + loja ───────────────────────────────────────────
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Set<Empresa>().Add(new Empresa
            {
                Id = empresaId,
                Nome = "Empresa Caixa",
                Documento = $"{Random.Shared.Next(100000, 999999)}",
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
            seed.Set<Loja>().Add(new Loja
            {
                Id = lojaId,
                EmpresaId = empresaId,
                Nome = "Loja Caixa",
                Ativa = true,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        await using var provider = BuildProductionProvider();

        // ── Abrir caixa ────────────────────────────────────────────────────
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(empresaId);
            var abrir = scope.ServiceProvider.GetRequiredService<AbrirCaixaUseCase>();
            await abrir.ExecuteAsync(new AbrirCaixaCommand(empresaId, 100m, lojaId));
        }

        // ── Fechar caixa: grava FechamentoCaixa + marcador "fechamento". ────
        // Sem o fix (#615), o CommitAsync lancaria por Kind=Unspecified em timestamptz.
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(empresaId);
            var fechar = scope.ServiceProvider.GetRequiredService<FecharCaixaUseCase>();
            var fech = await fechar.ExecuteAsync(new FecharCaixaCommand(empresaId, LojaId: lojaId));
            fech.Should().NotBeNull();
        }

        // ── Asserts em contexto fresco ─────────────────────────────────────
        await using (var assert = fixture.CreateDbContext())
        {
            assert.SetMobileTenantContext(empresaId);
            var hoje = HorarioBrasil.Hoje();
            var caixaRepo = new CaixaRepository(assert);

            // 1. FechamentoCaixa persistiu (a transacao NAO abortou).
            var fechamento = await caixaRepo.GetFechamentoDoDiaAsync(empresaId, hoje, lojaId);
            fechamento.Should().NotBeNull();

            // 2. O marcador "fechamento" persistiu e voltou do Postgres como UTC.
            var marcador = await assert.MovimentosCaixa
                .Where(m => m.EmpresaId == empresaId && m.Tipo == "fechamento")
                .SingleAsync();
            marcador.DataMovimento.Kind.Should().Be(DateTimeKind.Utc);

            // 3. A sessao e vista como encerrada por GetAberturaPendenteAsync (#596).
            var pendente = await caixaRepo.GetAberturaPendenteAsync(empresaId, lojaId);
            pendente.Should().BeNull();
        }
    }

    [SkippableFact]
    public async Task FecharCaixa_sessao_de_dia_anterior_data_no_dia_da_abertura_e_libera_hoje()
    {
        // Regressão #640: fechar (sem data, como a UI) uma sessão aberta ONTEM grava o snapshot
        // datado em ONTEM e libera o caixa de HOJE. Prova a *resolução da sessão* (pendente null +
        // ObterCaixaDia cross-day false), não só "AbrirCaixa não lança". Mock não reproduz isto.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var ontemUtc = DateTime.UtcNow.AddDays(-1);
        var ontem = HorarioBrasil.DataOperacional(ontemUtc);
        var hoje = HorarioBrasil.Hoje();

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Set<Empresa>().Add(new Empresa
            {
                Id = empresaId, Nome = "Empresa Caixa CrossDay",
                Documento = $"{Random.Shared.Next(100000, 999999)}",
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
            });
            seed.Set<Loja>().Add(new Loja
            {
                Id = lojaId, EmpresaId = empresaId, Nome = "Loja CrossDay", Ativa = true,
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
            });
            // Sessão aberta ontem (nunca fechada) + entrada ontem + entrada HOJE sob a mesma sessão.
            seed.MovimentosCaixa.Add(MovimentoCaixa.Criar(empresaId, "abertura", 100m, ontemUtc, lojaId));
            seed.MovimentosCaixa.Add(MovimentoCaixa.Criar(empresaId, "entrada", 50m, ontemUtc, lojaId));
            seed.MovimentosCaixa.Add(MovimentoCaixa.Criar(empresaId, "entrada", 70m, DateTime.UtcNow, lojaId));
            await seed.SaveChangesAsync();
        }

        await using var provider = BuildProductionProvider();

        // Fechar SEM data (a UI manda null): o servidor resolve a sessão aberta de ontem.
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(empresaId);
            var fechar = scope.ServiceProvider.GetRequiredService<FecharCaixaUseCase>();
            var fech = await fechar.ExecuteAsync(new FecharCaixaCommand(empresaId, LojaId: lojaId));

            fech.Data.Should().Be(ontem, "o fechamento é datado no dia civil da abertura");
            fech.SaldoInicial.Should().Be(100m);
            fech.TotalEntradasExtras.Should().Be(50m, "a entrada de HOJE (70) não entra no fechamento de ontem");
            fech.SaldoFinal.Should().Be(150m);
        }

        await using (var assert = fixture.CreateDbContext())
        {
            assert.SetMobileTenantContext(empresaId);
            var repo = new CaixaRepository(assert);

            (await repo.GetFechamentoDoDiaAsync(empresaId, ontem, lojaId))
                .Should().NotBeNull("o fechamento foi datado em ontem");
            (await repo.GetFechamentoDoDiaAsync(empresaId, hoje, lojaId))
                .Should().BeNull("não pode haver fechamento de hoje — senão a abertura de hoje seria bloqueada");
            (await repo.GetAberturaPendenteAsync(empresaId, lojaId))
                .Should().BeNull("a sessão foi resolvida (marcador de fechamento é o último evento)");

            var dia = await new ObterCaixaDiaUseCase(new CaixaSaldoCalculator(repo))
                .ExecuteAsync(new ObterCaixaDiaQuery(empresaId, hoje, lojaId));
            dia.AberturaPendenteCrossDay.Should().BeFalse("a sessão de ontem não está mais pendente");
            dia.Aberto.Should().BeFalse();
            dia.Fechado.Should().BeFalse();
        }

        // A abertura do caixa de HOJE deve funcionar (era o bug: "Caixa do dia já foi fechado").
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(empresaId);
            var abrir = scope.ServiceProvider.GetRequiredService<AbrirCaixaUseCase>();
            var act = async () => await abrir.ExecuteAsync(new AbrirCaixaCommand(empresaId, 0m, lojaId));
            await act.Should().NotThrowAsync();
        }
    }

    [SkippableFact]
    public async Task IndiceCoalescido_impede_segundo_fechamento_loja_null_no_mesmo_dia()
    {
        // Finding 5b/#640: o unique modelado pelo EF (EmpresaId, LojaId, Data) NÃO dedupa LojaId
        // nulo (NULL != NULL). O índice coalescido da migration AddUniqueFechamentoCaixaCoalescido
        // fecha a brecha — o 2º fechamento (empresa, loja-null, mesmo dia) deve bater em 23505.
        // EF InMemory não enforça isto; exige Postgres real.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var dia = HorarioBrasil.Hoje();

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Set<Empresa>().Add(new Empresa
            {
                Id = empresaId, Nome = "Empresa Dup",
                Documento = $"{Random.Shared.Next(100000, 999999)}",
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
            });
            seed.FechamentosCaixa.Add(FechamentoCaixa.Criar(empresaId, dia, 100m, 0m, 0m, 0m, 0m));
            await seed.SaveChangesAsync();
        }

        await using (var ctx = fixture.CreateDbContext())
        {
            // 2º fechamento para (empresa, loja-null, mesmo dia) → viola o índice coalescido.
            ctx.FechamentosCaixa.Add(FechamentoCaixa.Criar(empresaId, dia, 200m, 0m, 0m, 0m, 0m));
            var act = async () => await ctx.SaveChangesAsync();

            var ex = await act.Should().ThrowAsync<DbUpdateException>();
            ex.Which.InnerException.Should().BeOfType<PostgresException>()
                .Which.SqlState.Should().Be("23505", "índice único coalescido ix_fechamentos_caixa_dia_unica");
        }
    }

    private ServiceProvider BuildProductionProvider()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddMemoryCache(); // SubscriptionStatusCache (infra DI) depende de IMemoryCache.
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>());
        services.AddSingleton(Substitute.For<ICacheService>()); // ProdutoCacheInvalidator depende de ICacheService.
        services.AddEasyStockPostgreInfrastructure(fixture.ConnectionString, config);
        services.AddEasyStockApplication();
        return services.BuildServiceProvider();
    }
}
