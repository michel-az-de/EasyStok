using EasyStock.Application.Ports.Output.Persistence.Storefront;

namespace EasyStock.Application.UseCases.Atendimento.Avisos;

/// <summary>Resultado da confirmação: <c>null</c> no <see cref="ConfirmadoPeloClienteEm"/> quando desfeita.</summary>
public sealed record ConfirmacaoEncomendaResult(Guid PedidoId, DateTime? ConfirmadoPeloClienteEm, bool Mudou);

/// <summary>
/// Marca ou desfaz a confirmação da encomenda pelo cliente (#2096). Quem marca é a equipe (botão "Cliente confirmou") ou o
/// agente, quando o cliente confirma na conversa. Encomenda confirmada sai do aviso "sem confirmação" da vigia.
/// Pedido de outra empresa responde como inexistente.
/// </summary>
public sealed class ConfirmarEncomendaPeloClienteUseCase(
    IPedidoStorefrontRepository pedidos,
    IUnitOfWork unitOfWork,
    TimeProvider relogio)
{
    /// <param name="clienteId">Quando vem do agente: só pedido deste cliente (o id do pedido vem do LLM).</param>
    public Task<ConfirmacaoEncomendaResult?> ConfirmarAsync(
        Guid empresaId, Guid pedidoId, CancellationToken ct = default, Guid? clienteId = null) =>
        AplicarAsync(empresaId, pedidoId, confirmar: true, clienteId, ct);

    public Task<ConfirmacaoEncomendaResult?> DesfazerAsync(Guid empresaId, Guid pedidoId, CancellationToken ct = default) =>
        AplicarAsync(empresaId, pedidoId, confirmar: false, clienteId: null, ct);

    private async Task<ConfirmacaoEncomendaResult?> AplicarAsync(
        Guid empresaId, Guid pedidoId, bool confirmar, Guid? clienteId, CancellationToken ct)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        return await unitOfWork.ExecuteInTransactionSemRetryAsync<ConfirmacaoEncomendaResult?>(async token =>
        {
            var pedido = await pedidos.GetForUpdateAsync(empresaId, pedidoId, token);
            if (pedido is null || pedido.EmpresaId != empresaId) return null;
            if (clienteId is { } cliente && pedido.ClienteId != cliente) return null;

            bool mudou;
            try
            {
                mudou = confirmar
                    ? pedido.ConfirmarPeloCliente(relogio.GetUtcNow().UtcDateTime)
                    : pedido.DesfazerConfirmacaoDoCliente();
            }
            catch (RegraDeDominioVioladaException ex)
            {
                throw new UseCaseValidationException(ex.Message);
            }

            if (mudou)
            {
                await pedidos.UpdateAsync(pedido, token);
                await unitOfWork.CommitAsync();
            }

            return new ConfirmacaoEncomendaResult(pedido.Id, pedido.ConfirmadoPeloClienteEm, mudou);
        }, ct);
    }
}
