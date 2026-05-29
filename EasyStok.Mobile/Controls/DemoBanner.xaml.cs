using EasyStok.Mobile.Services;

namespace EasyStok.Mobile.Controls;

public partial class DemoBanner : ContentView
{
	public DemoBanner()
	{
		InitializeComponent();
	}

	private void OnSairTapped(object? sender, TappedEventArgs e) =>
		UiSafe.Fire(async () =>
		{
			var services = (Application.Current as App)?.Handler?.MauiContext?.Services
				?? IPlatformApplication.Current?.Services;
			if (services is null) return;

			var auth = (IAutenticacaoService)services.GetService(typeof(IAutenticacaoService))!;
			var sync = (SyncEngine)services.GetService(typeof(SyncEngine))!;
			sync.Stop();
			await auth.SairAsync();
			await Shell.Current.GoToAsync("//login");
		});
}
