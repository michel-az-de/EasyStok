using EasyStock.Application.Ports.Output.Reporting;

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

    public async Task<bool> ExecuteAsync(CancelReportRunCommand command)
    {
        var ct  = CancellationToken.None;
        var run = await _repo.GetByIdAsync(command.RunId, ct);
        if (run is null) return false;

        // Isolamento: só o próprio usuário ou SuperAdmin pode cancelar
        if (_user.Nivel != EasyStock.Domain.Enums.NivelAcesso.SuperAdmin &&
            run.UsuarioSolicitanteId != _user.UsuarioId)
            return false;

        run.RequestCancellation();
        return true;
    }
}
