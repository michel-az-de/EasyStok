using EasyStock.Api.BackgroundServices;
using EasyStock.Api.Configuration;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Infra.Postgre.IntegrationTests.Notifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Workflows;

/// <summary>
/// N12: o job de vencimento de contas só avisa quando <c>BackgroundJobs:EnableContaFinanceiraNotificacoes</c> está ligado
/// (padrão desligado, decisão do Felipe, Q3). Desligado, ele continua marcando a parcela vencida (regra de negócio) mas
/// não publica evento nem carimba o dedup, para o aviso não se perder quando alguém ligar.
/// </summary>
public class ContaFinanceiraNotificacoesDesligadasIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private readonly MotorNotificacoesSuporte _s = new(fixture);

    private async Task<(Guid Vencida, Guid VenceHoje)> SemearAsync(Guid empresaId)
    {
        var hoje = DateTime.UtcNow.Date;
        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        var categoria = CategoriaFinanceira.Criar(empresaId, "Despesa de teste", TipoCategoriaFinanceira.Despesa);
        db.CategoriasFinanceiras.Add(categoria);
        var conta = ContaPagar.Criar(empresaId, null, categoria.Id, "Conta de teste", hoje.AddDays(-5));
        conta.AdicionarParcela(1, 100m, hoje.AddDays(-2));
        conta.AdicionarParcela(2, 100m, hoje.AddHours(10));
        conta.Emitir();
        db.ContasPagar.Add(conta);
        await db.SaveChangesAsync();
        return (conta.Parcelas.Single(p => p.Numero == 1).Id, conta.Parcelas.Single(p => p.Numero == 2).Id);
    }

    [SkippableFact]
    public async Task PadraoMarcaParcelaVencidaENaoPublicaNemCarimba()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await _s.SemearEmpresaAsync();
        var (vencida, venceHoje) = await SemearAsync(empresa);
        var notificador = Substitute.For<INotificadorService>();
        await using var provider = _s.ConstruirProviderDeJobDaApi(papelRls: true,
            ajustar: services => services.AddScoped(_ => notificador));
        var job = new ContaFinanceiraVencimentoJob(provider, NullLogger<ContaFinanceiraVencimentoJob>.Instance);

        await job.ProcessarAsync(CancellationToken.None);

        await using var db = fixture.CreateDbContext();
        var parcelaVencida = await db.ParcelasPagar.AsNoTracking().IgnoreQueryFilters().SingleAsync(p => p.Id == vencida);
        parcelaVencida.Status.Should().Be(StatusParcela.Vencida, "marcar a parcela vencida é regra de negócio, não aviso");
        parcelaVencida.NotificadaVencidaEm.Should().BeNull("sem aviso, o carimbo não é gasto");
        (await db.ParcelasPagar.AsNoTracking().IgnoreQueryFilters().SingleAsync(p => p.Id == venceHoje)).NotificadaD1Em
            .Should().BeNull("as coortes D-3 e D-1 nem rodam");
        await notificador.DidNotReceiveWithAnyArgs().PublicarEventoAsync(default, default, default, default!, default, default);
        (await _s.LerEventosDaEmpresaAsync(empresa)).Should().BeEmpty("nenhum evento sai");
    }

    [SkippableFact]
    public async Task LigadoPublicaECarimba()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await _s.SemearEmpresaAsync();
        var (vencida, venceHoje) = await SemearAsync(empresa);
        var notificador = Substitute.For<INotificadorService>();
        await using var provider = _s.ConstruirProviderDeJobDaApi(papelRls: true,
            ajustar: services => services.AddScoped(_ => notificador));
        var opcoes = Options.Create(new BackgroundJobOptions { EnableContaFinanceiraNotificacoes = true });
        var job = new ContaFinanceiraVencimentoJob(provider, NullLogger<ContaFinanceiraVencimentoJob>.Instance, opcoes);

        await job.ProcessarAsync(CancellationToken.None);

        await using var db = fixture.CreateDbContext();
        (await db.ParcelasPagar.AsNoTracking().IgnoreQueryFilters().SingleAsync(p => p.Id == vencida)).NotificadaVencidaEm.Should().NotBeNull();
        (await db.ParcelasPagar.AsNoTracking().IgnoreQueryFilters().SingleAsync(p => p.Id == venceHoje)).NotificadaD1Em.Should().NotBeNull();
        await notificador.ReceivedWithAnyArgs(2).PublicarEventoAsync(default, default, default, default!, default, default);
    }
}
