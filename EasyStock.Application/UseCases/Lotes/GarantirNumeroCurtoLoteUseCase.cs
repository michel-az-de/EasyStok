namespace EasyStock.Application.UseCases.Lotes;

public sealed record GarantirNumeroCurtoLoteCommand(Guid EmpresaId, Guid LoteId);

public sealed record NumeroCurtoLoteResult(Guid LoteId, string NumeroCurto);

/// <summary>#2199 (D4): lote criado antes do número curto ganha o dele na primeira impressão; repetir devolve o mesmo.</summary>
public class GarantirNumeroCurtoLoteUseCase(ILoteNumeroCurtoRepository repositorio)
{
    public async Task<NumeroCurtoLoteResult?> ExecuteAsync(GarantirNumeroCurtoLoteCommand command, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(command.EmpresaId);
        var numero = await repositorio.GarantirAsync(command.EmpresaId, command.LoteId, ct);
        return numero is null ? null : new NumeroCurtoLoteResult(command.LoteId, numero);
    }
}
