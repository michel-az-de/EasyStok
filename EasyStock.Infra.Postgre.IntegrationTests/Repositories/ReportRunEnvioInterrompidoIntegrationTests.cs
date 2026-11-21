using EasyStock.Application.Ports.Output;
using EasyStock.Application.Reporting;
using EasyStock.Application.UseCases.Reports;
using EasyStock.Domain.Reporting;
using EasyStock.Infra.Async.Storage;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Data.Interceptors;
using EasyStock.Infra.Postgre.Repositories.Reporting;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #2788: o Worker morre no meio do envio e o watchdog fecha a run sem sucesso (tentativas esgotadas ou cancelamento
/// pendente). Nenhuma tentativa vai concluir o envio e a run não aponta para o arquivo, então o watchdog o apaga.
/// Usa o storage local de produção, que escreve o envio em andamento ao lado da chave e só o renomeia ao fechar.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class ReportRunEnvioInterrompidoIntegrationTests(PostgreSqlDatabaseFixture fixture) : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "easystok-2788-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
            Directory.Delete(_raiz, recursive: true);
    }

    private EasyStockDbContext Contexto() => new(new DbContextOptionsBuilder<EasyStockDbContext>()
        .UseNpgsql(fixture.ConnectionString)
        .AddInterceptors(new AuditTimestampsInterceptor())
        .Options);

    private LocalFileStorage Storage()
    {
        var ambiente = Substitute.For<IHostEnvironment>();
        ambiente.ContentRootPath.Returns(_raiz);
        return new LocalFileStorage(Options.Create(new FileStorageOptions { LocalRootPath = _raiz }), ambiente);
    }

    private string[] ArquivosNoStorage() =>
        Directory.Exists(_raiz) ? Directory.GetFiles(_raiz, "*", SearchOption.AllDirectories) : [];

    /// <summary>Enfileira, põe em Running e, se pedido, cancela (vira Canceling). Devolve a run como gravada.</summary>
    private async Task<ReportRun> RunEmAndamento(int maxTentativas, bool cancelar)
    {
        var usuario = Guid.NewGuid();
        var run = ReportRun.Enqueue(Guid.NewGuid(), usuario, "vendas.por-periodo", ReportCategoria.Vendas,
            ReportContexto.Tenant, "{}", "hash", ReportFormat.Csv, "1.0.0", TimeSpan.FromDays(7), maxTentativas);
        await using (var db = Contexto()) await new ReportRunRepository(db).AddAsync(run, CancellationToken.None);

        await using (var db = Contexto())
        {
            var reclamada = await db.ReportRuns.SingleAsync(r => r.Id == run.Id);
            reclamada.TryStart(TimeSpan.FromMinutes(5)).Should().BeTrue();
            await db.SaveChangesAsync();
        }

        if (cancelar)
            await using (var db = Contexto())
            {
                var user = Substitute.For<ICurrentUserAccessor>();
                user.UsuarioId.Returns(usuario);
                (await new CancelReportRunUseCase(new ReportRunRepository(db), user)
                    .ExecuteAsync(new CancelReportRunCommand(run.Id))).Should().BeTrue();
            }

        return await Ler(run.Id);
    }

    /// <summary>
    /// O Worker morre no meio do envio: o sistema operacional fecha o arquivo e nenhum código do Worker conclui o envio.
    /// Fechar só o handle reproduz isso; o finalizador suprimido impede o stream de concluir o envio depois.
    /// </summary>
    private async Task EnvioInterrompido(ReportRun run)
    {
        var envio = (FileStream)await Storage().OpenUploadStreamAsync(
            ReportArtefato.Chave(run), ReportArtefato.ContentType(run.Format), CancellationToken.None);
        await envio.WriteAsync("data;valor\n2026-11-16;10\n"u8.ToArray());
        await envio.FlushAsync();
        envio.SafeFileHandle.Dispose();
        GC.SuppressFinalize(envio);
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

    private async Task Vigiar()
    {
        await using var db = Contexto();
        await new ReciclarExecucoesVencidasUseCase(new ReportRunRepository(db), Storage(),
                NullLogger<ReciclarExecucoesVencidasUseCase>.Instance)
            .ExecuteAsync(CancellationToken.None);
    }

    [SkippableFact]
    public async Task Watchdog_apaga_o_envio_interrompido_da_run_que_esgotou_as_tentativas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var run = await RunEmAndamento(maxTentativas: 1, cancelar: false);
        await EnvioInterrompido(run);
        ArquivosNoStorage().Should().ContainSingle("o envio interrompido fica no storage");
        await VencerOPrazo(run.Id);

        await Vigiar();

        (await Ler(run.Id)).Status.Should().Be(ReportStatus.Failed);
        ArquivosNoStorage().Should().BeEmpty("a run falhou de vez e nenhuma tentativa vai concluir o envio");
    }

    [SkippableFact]
    public async Task Watchdog_apaga_o_envio_interrompido_da_run_cancelada()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var run = await RunEmAndamento(maxTentativas: 3, cancelar: true);
        await EnvioInterrompido(run);
        await VencerOPrazo(run.Id);

        await Vigiar();

        (await Ler(run.Id)).Status.Should().Be(ReportStatus.Canceled);
        ArquivosNoStorage().Should().BeEmpty("a run foi cancelada e nenhuma tentativa vai concluir o envio");
    }

    [SkippableFact]
    public async Task Watchdog_mantem_o_envio_da_run_devolvida_a_fila()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var run = await RunEmAndamento(maxTentativas: 3, cancelar: false);
        await EnvioInterrompido(run);
        await VencerOPrazo(run.Id);

        await Vigiar();

        (await Ler(run.Id)).Status.Should().Be(ReportStatus.Pending);
        ArquivosNoStorage().Should().ContainSingle("a nova tentativa grava na mesma chave e não pode perder o envio");
    }
}
