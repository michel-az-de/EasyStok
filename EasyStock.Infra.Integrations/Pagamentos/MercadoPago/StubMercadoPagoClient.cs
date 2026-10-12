using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Pagamentos;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Integrations.Pagamentos.MercadoPago;

/// <summary>
/// Stub de desenvolvimento do MercadoPago — retorna URL fictícia imediatamente.
/// Registrado quando <c>MercadoPago:UseStub=true</c>, só fora de Production e só pela
/// <c>FabricaDeSimulador</c> (I3.1).
/// </summary>
public sealed class StubMercadoPagoClient(ILogger<StubMercadoPagoClient> logger) : IMercadoPagoClient, ISimulador
{
    public Task<EstornoMercadoPagoResult?> ConsultarEstornoAsync(string pagamentoId, string estornoId, CancellationToken ct = default) =>
        Task.FromResult<EstornoMercadoPagoResult?>(null);

    public Task<IReadOnlyList<EstornoMercadoPagoResult>> ListarEstornosAsync(string pagamentoId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EstornoMercadoPagoResult>>([]);

    public Task<PreferenceCriadaResult> CriarPreferenceAsync(
        CriarPreferenceCommand command,
        CancellationToken ct = default)
    {
        // S11: um pedido pode ter mais de uma cobrança (reemissão, troca de forma); a chave de
        // idempotência distingue cada uma e mantém o índice único (Provedor, ReferenciaExterna).
        var chave = command.IdempotencyKey ?? command.PedidoId.ToString();
        var preferenceId = $"stub-{chave}";
        var initPoint = $"https://stub.mp/{chave}";

        logger.LogInformation(
            "StubMercadoPago preference criada pedidoId={PedidoId} initPoint={InitPoint}",
            command.PedidoId, initPoint);

        return Task.FromResult(new PreferenceCriadaResult(preferenceId, initPoint, ContaSimulada));
    }

    /// <summary>Conta fixa do simulador: a mesma nas preferências e na consulta, para a cobrança não ficar aguardando credencial.</summary>
    public const string ContaSimulada = "stub-conta";

    public Task<string> ObterContaRecebedoraAsync(CancellationToken ct = default) => Task.FromResult(ContaSimulada);

    /// <summary>Stub não conhece pagamento nenhum: o webhook (que não chega em Development) seria ignorado.</summary>
    public Task<PagamentoMercadoPago?> ConsultarPagamentoAsync(string pagamentoId, CancellationToken ct = default) =>
        Task.FromResult<PagamentoMercadoPago?>(null);

    /// <summary>Sem pagamento no stub: o job de cobrança segue expirando normalmente.</summary>
    public Task<IReadOnlyList<PagamentoMercadoPago>> BuscarPagamentosPorReferenciaAsync(
        string referenciaExterna, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PagamentoMercadoPago>>([]);

    public Task<EstornoMercadoPagoResult> EstornarAsync(
        string pagamentoId, decimal? valor, string idempotencyKey, CancellationToken ct = default)
    {
        // I3.1: o simulador nunca devolve "approved"; quem consome vê que nenhum dinheiro voltou.
        logger.LogInformation("StubMercadoPago estorno simulado");
        return Task.FromResult(new EstornoMercadoPagoResult(
            $"stub-estorno-{Guid.NewGuid():N}", valor, EstornoMercadoPagoResult.Simulado));
    }

    public Task ExpirarPreferenciaAsync(string preferenceId, DateTime expiraEm, CancellationToken ct = default) =>
        Task.CompletedTask;
}
