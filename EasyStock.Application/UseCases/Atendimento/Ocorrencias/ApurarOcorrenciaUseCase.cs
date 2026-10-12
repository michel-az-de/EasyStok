using EasyStock.Application.Ports.Output.Persistence.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Ocorrencias;

public sealed class ApurarOcorrenciaUseCase(IOcorrenciaRepository repo, IUnitOfWork uow, TimeProvider relogio)
{
    public async Task<OcorrenciaDto?> ExecuteAsync(Guid empresaId, Guid id, Guid usuarioId, string? nome, NivelAcesso nivel, CancellationToken ct = default)
    {
        ExigirGerente(nivel);
        UseCaseGuards.EnsureEmpresaId(empresaId);
        UseCaseGuards.EnsureNotEmpty(id, "OcorrenciaId");
        UseCaseGuards.EnsureNotEmpty(usuarioId, "UsuarioId");
        if (nome?.Length > 120) throw new UseCaseValidationException("Nome do responsável inválido.");
        return await uow.ExecuteInTransactionSemRetryAsync(async token =>
        {
            var o = await repo.ObterTravadaAsync(empresaId, id, token);
            if (o is null || o.EmpresaId != empresaId) return null;
            if (o.Apurar(usuarioId, nome, relogio.GetUtcNow().UtcDateTime)) await uow.CommitAsync();
            return OcorrenciaDto.De(o);
        }, ct);
    }

    internal static void ExigirGerente(NivelAcesso nivel)
    {
        if (nivel is not (NivelAcesso.Gerente or NivelAcesso.Admin or NivelAcesso.SuperAdmin))
            throw new UnauthorizedAccessException("Só a dona ou um gerente pode apurar e encerrar ocorrências.");
    }
}
