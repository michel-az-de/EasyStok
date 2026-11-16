using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Reports;
using EasyStock.Domain.Reporting;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Data.Interceptors;
using EasyStock.Infra.Postgre.Repositories.Reporting;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #2773: o worker morria entre o pedido de cancelamento e o fechamento; a run ficava em Canceling
/// para sempre porque o watchdog só reciclava Running, e o enfileiramento devolvia a run presa.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class ReportRunWatchdogCancelingIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    private EasyStockDbContext Contexto() => new(new DbContextOptionsBuilder<EasyStockDbContext>()
        .UseNpgsql(fixture.ConnectionString)
        .AddInterceptors(new AuditTimestampsInterceptor())
        .Options);

    private static ICurrentUserAccessor Usuario(Guid id)
    {
        var user = Substitute.For<ICurrentUserAccessor>();
        user.UsuarioId.Returns(id);
        return user;
    }

    /// <summary>Enfileira, põe em Running e, se pedido, cancela (vira Canceling).</summary>
    private async Task<Guid> RunEmAndamento(bool cancelar)
    {
        var usuario = Guid.NewGuid();
        var run = ReportRun.Enqueue(Guid.NewGuid(), usuario, "vendas.por-periodo", ReportCategoria.Vendas,
            ReportContexto.Tenant, "{}", "hash", ReportFormat.Csv, "1.0.0", TimeSpan.FromDays(7));
        await using (var db = Contexto()) await new ReportRunRepository(db).AddAsync(run, CancellationToken.None);

        await using (var db = Contexto())
        {
            var reclamada = await db.ReportRuns.SingleAsync(r => r.Id == run.Id);
            reclamada.TryStart(TimeSpan.FromMinutes(5)).Should().BeTrue();
            await db.SaveChangesAsync();
        }

        if (cancelar)
            await using (var db = Contexto())
                (await new CancelReportRunUseCase(new ReportRunRepository(db), Usuario(usuario))
                    .ExecuteAsync(new CancelReportRunCommand(run.Id))).Should().BeTrue();

        return run.Id;
    }

    private async Task VencerOPrazo(Guid id)
    {
        await using var db = Contexto();
        var vencido = DateTimeOffset.UtcNow.AddMinutes(-10);
        await db.Database.ExecuteSqlAsync(
            $"UPDATE public.report_runs SET lease_expires_at = {vencido} WHERE id = {id}");
    }

    private async Task<ReportRun> Ler(Guid id)
    {
        await using var db = Contexto();
        return await db.ReportRuns.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private async Task Reclamar()
    {
        await using var db = Contexto();
        await new ReportRunRepository(db).ReclaimExpiredLeasesAsync(CancellationToken.None);
    }

    [SkippableFact]
    public async Task Watchdog_fecha_como_cancelada_a_run_cancelando_com_prazo_vencido()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var id = await RunEmAndamento(cancelar: true);
        (await Ler(id)).Status.Should().Be(ReportStatus.Canceling);
        await VencerOPrazo(id);

        await Reclamar();

        var lida = await Ler(id);
        lida.Status.Should().Be(ReportStatus.Canceled);
        lida.FinishedAt.Should().NotBeNull();
        lida.LeaseExpiresAt.Should().BeNull();
    }

    [SkippableFact]
    public async Task Watchdog_nao_toca_na_run_cancelando_com_prazo_valido()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var id = await RunEmAndamento(cancelar: true);

        await Reclamar();

        var lida = await Ler(id);
        lida.Status.Should().Be(ReportStatus.Canceling);
        lida.FinishedAt.Should().BeNull();
        lida.LeaseExpiresAt.Should().NotBeNull();
    }

    [SkippableFact]
    public async Task Watchdog_continua_devolvendo_a_pendente_a_run_em_execucao_com_prazo_vencido()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var id = await RunEmAndamento(cancelar: false);
        await VencerOPrazo(id);

        await Reclamar();

        var lida = await Ler(id);
        lida.Status.Should().Be(ReportStatus.Pending);
        lida.LeaseExpiresAt.Should().BeNull();
    }

    [SkippableFact]
    public async Task Watchdog_e_idempotente_depois_de_fechar_a_run_cancelando()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var id = await RunEmAndamento(cancelar: true);
        await VencerOPrazo(id);
        await Reclamar();
        var primeira = await Ler(id);

        await Reclamar();

        var segunda = await Ler(id);
        segunda.Status.Should().Be(primeira.Status);
        segunda.FinishedAt.Should().Be(primeira.FinishedAt);
        segunda.LeaseExpiresAt.Should().Be(primeira.LeaseExpiresAt);
    }
}
