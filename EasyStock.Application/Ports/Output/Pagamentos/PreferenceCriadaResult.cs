namespace EasyStock.Application.Ports.Output.Pagamentos;

/// <summary>Resultado da criação de Preference MercadoPago.</summary>
public sealed record PreferenceCriadaResult(
    string PreferenceId,
    string InitPointUrl);
