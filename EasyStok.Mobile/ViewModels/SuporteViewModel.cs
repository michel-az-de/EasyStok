using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyStok.Mobile.Services;
using EasyStok.Mobile.Storage;

namespace EasyStok.Mobile.ViewModels;

public sealed partial class SuporteViewModel : BaseViewModel
{
	private readonly IAuthService _auth;
	private readonly ISecureStore _store;
	private readonly ThemeService _theme;
	private readonly SyncEngine _sync;

	[ObservableProperty]
	private string _sessaoLabel = "—";

	[ObservableProperty]
	private string _empresaLabel = "—";

	[ObservableProperty]
	private bool _isDemo;

	[ObservableProperty]
	private bool _isDarkTheme = true;

	[ObservableProperty]
	private string _baseUrl = string.Empty;

	[ObservableProperty]
	private string _appVersion = string.Empty;

	[ObservableProperty]
	private string _deviceLabel = string.Empty;

	[ObservableProperty]
	private string _statusBackend = string.Empty;

	[ObservableProperty]
	private bool _temCrashLog;

	public SuporteViewModel(IAuthService auth, ISecureStore store, ThemeService theme, SyncEngine sync)
	{
		_auth = auth;
		_store = store;
		_theme = theme;
		_sync = sync;
	}

	public async Task InitializeAsync()
	{
		IsDemo = await _auth.IsDemoAsync();
		if (IsDemo)
		{
			SessaoLabel = "Modo demo offline";
		}
		else
		{
			var usuario = await _store.GetUsuarioAsync();
			SessaoLabel = usuario?.Email ?? "Sessão sem dados";
		}

		var empresa = await _store.GetEmpresaIdAsync();
		var loja = await _store.GetLojaIdAsync();
		EmpresaLabel = empresa is null
			? "Sem empresa"
			: $"Empresa {empresa.ToString()![..8]}… · Loja {loja?.ToString()?[..8] ?? "—"}…";

		IsDarkTheme = _theme.Current == AppTheme.Dark;
		BaseUrl = AppConfig.GetBaseUrl();
		AppVersion = $"{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";
		DeviceLabel = $"{DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model} · Android {DeviceInfo.Current.VersionString}";

		var log = CrashLog.ReadAll();
		TemCrashLog = !string.IsNullOrEmpty(log);
	}

	partial void OnIsDarkThemeChanged(bool value)
	{
		_theme.Apply(value ? AppTheme.Dark : AppTheme.Light);
	}

	[RelayCommand]
	private Task SalvarBaseUrlAsync() => RunAsync(async () =>
	{
		if (string.IsNullOrWhiteSpace(BaseUrl) || !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
		{
			ErrorMessage = "URL inválida.";
			return;
		}
		AppConfig.SetBaseUrl(BaseUrl.Trim());
		StatusBackend = "Salvo. Reabra o app para reconectar.";
		await Task.CompletedTask;
	});

	[RelayCommand]
	private Task VerCrashLogAsync() => RunAsync(async () =>
	{
		var log = CrashLog.ReadAll() ?? "Sem crashes registrados.";
		// Truncar se for muito grande para nao travar o DisplayAlert
		if (log.Length > 4000) log = log[^4000..] + "\n\n…(truncado)";
		var page = Application.Current?.Windows?.FirstOrDefault()?.Page;
		if (page is not null)
			await page.DisplayAlert("Crash log", log, "OK");
	});

	[RelayCommand]
	private Task LimparCrashLogAsync() => RunAsync(async () =>
	{
		CrashLog.Clear();
		TemCrashLog = false;
		StatusBackend = "Logs apagados.";
		await Task.CompletedTask;
	});

	[RelayCommand]
	private Task AbrirWebAsync() => RunAsync(async () =>
	{
		await Launcher.Default.OpenAsync(new Uri("https://easystok.azurewebsites.net"));
	});

	[RelayCommand]
	private Task SairAsync() => RunAsync(async () =>
	{
		_sync.Stop();
		await _auth.LogoutAsync();
		await Shell.Current.GoToAsync("//login");
	});
}
