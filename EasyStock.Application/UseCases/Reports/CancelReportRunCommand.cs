using EasyStock.Application.Ports.Output.Reporting;
using EasyStock.Domain.Reporting;

namespace EasyStock.Application.UseCases.Reports;

public sealed record CancelReportRunCommand(Guid RunId) : ICommand;

public sealed class CancelReportRunUseCase
    : IUseCase<CancelReportRunCommand, bool>
{
    private readonly IReportRunRepository _repo;
    private readonly ICurrentUserAccessor _user;

    public CancelReportRunUseCase(
        IReportRunRepository repo,
        ICurrentUserAccessor user)
    {
        _repo = repo;
        _user = user;
    }

    private const int MaxTentativasDeGravacao = 3;

    public async Task<bool> ExecuteAsync(CancelReportRunCommand command)
    {
        var ct = CancellationToken.None;

        // O Worker pode reclamar a run entre a leitura e a gravação (xmin). Nesse caso relê e reaplica:
        // a run que virou Running passa a Canceling, e a que já terminou lança InvalidReportStateException.
        for (var tentativa = 0; tentativa < MaxTentativasDeGravacao; tentativa++)
        {
            var run = await _repo.GetByIdAsync(command.RunId, ct);
            if (run is null) return false;

            // Isolamento: só o próprio usuário ou SuperAdmin pode cancelar
            if (_user.Nivel != EasyStock.Domain.Enums.NivelAcesso.SuperAdmin &&
                run.UsuarioSolicitanteId != _user.UsuarioId)
                return false;

            // Duplo clique em Cancelar: o pedido já está valendo, não há o que regravar.
            if (run.Status == ReportStatus.Canceling) return true;

            run.RequestCancellation();
            if (await _repo.UpdateAsync(run, ct)) return true;
        }

        throw new UseCaseConcurrencyException("Não foi possível gravar o cancelamento: a execução mudou várias vezes. Tente de novo.");
    }
}
