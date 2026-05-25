namespace EasyStok.Mobile.Services;

/// <summary>
/// Configuracao da API. Inicialmente hardcoded — Suporte page (F5) permite
/// trocar em runtime via Preferences. Override por env build (Debug aponta
/// pra emulator-host 10.0.2.2 via PreprocessorDirective).
/// </summary>
public static class AppConfig
{
    public const string PreferenceKeyBaseUrl = "easystok.base_url";

#if DEBUG
    public const string DefaultBaseUrl = "http://10.0.2.2:5000";
#else
	public const string DefaultBaseUrl = "https://easystok.azurewebsites.net";
#endif

    public static string GetBaseUrl()
    {
        var custom = Preferences.Default.Get<string?>(PreferenceKeyBaseUrl, null);
        return string.IsNullOrEmpty(custom) ? DefaultBaseUrl : custom;
    }

    /// <summary>
    /// Persiste a URL custom. Em release exige HTTPS — anteriormente aceitava
    /// qualquer string, vetor de phishing/MITM se Suporte page fosse invocada
    /// com URL maliciosa. Em debug aceita HTTP (emulador/dev local).
    /// </summary>
    /// <exception cref="ArgumentException">URL invalida ou esquema nao suportado.</exception>
    public static void SetBaseUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL nao pode ser vazia.", nameof(url));

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            throw new ArgumentException($"URL invalida: '{url}'.", nameof(url));

#if DEBUG
        if (uri.Scheme is not ("http" or "https"))
            throw new ArgumentException(
                $"Esquema '{uri.Scheme}' nao suportado. Use http ou https.", nameof(url));
#else
		if (uri.Scheme != "https")
			throw new ArgumentException(
				$"Em release apenas HTTPS e aceito (esquema recebido: '{uri.Scheme}').", nameof(url));
#endif

        Preferences.Default.Set(PreferenceKeyBaseUrl, uri.ToString());
    }
}
