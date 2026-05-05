namespace EasyStok.Mobile.Services;

/// <summary>
/// Gerencia o tema (Dark/Light) da aplicacao. Persistido em Preferences
/// com chave <c>easystok.theme</c>. <see cref="Initialize"/> deve ser
/// chamado no App ctor antes do Shell montar pra evitar flash do tema
/// errado.
/// </summary>
public sealed class ThemeService
{
	private const string PreferenceKey = "easystok.theme";

	public AppTheme Current { get; private set; } = AppTheme.Dark;

	public void Initialize()
	{
		var raw = Preferences.Default.Get<string>(PreferenceKey, "dark");
		var theme = raw == "light" ? AppTheme.Light : AppTheme.Dark;
		Apply(theme, persist: false);
	}

	public void Apply(AppTheme theme, bool persist = true)
	{
		Current = theme;
		if (Application.Current is not null)
			Application.Current.UserAppTheme = theme;
		if (persist)
			Preferences.Default.Set(PreferenceKey, theme == AppTheme.Light ? "light" : "dark");
	}

	public void Toggle() =>
		Apply(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
}
