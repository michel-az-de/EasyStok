using EasyStock.Application.UseCases.AtualizarStatusPedido;

namespace EasyStock.Application.UseCases.Operacao.Kds;

/// <param name="OcorridoEm">Quando o toque aconteceu no aparelho (fila offline). Vai para o evento de auditoria.</param>
public sealed record KdsStatusLoteItem(Guid Id, string Status, DateTime? OcorridoEm = null);

public sealed record KdsStatusLoteResultado(Guid Id, bool Sucesso, string? Status, string? Erro);

public sealed record AtualizarStatusPedidosEmLoteCommand(
    Guid EmpresaId,
    IReadOnlyList<KdsStatusLoteItem> Itens,
    Guid? UsuarioId = null,
    string? Origem = "kds") : ICommand;

/// <summary>
/// Marcação em lote do KDS quando a internet volta (S19, US-042): aplica em sequência, cada item no seu
/// commit via <see cref="AtualizarStatusPedidoUseCase"/>, e não para no primeiro erro.
/// </summary>
public class AtualizarStatusPedidosEmLoteUseCase(
    AtualizarStatusPedidoUseCase atualizarStatus,
    IUnitOfWork uow,
    ILogger<AtualizarStatusPedidosEmLoteUseCase> logger)
{
    /// <summary>Teto por requisição: a fila offline de um tablet não chega perto disso.</summary>
    public const int MaximoItens = 100;

    public async Task<IReadOnlyList<KdsStatusLoteResultado>> ExecuteAsync(
        AtualizarStatusPedidosEmLoteCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        if (cmd.Itens.Count > MaximoItens)
            throw new UseCaseValidationException($"Lote acima de {MaximoItens} itens.");

        var resultados = new List<KdsStatusLoteResultado>(cmd.Itens.Count);
        foreach (var item in cmd.Itens)
        {
            ct.ThrowIfCancellationRequested();
            resultados.Add(await AplicarAsync(cmd, item));
        }
        return resultados;
    }

    private async Task<KdsStatusLoteResultado> AplicarAsync(AtualizarStatusPedidosEmLoteCommand cmd, KdsStatusLoteItem item)
    {
        try
        {
            var pedido = await atualizarStatus.ExecuteAsync(new AtualizarStatusPedidoCommand(
                cmd.EmpresaId, item.Id, item.Status, cmd.UsuarioId, Origem: cmd.Origem, OcorridoEm: item.OcorridoEm));
            return pedido is null
                ? new KdsStatusLoteResultado(item.Id, false, null, "Pedido não encontrado.")
                : new KdsStatusLoteResultado(item.Id, true, pedido.Status, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // O item que falhou não pode vazar alteração rastreada para o commit do próximo.
            uow.DescartarAlteracoesPendentes();
            logger.LogWarning(ex, "KDS lote: pedido {PedidoId} não mudou para {Status}.", item.Id, item.Status);
            var mensagem = ex is UseCaseValidationException or RegraDeDominioVioladaException
                ? ex.Message
                : "Falha ao atualizar o status.";
            return new KdsStatusLoteResultado(item.Id, false, null, mensagem);
        }
    }
}
