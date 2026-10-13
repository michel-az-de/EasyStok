namespace EasyStock.Application.Ports.Output.Pagamentos;

/// <summary>Resultado da criação de Preference MercadoPago.</summary>
/// <param name="ContaRecebedoraId"><c>collector_id</c> da preferência: a conta que vai receber (I3.2).</param>
public sealed record PreferenceCriadaResult(
    string PreferenceId,
    string InitPointUrl,
    string? ContaRecebedoraId = null);
