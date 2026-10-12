using EasyStock.Application.Ports.Output.Pagamentos;

namespace EasyStock.Infra.Integrations.Pagamentos.MercadoPago;

/// <summary>
/// Sem <c>MercadoPago:AccessToken</c> e sem simulador (I3.1, #1667): toda operação diz "não configurado" em
/// vez de ir ao provedor com token vazio e voltar 401. Vale também para as consultas: sem credencial não
/// se sabe se há pagamento (invariante 4), então o job de cobrança não pode concluir que não houve.
/// </summary>
public sealed class MercadoPagoNaoConfiguradoClient : IMercadoPagoClient
{
    public Task<PreferenceCriadaResult> CriarPreferenceAsync(CriarPreferenceCommand command, CancellationToken ct = default) =>
        throw new MercadoPagoNaoConfiguradoException();

    public Task<PagamentoMercadoPago?> ConsultarPagamentoAsync(string pagamentoId, CancellationToken ct = default) =>
        throw new MercadoPagoNaoConfiguradoException();

    public Task<IReadOnlyList<PagamentoMercadoPago>> BuscarPagamentosPorReferenciaAsync(string referenciaExterna, CancellationToken ct = default) =>
        throw new MercadoPagoNaoConfiguradoException();

    public Task<EstornoMercadoPagoResult> EstornarAsync(string pagamentoId, decimal? valor, string idempotencyKey, CancellationToken ct = default) =>
        throw new MercadoPagoNaoConfiguradoException();

    public Task<EstornoMercadoPagoResult?> ConsultarEstornoAsync(string pagamentoId, string estornoId, CancellationToken ct = default) =>
        throw new MercadoPagoNaoConfiguradoException();

    public Task<IReadOnlyList<EstornoMercadoPagoResult>> ListarEstornosAsync(string pagamentoId, CancellationToken ct = default) =>
        throw new MercadoPagoNaoConfiguradoException();

    public Task ExpirarPreferenciaAsync(string preferenceId, DateTime expiraEm, CancellationToken ct = default) =>
        throw new MercadoPagoNaoConfiguradoException();
}