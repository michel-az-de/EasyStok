using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Reporting;
using EasyStock.Application.UseCases.Reports;
using EasyStock.Domain.Reporting;

namespace EasyStock.Application.Tests.UseCases.Reports;

/// <summary>
/// #2739: cancelar a execução mudava a run só em memória e o DELETE devolvia 204 sem gravar nada, então o Worker
/// rodava o relatório mesmo assim.
/// </summary>
public sealed class CancelReportRunUseCaseTests
{
    private readonly IReportRunRepository _repo = Substitute.For<IReportRunRepository>();
    private readonly ICurrentUserAccessor _user = Substitute.For<ICurrentUserAccessor>();
    private readonly Guid _usuario = Guid.NewGuid();

    public CancelReportRunUseCaseTests()
    {
        _user.UsuarioId.Returns(_usuario);
        _user.Nivel.Returns(NivelAcesso.Gerente);
        _repo.UpdateAsync(Arg.Any<ReportRun>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    private CancelReportRunUseCase UseCase() => new(_repo, _user);

    private ReportRun Run(Guid? dono = null, bool emExecucao = false)
    {
        var run = ReportRun.Enqueue(Guid.NewGuid(), dono ?? _usuario, "vendas.por-periodo", ReportCategoria.Vendas,
            ReportContexto.Tenant, "{}", "hash", ReportFormat.Csv, "1.0.0", TimeSpan.FromDays(7));
        if (emExecucao) run.TryStart(TimeSpan.FromMinutes(5));
        _repo.GetByIdAsync(run.Id, Arg.Any<CancellationToken>()).Returns(run);
        return run;
    }

    [Fact]
    public async Task Run_pendente_do_proprio_usuario_e_gravada_como_cancelada()
    {
        var run = Run();

        (await UseCase().ExecuteAsync(new CancelReportRunCommand(run.Id))).Should().BeTrue();

        await _repo.Received(1).UpdateAsync(
            Arg.Is<ReportRun>(r => r.Id == run.Id && r.Status == ReportStatus.Canceled), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_em_execucao_e_gravada_como_cancelando()
    {
        var run = Run(emExecucao: true);

        (await UseCase().ExecuteAsync(new CancelReportRunCommand(run.Id))).Should().BeTrue();

        await _repo.Received(1).UpdateAsync(
            Arg.Is<ReportRun>(r => r.Status == ReportStatus.Canceling), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_de_outro_usuario_nao_e_cancelada_nem_gravada()
    {
        var run = Run(dono: Guid.NewGuid());

        (await UseCase().ExecuteAsync(new CancelReportRunCommand(run.Id))).Should().BeFalse();

        await _repo.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task Run_inexistente_devolve_false_sem_gravar()
    {
        (await UseCase().ExecuteAsync(new CancelReportRunCommand(Guid.NewGuid()))).Should().BeFalse();

        await _repo.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task Run_em_estado_terminal_lanca_transicao_invalida_e_nao_grava()
    {
        var run = Run(emExecucao: true);
        run.MarkSucceeded("chave", 10, "sha", 1);

        var acao = () => UseCase().ExecuteAsync(new CancelReportRunCommand(run.Id));

        await acao.Should().ThrowAsync<InvalidReportStateException>();
        await _repo.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task Conflito_com_o_worker_relê_a_run_e_tenta_de_novo()
    {
        var run = Run();
        var releitura = ReportRun.Enqueue(Guid.NewGuid(), _usuario, "vendas.por-periodo", ReportCategoria.Vendas,
            ReportContexto.Tenant, "{}", "hash", ReportFormat.Csv, "1.0.0", TimeSpan.FromDays(7));
        _repo.GetByIdAsync(run.Id, Arg.Any<CancellationToken>()).Returns(run, releitura);
        _repo.UpdateAsync(Arg.Any<ReportRun>(), Arg.Any<CancellationToken>()).Returns(false, true);

        (await UseCase().ExecuteAsync(new CancelReportRunCommand(run.Id))).Should().BeTrue();

        await _repo.Received(2).GetByIdAsync(run.Id, Arg.Any<CancellationToken>());
        await _repo.Received(2).UpdateAsync(Arg.Any<ReportRun>(), Arg.Any<CancellationToken>());
    }
}
