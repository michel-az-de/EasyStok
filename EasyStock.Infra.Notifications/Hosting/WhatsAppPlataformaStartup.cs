namespace EasyStock.Infra.Notifications.Hosting;

/// <summary>
/// Validação de startup do WhatsApp de plataforma (N6), chamada pelas duas hospedagens (API e Worker) para não
/// divergirem. Com <c>Notifications:WhatsApp:Plataforma:Provider=meta</c> a plataforma passa a "usar a Meta": exige
/// <c>PhoneNumberId</c> (só dígitos, até 32), <c>VerifyToken</c> próprio, e também token e <c>AppSecret</c> do app.
/// Com outro valor (padrão <c>stub</c>) não exige nada.
/// </summary>
public static class WhatsAppPlataformaStartup
{
    public const int PhoneNumberIdTamanhoMaximo = 32;

    public static bool UsaMeta(string? provider) =>
        string.Equals(provider?.Trim(), "meta", StringComparison.OrdinalIgnoreCase);

    public static void Validar(
        string? provider, string? phoneNumberId, string? verifyToken, string? accessToken, string? appSecret)
    {
        if (!UsaMeta(provider)) return;

        const string quando = "when Notifications:WhatsApp:Plataforma:Provider=meta.";
        var numero = phoneNumberId?.Trim();
        if (string.IsNullOrEmpty(numero)
            || numero.Length > PhoneNumberIdTamanhoMaximo
            || !numero.All(char.IsAsciiDigit))
            throw new InvalidOperationException(
                $"Notifications:WhatsApp:Plataforma:PhoneNumberId is required {quando} Use o id numérico da Meta (só dígitos, até {PhoneNumberIdTamanhoMaximo}).");
        if (string.IsNullOrWhiteSpace(verifyToken))
            throw new InvalidOperationException($"Notifications:WhatsApp:Plataforma:VerifyToken is required {quando}");
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException($"Notifications:WhatsApp:Meta:AccessToken is required {quando}");
        if (string.IsNullOrWhiteSpace(appSecret))
            throw new InvalidOperationException($"Notifications:WhatsApp:Meta:AppSecret is required {quando}");
    }

    public static void Validar(Microsoft.Extensions.Configuration.IConfiguration c) => Validar(
        c["Notifications:WhatsApp:Plataforma:Provider"],
        c["Notifications:WhatsApp:Plataforma:PhoneNumberId"],
        c["Notifications:WhatsApp:Plataforma:VerifyToken"],
        c["Notifications:WhatsApp:Meta:AccessToken"],
        c["Notifications:WhatsApp:Meta:AppSecret"]);
}
