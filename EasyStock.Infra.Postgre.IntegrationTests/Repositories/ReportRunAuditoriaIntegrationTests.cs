using EasyStock.Domain.Reporting;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Data.Interceptors;
using EasyStock.Infra.Postgre.Repositories.Reporting;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #2671: a <see cref="ReportRun"/> é a única entidade com <c>CriadoEm</c>/<c>AlteradoEm</c> em
/// <see cref="DateTimeOffset"/>, e o interceptor de auditoria gravava <see cref="DateTime"/> nelas. O change tracker
/// lançava InvalidCastException e o enfileiramento de relatório caía em 500.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class ReportRunAuditoriaIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    private EasyStockDbContext Contexto() => new(new DbContextOptionsBuilder<EasyStockDbContext>()
        .UseNpgsql(fixture.ConnectionString)
        .AddInterceptors(new AuditTimestampsInterceptor())
        .Options);

    [SkippableFact]
    public async Task Enfileirar_e_alterar_a_run_carimbam_a_auditoria()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var antes = DateTimeOffset.UtcNow.AddSeconds(-1);
        var run = ReportRun.Enqueue(Guid.NewGuid(), Guid.NewGuid(), "vendas.por-periodo", ReportCategoria.Vendas,
            ReportContexto.Tenant, "{}", "hash", ReportFormat.Csv, "1.0.0", TimeSpan.FromDays(7));

        await using (var db = Contexto())
            await new ReportRunRepository(db).AddAsync(run, CancellationToken.None);

        DateTimeOffset criadoEm;
        await using (var db = Contexto())
        {
            var gravada = await db.ReportRuns.SingleAsync(r => r.Id == run.Id);
            criadoEm = db.Entry(gravada).Property<DateTimeOffset>("CriadoEm").CurrentValue;
            criadoEm.Should().BeOnOrAfter(antes);
            db.Entry(gravada).Property<DateTimeOffset>("AlteradoEm").CurrentValue.Should().BeOnOrAfter(antes);

            gravada.TryStart(TimeSpan.FromMinutes(5)).Should().BeTrue();
            await db.SaveChangesAsync();
        }

        await using (var db = Contexto())
        {
            var alterada = await db.ReportRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id);
            alterada.Status.Should().Be(ReportStatus.Running);
            var entrada = db.Entry(await db.ReportRuns.SingleAsync(r => r.Id == run.Id));
            entrada.Property<DateTimeOffset>("CriadoEm").CurrentValue.Should().Be(criadoEm);
            entrada.Property<DateTimeOffset>("AlteradoEm").CurrentValue.Should().BeOnOrAfter(criadoEm);
        }
    }
}
