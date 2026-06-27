namespace EasyStock.Application.UseCases.Inventario;

public sealed record CriarContagemCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid UsuarioId,
    EscopoContagem Escopo,
    Guid? EscopoRefId,
    ModoContagem Modo,
    EstrategiaLoteContagem EstrategiaLote,
    string? Observacao = null);

public sealed record ContagemResult(
    Guid Id,
    Guid EmpresaId,
    EscopoContagem Escopo,
    Guid? EscopoRefId,
    ModoContagem Modo,
    EstrategiaLoteContagem EstrategiaLote,
    StatusContagem Status,
    DateTime CriadoEm,
    DateTime? IniciadoEm,
    DateTime? FinalizadoEm,
    DateTime? AplicadoEm,
    string? Observacao,
    int TotalItens,
    int Conferidos);

/// <summary>Cria uma Contagem em Rascunho. Descoberta = V2 (rejeitada no MVP).</summary>
public class CriarContagemUseCase(
    IContagemRepository repo,
    IUnitOfWork uow,
    ILogger<CriarContagemUseCase> logger)
{
    public async Task<ContagemResult> ExecuteAsync(CriarContagemCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.UsuarioId, "UsuarioId");
        UseCaseGuards.EnsureSemTagsHtml(cmd.Observacao, "Observacao");
        if (cmd.EstrategiaLote == EstrategiaLoteContagem.Descoberta)
            throw new UseCaseValidationException("Estrategia 'Descoberta' ainda nao disponivel (V2). Use 'Guiado'.");

        // Contagem.Criar valida a coerencia escopo/EscopoRefId (RegraDeDominioVioladaException).
        var contagem = Contagem.Criar(
            cmd.EmpresaId, cmd.Escopo, cmd.EscopoRefId, cmd.Modo, cmd.EstrategiaLote, cmd.UsuarioId, cmd.Observacao);

        await repo.AddAsync(contagem);
        await uow.CommitAsync();

        logger.LogInformation("Contagem {Id} criada (escopo {Escopo}, modo {Modo}) por {Usuario}.",
            contagem.Id, cmd.Escopo, cmd.Modo, cmd.UsuarioId);
        return Map(contagem);
    }

    internal static ContagemResult Map(Contagem c) => new(
        c.Id, c.EmpresaId, c.Escopo, c.EscopoRefId, c.Modo, c.EstrategiaLote, c.Status,
        c.CriadoEm, c.IniciadoEm, c.FinalizadoEm, c.AplicadoEm, c.Observacao,
        c.Itens.Count, c.Itens.Count(i => i.Conferido));
}
