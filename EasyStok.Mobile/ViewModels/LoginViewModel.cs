using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyStok.Mobile.Services;
using EasyStok.Mobile.Storage;
using Plugin.Fingerprint;
using Plugin.Fingerprint.Abstractions;

namespace EasyStok.Mobile.ViewModels;

public sealed partial class LoginViewModel : BaseViewModel
{
	private readonly IAuthService _auth;
	private readonly ISecureStore _store;

	[ObservableProperty]
	private string _email = string.Empty;

	[ObservableProperty]
	private string _senha = string.Empty;

	[ObservableProperty]
	private bool _biometricsAvailable;

	[ObservableProperty]
	private bool _biometricsEnabled;

	public LoginViewModel(IAuthService auth, ISecureStore store)
	{
		_auth = auth;
		_store = store;
	}

	public async Task InitializeAsync()
	{
		Email = await _store.GetEmailLastLoginAsync() ?? string.Empty;
		BiometricsEnabled = await _store.GetBiometricsEnabledAsync();
		BiometricsAvailable = await CrossFingerprint.Current.IsAvailableAsync(allowAlternativeAuthentication: false);

		// Se biometria ja esta autorizada e o ultimo refresh ainda eh valido,
		// oferece login automatico sem digitar senha.
		var refreshToken = await _store.GetRefreshTokenAsync();
		if (BiometricsEnabled && BiometricsAvailable && !string.IsNullOrEmpty(refreshToken))
		{
			await TryBiometricLoginAsync();
		}
	}

	[RelayCommand]
	private Task LoginAsync() => RunAsync(async () =>
	{
		if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Senha))
		{
			ErrorMessage = "Informe email e senha.";
			return;
		}

		var result = await _auth.LoginAsync(Email.Trim(), Senha, empresaId: null);
		if (!result.Success)
		{
			ErrorMessage = result.Error ?? "Falha no login.";
			return;
		}

		// Oferece habilitar biometria no primeiro login bem-sucedido em device
		// que tem hardware. (Application.MainPage foi deprecated em MAUI 9 —
		// usamos Windows[0].Page agora.)
		if (BiometricsAvailable && !BiometricsEnabled)
		{
			var page = Application.Current?.Windows?.FirstOrDefault()?.Page;
			var ok = page is not null && await page.DisplayAlert(
				"Biometria",
				"Quer entrar com digital nas proximas vezes?",
				"Sim", "Agora nao");
			if (ok)
			{
				await _store.SetBiometricsEnabledAsync(true);
				BiometricsEnabled = true;
			}
		}

		await NavigateAfterLoginAsync(result.Data!.Usuario.Nivel);
	});

	[RelayCommand]
	private Task BiometricLoginAsync() => RunAsync(TryBiometricLoginAsync);

	private async Task TryBiometricLoginAsync()
	{
		var req = new AuthenticationRequestConfiguration("Entrar no EasyStok",
			"Confirme sua identidade para usar a sessao salva.");
		var auth = await CrossFingerprint.Current.AuthenticateAsync(req);
		if (!auth.Authenticated)
		{
			ErrorMessage = auth.ErrorMessage ?? "Autenticacao biometrica cancelada.";
			return;
		}

		var refreshed = await _auth.RefreshAsync();
		if (!refreshed)
		{
			ErrorMessage = "Sessao expirou. Faca login com email e senha.";
			await _store.SetBiometricsEnabledAsync(false);
			BiometricsEnabled = false;
			return;
		}

		var usuario = await _store.GetUsuarioAsync();
		await NavigateAfterLoginAsync(usuario?.Nivel ?? "Operador");
	}

	private async Task NavigateAfterLoginAsync(string _nivel)
	{
		// Multi-tenant flow:
		// - Se JWT tem empresaId: salva e vai pra LojaPicker (que auto-escolhe se loja unica)
		// - Se JWT NAO tem empresaId (multi-empresa): vai pra TenantPicker
		var empresaId = await _auth.GetEmpresaIdFromTokenAsync();
		if (empresaId is null)
		{
			await Shell.Current.GoToAsync("//tenant-picker");
			return;
		}

		await _store.SetEmpresaIdAsync(empresaId.Value);
		await Shell.Current.GoToAsync("//loja-picker");
	}
}
