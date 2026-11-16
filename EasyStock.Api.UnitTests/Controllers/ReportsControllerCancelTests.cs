using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Reporting;
using EasyStock.Application.UseCases.Reports;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Reporting;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>#2739: o DELETE de uma execução devolve 409 com texto claro quando o cancelamento não grava.</summary>
public class ReportsControllerCancelTests
{
    private readonly IReportRunRepository _repo = Substitute.For<IReportRunRepository>();
    private readonly ICurrentUserAccessor _user = Substitute.For<ICurrentUserAccessor>();
    private readonly Guid _usuario = Guid.NewGuid();

    public ReportsControllerCancelTests()
    {
        _user.UsuarioId.Returns(_usuario);
        _user.Nivel.Returns(NivelAcesso.Gerente);
    }

    private ReportsController Controller() => new(
        null!, null!, null!, new CancelReportRunUseCase(_repo, _user), null!, null!, null!, null!, null!, _user, null!);

    private ReportRun NovaRun() => ReportRun.Enqueue(Guid.NewGuid(), _usuario, "vendas.por-periodo",
        ReportCategoria.Vendas, ReportContexto.Tenant, "{}", "hash", ReportFormat.Csv, "1.0.0", TimeSpan.FromDays(7));

    [Fact]
    public async Task Tres_conflitos_de_gravacao_viram_409_e_nao_500()
    {
        var run = NovaRun();
        _repo.GetByIdAsync(run.Id, Arg.Any<CancellationToken>()).Returns(_ => NovaRun());
        _repo.UpdateAsync(Arg.Any<ReportRun>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await Controller().CancelRun(run.Id);

        var conflito = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflito.Value!.ToString().Should().Contain("Tente de novo");
    }

    [Fact]
    public async Task Cancelar_run_ja_em_cancelando_responde_204_como_o_primeiro_pedido()
    {
        var run = NovaRun();
        run.TryStart(TimeSpan.FromMinutes(5));
        run.RequestCancellation();
        _repo.GetByIdAsync(run.Id, Arg.Any<CancellationToken>()).Returns(run);

        var result = await Controller().CancelRun(run.Id);

        result.Should().BeOfType<NoContentResult>();
    }
}
