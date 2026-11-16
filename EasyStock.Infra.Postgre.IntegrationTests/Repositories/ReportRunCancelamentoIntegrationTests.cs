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
/// #2739: o DELETE de uma execução devolvia 204 sem gravar o cancelamento; a run seguia Pending e o Worker a rodava.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class ReportRunCancelamentoIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    private EasyStockDbContext Contexto() => new(new DbContextOptionsBuilder<EasyStockDbContext>()
        .UseNpgsql(fixture.ConnectionString)
        .AddInterceptors(new AuditTimestampsInterceptor())
        .Options);

    private static async Task<ReportRun> Enfileirar(EasyStockDbContext db, Guid usuario)
    {
        var run = ReportRun.Enqueue(Guid.NewGuid(), usuario, "vendas.por-periodo", ReportCategoria.Vendas,
            ReportContexto.Tenant, "{}", "hash", ReportFormat.Csv, "1.0.0", TimeSpan.FromDays(7));
        await new ReportRunRepository(db).AddAsync(run, CancellationToken.None);
        return run;
    }

    private static ICurrentUserAccessor Usuario(Guid id)
    {
        var user = Substitute.For<ICurrentUserAccessor>();
        user.UsuarioId.Returns(id);
        return user;
    }

    [SkippableFact]
    public async Task Cancelar_run_pendente_grava_o_status_cancelado()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var usuario = Guid.NewGuid();
        ReportRun run;
        await using (var db = Contexto()) run = await Enfileirar(db, usuario);

        await using (var db = Contexto())
        {
            var cancelou = await new CancelReportRunUseCase(new ReportRunRepository(db), Usuario(usuario))
                .ExecuteAsync(new CancelReportRunCommand(run.Id));
            cancelou.Should().BeTrue();
        }

        await using (var db = Contexto())
        {
            var lida = await db.ReportRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id);
            lida.Status.Should().Be(ReportStatus.Canceled);
            lida.FinishedAt.Should().NotBeNull();
        }
    }

    [SkippableFact]
    public async Task Cancelar_depois_do_worker_reclamar_a_run_grava_cancelando()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var usuario = Guid.NewGuid();
        ReportRun run;
        await using (var db = Contexto()) run = await Enfileirar(db, usuario);

        await using (var db = Contexto())
        {
            var reclamada = await db.ReportRuns.SingleAsync(r => r.Id == run.Id);
            reclamada.TryStart(TimeSpan.FromMinutes(5)).Should().BeTrue();
            await db.SaveChangesAsync();
        }

        await using (var db = Contexto())
        {
            (await new CancelReportRunUseCase(new ReportRunRepository(db), Usuario(usuario))
                .ExecuteAsync(new CancelReportRunCommand(run.Id))).Should().BeTrue();
        }

        await using (var db = Contexto())
            (await db.ReportRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id)).Status
                .Should().Be(ReportStatus.Canceling);
    }

    [SkippableFact]
    public async Task Gravar_com_xmin_velho_devolve_false_e_nao_sobrescreve_o_worker()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var usuario = Guid.NewGuid();
        ReportRun run;
        await using (var db = Contexto()) run = await Enfileirar(db, usuario);

        ReportRun lidaAntes;
        await using (var db = Contexto())
            lidaAntes = (await new ReportRunRepository(db).GetByIdAsync(run.Id, CancellationToken.None))!;

        await using (var db = Contexto())
        {
            var reclamada = await db.ReportRuns.SingleAsync(r => r.Id == run.Id);
            reclamada.TryStart(TimeSpan.FromMinutes(5));
            await db.SaveChangesAsync();
        }

        lidaAntes.RequestCancellation();
        await using (var db = Contexto())
            (await new ReportRunRepository(db).UpdateAsync(lidaAntes, CancellationToken.None)).Should().BeFalse();

        await using (var db = Contexto())
            (await db.ReportRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id)).Status
                .Should().Be(ReportStatus.Running);
    }
}
