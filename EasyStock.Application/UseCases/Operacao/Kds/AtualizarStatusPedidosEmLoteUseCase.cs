using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.AtualizarStatusPedido;

namespace EasyStock.Application.UseCases.Operacao.Kds;

/// <param name="OcorridoEm">Quando o toque aconteceu no aparelho (fila offline). Vai para o evento de auditoria.</param>
/// <param name="ConfirmarSaldoAReceber">#1863: "entregue" com saldo a receber só com a confirmação de quem marcou, como no PATCH.</param>
public sealed record KdsStatusLoteItem(Guid Id, string Status, DateTime? OcorridoEm = null, bool ConfirmarSaldoAReceber = false);

/// <param name="Codigo">#1863: <see cref="SaldoAReceberNaEntregaException.Codigo"/> quando o item parou no saldo a receber.</param>
/// <param name="Valor">Quanto falta receber, junto do código.</param>
public sealed record KdsStatusLoteResultado(Guid Id, bool Sucesso, string? Status, string? Erro, string? Codigo = null, decimal? Valor = null);

/// <param name="ConferirSaldoNaEntrega">#1863: o KDS confere o saldo a receber no "entregue"; o lote de papel
/// (registro do que já aconteceu na queda) não confere.</param>
public sealed record AtualizarStatusPedidosEmLoteCommand(
    Guid EmpresaId,
    IReadOnlyList<KdsStatusLoteItem> Itens,
    Guid? UsuarioId = null,
    string? Origem = "kds",
    NivelAcesso NivelSolicitante = NivelAcesso.Operador,
    bool ConferirSaldoNaEntrega = true) : ICommand;

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
                cmd.EmpresaId, item.Id, item.Status, cmd.UsuarioId, Origem: cmd.Origem, OcorridoEm: item.OcorridoEm,
                NivelSolicitante: cmd.NivelSolicitante,
                ConferirSaldoNaEntrega: cmd.ConferirSaldoNaEntrega, SaldoAReceberConfirmado: item.ConfirmarSaldoAReceber));
            return pedido is null
                ? new KdsStatusLoteResultado(item.Id, false, null, "Pedido não encontrado.")
                : new KdsStatusLoteResultado(item.Id, true, pedido.Status, null);
        }
        catch (SaldoAReceberNaEntregaException ex)
        {
            // #1863: não é falha; a tela da fila pergunta e reenvia o item confirmado.
            uow.DescartarAlteracoesPendentes();
            return new KdsStatusLoteResultado(item.Id, false, null, ex.Message, SaldoAReceberNaEntregaException.Codigo, ex.Valor);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // O item que falhou não pode vazar alteração rastreada para o commit do próximo.
            uow.DescartarAlteracoesPendentes();
            logger.LogWarning(ex, "KDS lote: pedido {PedidoId} não mudou para {Status}.", item.Id, item.Status);
            var mensagem = ex is UseCaseValidationException or RegraDeDominioVioladaException or UnauthorizedAccessException
                ? ex.Message
                : "Falha ao atualizar o status.";
            return new KdsStatusLoteResultado(item.Id, false, null, mensagem);
        }
    }
}
