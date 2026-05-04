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

	public static void SetBaseUrl(string url) =>
		Preferences.Default.Set(PreferenceKeyBaseUrl, url);
}
