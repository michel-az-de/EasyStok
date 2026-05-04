using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyStok.Mobile.Services;
using EasyStok.Mobile.Storage;

namespace EasyStok.Mobile.ViewModels;

public sealed partial class LoginViewModel : BaseViewModel
{
	private readonly IAuthService _auth;
	private readonly ISecureStore _store;

	[ObservableProperty]
	private string _email = string.Empty;

	[ObservableProperty]
	private string _senha = string.Empty;

	// Biometria temporariamente desativada — Plugin.Fingerprint 3.0.0-beta.1
	// causou crash de startup em alguns devices. Volta na F5 com BiometricPrompt
	// nativo. Mantemos as properties pra UI continuar com bind valido.
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
		try
		{
			Email = await _store.GetEmailLastLoginAsync() ?? string.Empty;
			BiometricsEnabled = false;
			BiometricsAvailable = false;
		}
		catch (Exception ex)
		{
			CrashLog.Write("LoginViewModel.InitializeAsync", ex);
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

		await NavigateAfterLoginAsync(result.Data!.Usuario.Nivel);
	});

	[RelayCommand]
	private Task BiometricLoginAsync() => RunAsync(async () =>
	{
		// Stub ate F5 reintroduzir biometria nativa.
		ErrorMessage = "Biometria ainda nao habilitada nesta versao.";
		await Task.CompletedTask;
	});

	private async Task NavigateAfterLoginAsync(string _nivel)
	{
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
