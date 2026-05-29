namespace EasyStock.Application.UseCases.Faturas.CancelarFatura;

public sealed record CancelarFaturaCommand(
    Guid EmpresaId,
    Guid FaturaId,
    string? Motivo = null,
    Guid? UsuarioId = null,
    string? UsuarioNome = null,
    string? OrigemRegistro = "api"
);

public class CancelarFaturaUseCase(
    IFaturaRepository repo,
    IUnitOfWork uow,
    ILogger<CancelarFaturaUseCase> logger)
{
    public async Task ExecuteAsync(CancelarFaturaCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.FaturaId, nameof(cmd.FaturaId));

        var fatura = await repo.GetByIdAsync(cmd.EmpresaId, cmd.FaturaId, ct)
            ?? throw new UseCaseValidationException("Fatura nao encontrada.");

        var statusAntes = fatura.Status;

        try
        {
            fatura.Cancelar(cmd.Motivo);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }

        fatura.Eventos.Add(FaturaEvento.Criar(
            fatura.Id, TipoEventoFatura.Cancelada,
            usuarioId: cmd.UsuarioId, usuarioNome: cmd.UsuarioNome, origem: cmd.OrigemRegistro,
            valorAntes: statusAntes.ToString(),
            valorDepois: StatusFatura.Cancelada.ToString(),
            metadadosJson: cmd.Motivo is null ? null : System.Text.Json.JsonSerializer.Serialize(new { motivo = cmd.Motivo })
        ));

        await repo.UpdateAsync(fatura, ct);
        await uow.CommitAsync();

        logger.LogInformation("Fatura cancelada. FaturaId={FaturaId} Motivo={Motivo}", fatura.Id, cmd.Motivo ?? "(nao informado)");
    }
}
