using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using System.Runtime.CompilerServices;

namespace EasyStok.Mobile.Services;

/// <summary>
/// Helper estrutural pra eliminar crashes de UI causados por <c>async void</c>
/// sem try/catch. Toda excecao em handler de evento <em>tem</em> que passar
/// por <see cref="Fire"/> — captura, loga em <see cref="CrashLog"/> e mostra
/// um <see cref="Snackbar"/> nao-bloqueante. Sem isso, qualquer falha no
/// handler escapa do <c>SynchronizationContext</c> e o Android mata o
/// processo (AppDomain.UnhandledException registra mas nao impede o kill).
/// </summary>
public static class UiSafe
{
    private const int MaxMessageLength = 140;

    /// <summary>
    /// Use em event handlers de Page/Popup. Substitui <c>private async void
    /// OnX(...)</c> por <c>private void OnX(...) =&gt; UiSafe.Fire(async () =&gt; { ... })</c>.
    /// </summary>
    public static async void Fire(Func<Task> work, [CallerMemberName] string source = "")
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            CrashLog.Write($"UiSafe.Fire[{source}]", ex);
            await ShowSnackbarAsync(Truncate(ex.Message ?? ex.GetType().Name, MaxMessageLength));
        }
    }

    /// <summary>
    /// Snackbar de erro com cor accent (orange). Usado pelo BaseViewModel
    /// e por handlers que querem reportar falha sem propagar.
    /// </summary>
    public static async Task ShowSnackbarAsync(string message)
    {
        try
        {
            var options = new SnackbarOptions
            {
                BackgroundColor = Color.FromArgb("#E85814"),
                TextColor = Colors.White,
                CornerRadius = new CornerRadius(10),
            };
            var snackbar = Snackbar.Make(message, duration: TimeSpan.FromSeconds(5), visualOptions: options);
            await snackbar.Show();
        }
        catch
        {
            // Ultima linha — nem o snackbar saiu. Engole pra nao recursionar.
        }
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");
}
