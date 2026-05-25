namespace EasyStock.Application.Ports.Output.Pagamentos;

/// <summary>
/// Porta de saída para o gateway MercadoPago (ADR-0005).
/// Implementações: <c>MercadoPagoClient</c> (HTTP direto, sem SDK estático)
/// e <c>StubMercadoPagoClient</c> (ambiente Development).
/// </summary>
public interface IMercadoPagoClient
{
    /// <summary>
    /// Cria uma Preference MP e retorna a URL de checkout (init_point).
    /// Timeout de 5 s definido na implementação concreta (ADR-0005).
    /// </summary>
    Task<PreferenceCriadaResult> CriarPreferenceAsync(
        CriarPreferenceCommand command,
        CancellationToken ct = default);
}
