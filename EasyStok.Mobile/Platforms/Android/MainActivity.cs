using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Views;
using EasyStok.Mobile.Services;

namespace EasyStok.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        AndroidEnvironment.UnhandledExceptionRaiser += (s, e) =>
        {
            BootCrashLog.Write("AndroidEnvironment.UnhandledExceptionRaiser", e.Exception);
        };

        try
        {
            base.OnCreate(savedInstanceState);

            // Garante que statusbar e navigation bar fiquem com navy 900
            // (#06143A) durante toda a vida do app, nao so no splash.
            if (Window is not null)
            {
                var navy = Android.Graphics.Color.ParseColor("#06143A");
                Window.SetStatusBarColor(navy);
                Window.SetNavigationBarColor(navy);

                // Forca icones da status bar em claro (fundo escuro).
                if (OperatingSystem.IsAndroidVersionAtLeast(30))
                {
                    Window.InsetsController?.SetSystemBarsAppearance(0,
                        (int)WindowInsetsControllerAppearance.LightStatusBars);
                }
                else
                {
#pragma warning disable CA1422
                    Window.DecorView.SystemUiVisibility = (StatusBarVisibility)0;
#pragma warning restore CA1422
                }
            }
        }
        catch (Exception ex)
        {
            BootCrashLog.Write("MainActivity.OnCreate", ex);
            throw;
        }
    }
}
