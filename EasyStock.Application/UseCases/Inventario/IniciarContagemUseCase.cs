namespace EasyStock.Application.UseCases.Inventario;

public sealed record IniciarContagemCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid ContagemId);

/// <summary>
/// Rascunho -> EmAndamento. Materializa a membership: 1 ItemContagem por lote do escopo,
/// congelando o conjunto (edge: categoria muda no meio). O baseline (QtdSistemaNoMomento)
/// NAO e capturado aqui — e lido por item no RegistrarItem (modelo de delta).
/// </summary>
public class IniciarContagemUseCase(
    IContagemRepository repo,
    IUnitOfWork uow,
    ILogger<IniciarContagemUseCase> logger)
{
    public async Task<ContagemResult> ExecuteAsync(IniciarContagemCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.ContagemId, "ContagemId");

        var contagem = await repo.GetByIdComItensAsync(cmd.EmpresaId, cmd.ContagemId)
            ?? throw new UseCaseValidationException("Contagem nao encontrada.");

        contagem.Iniciar(DateTime.UtcNow); // valida Rascunho -> EmAndamento

        var lotes = await repo.GetLotesDoEscopoAsync(cmd.EmpresaId, contagem.Escopo, contagem.EscopoRefId);
        foreach (var lote in lotes)
        {
            contagem.Itens.Add(new ItemContagem
            {
                Id = Guid.NewGuid(),
                EmpresaId = cmd.EmpresaId,
                ContagemId = contagem.Id,
                ProdutoId = lote.ProdutoId,
                ItemEstoqueId = lote.Id,
                Conferido = false,
            });
        }

        await repo.UpdateAsync(contagem);
        await uow.CommitAsync();

        logger.LogInformation("Contagem {Id} iniciada: {N} lotes materializados no escopo {Escopo}.",
            contagem.Id, lotes.Count, contagem.Escopo);
        return CriarContagemUseCase.Map(contagem);
    }
}
