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
        AtualizarStatusBar(theme);
    }

    private static void AtualizarStatusBar(AppTheme theme)
    {
#if ANDROID
        try
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity?.Window is null) return;

            // PWA tem fundo navy fixo (#111827), entao status bar combina com
            // ele independente do tema MAUI (que so afeta as telas nativas
            // LoginPage / SuportePage).
            var hex = "#111827";
            var color = Android.Graphics.Color.ParseColor(hex);
            activity.Window.SetStatusBarColor(color);
            activity.Window.SetNavigationBarColor(color);

            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                var lightIcons = theme == AppTheme.Light;
                var mask = (int)Android.Views.WindowInsetsControllerAppearance.LightStatusBars;
                activity.Window.InsetsController?.SetSystemBarsAppearance(lightIcons ? mask : 0, mask);
            }
        }
        catch { /* status bar e cosmetico — nao quebra app */ }
#endif
    }

    public void Toggle() =>
        Apply(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
}
