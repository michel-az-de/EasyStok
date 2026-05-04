using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using EasyStok.Mobile.Services;

namespace EasyStok.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	protected override void OnCreate(Bundle? savedInstanceState)
	{
		// Captura crashes da camada Java/native antes do MAUI inicializar.
		AndroidEnvironment.UnhandledExceptionRaiser += (s, e) =>
		{
			BootCrashLog.Write("AndroidEnvironment.UnhandledExceptionRaiser", e.Exception);
			// Nao marca como handled — deixa o sistema reportar crash normalmente
			// (assim o ANR/crash dialog aparece e o user sabe que algo deu errado).
		};

		try
		{
			base.OnCreate(savedInstanceState);
		}
		catch (Exception ex)
		{
			BootCrashLog.Write("MainActivity.OnCreate", ex);
			throw;
		}
	}
}
