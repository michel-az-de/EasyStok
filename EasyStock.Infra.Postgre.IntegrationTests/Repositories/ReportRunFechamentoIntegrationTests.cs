using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Reporting;
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
/// #2764: cancelar durante a geração tem de impedir a entrega. O fechamento do Worker decide pelo status gravado,
/// com a linha travada, e não pela entidade em memória (que ainda diz Running).
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class ReportRunFechamentoIntegrationTests(PostgreSqlDatabaseFixture fixture)
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

    /// <summary>Enfileira, põe em Running e devolve a run como o Worker a tem em memória (sem rastreamento).</summary>
    private async Task<ReportRun> EmExecucao(Guid usuario)
    {
        var nova = ReportRun.Enqueue(Guid.NewGuid(), usuario, "vendas.por-periodo", ReportCategoria.Vendas,
            ReportContexto.Tenant, "{}", "hash", ReportFormat.Csv, "1.0.0", TimeSpan.FromDays(7));
        await using (var db = Contexto())
            await new ReportRunRepository(db).AddAsync(nova, CancellationToken.None);

        await using (var db = Contexto())
        {
            var reclamada = await db.ReportRuns.SingleAsync(r => r.Id == nova.Id);
            reclamada.TryStart(TimeSpan.FromMinutes(5)).Should().BeTrue();
            await db.SaveChangesAsync();
        }

        await using (var db = Contexto())
            return (await new ReportRunRepository(db).GetByIdAsync(nova.Id, CancellationToken.None))!;
    }

    private async Task PedirCancelamento(ReportRun run, Guid usuario)
    {
        await using var db = Contexto();
        (await new CancelReportRunUseCase(new ReportRunRepository(db), Usuario(usuario))
            .ExecuteAsync(new CancelReportRunCommand(run.Id))).Should().BeTrue();
    }

    private async Task<ReportRun> Gravada(Guid id)
    {
        await using var db = Contexto();
        return await db.ReportRuns.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private async Task<ReportRunDesfecho> Fechar(ReportRun run, Action<ReportRun> desfecho)
    {
        await using var db = Contexto();
        return await new ReportRunRepository(db).FecharAsync(run, desfecho, CancellationToken.None);
    }

    [SkippableFact]
    public async Task Sucesso_depois_do_pedido_de_cancelamento_grava_cancelado()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var usuario = Guid.NewGuid();
        var run = await EmExecucao(usuario);
        await PedirCancelamento(run, usuario);

        var desfecho = await Fechar(run, r => r.MarkSucceeded("tenants/x/relatorio.csv", 10, "abc", 3));

        desfecho.Should().Be(ReportRunDesfecho.CanceladoPeloUsuario);
        var lida = await Gravada(run.Id);
        lida.Status.Should().Be(ReportStatus.Canceled);
        lida.ArtifactStorageKey.Should().BeNull();
    }

    [SkippableFact]
    public async Task Falha_nao_terminal_depois_do_pedido_de_cancelamento_nao_reenfileira()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var usuario = Guid.NewGuid();
        var run = await EmExecucao(usuario);
        await PedirCancelamento(run, usuario);

        var desfecho = await Fechar(run, r => r.MarkFailed("Erro", "Algo deu errado.", terminal: false));

        desfecho.Should().Be(ReportRunDesfecho.CanceladoPeloUsuario);
        (await Gravada(run.Id)).Status.Should().Be(ReportStatus.Canceled);
    }

    [SkippableFact]
    public async Task Falha_terminal_depois_do_pedido_de_cancelamento_nao_grava_falhou()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var usuario = Guid.NewGuid();
        var run = await EmExecucao(usuario);
        await PedirCancelamento(run, usuario);

        var desfecho = await Fechar(run, r => r.MarkFailed("Erro", "Parâmetros inválidos.", terminal: true));

        desfecho.Should().Be(ReportRunDesfecho.CanceladoPeloUsuario);
        (await Gravada(run.Id)).Status.Should().Be(ReportStatus.Canceled);
    }

    [SkippableFact]
    public async Task Sem_cancelamento_o_sucesso_e_gravado_com_a_chave_do_arquivo()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var run = await EmExecucao(Guid.NewGuid());

        var desfecho = await Fechar(run, r => r.MarkSucceeded("tenants/x/relatorio.csv", 10, "abc", 3));

        desfecho.Should().Be(ReportRunDesfecho.Gravado);
        var lida = await Gravada(run.Id);
        lida.Status.Should().Be(ReportStatus.Succeeded);
        lida.ArtifactStorageKey.Should().Be("tenants/x/relatorio.csv");
    }

    [SkippableFact]
    public async Task Tentativa_reciclada_devolve_tentativa_perdida_e_nao_grava()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var run = await EmExecucao(Guid.NewGuid());
        await using (var db = Contexto())
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE public.report_runs SET status = 0 WHERE id = {0}", run.Id);

        var chamado = false;
        var desfecho = await Fechar(run, r =>
        {
            chamado = true;
            r.MarkSucceeded("tenants/x/relatorio.csv", 10, "abc", 3);
        });

        desfecho.Should().Be(ReportRunDesfecho.TentativaPerdida);
        chamado.Should().BeFalse();
        var lida = await Gravada(run.Id);
        lida.Status.Should().Be(ReportStatus.Pending);
        lida.ArtifactStorageKey.Should().BeNull();
    }
}
