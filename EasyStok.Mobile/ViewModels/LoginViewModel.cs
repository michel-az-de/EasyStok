using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyStok.Mobile.Services;
using EasyStok.Mobile.Storage;

namespace EasyStok.Mobile.ViewModels;

public sealed partial class LoginViewModel : BaseViewModel
{
	private readonly IAuthService _auth;
	private readonly ISecureStore _store;
	private readonly IDemoSeedService _demoSeed;

	[ObservableProperty]
	private string _email = string.Empty;

	[ObservableProperty]
	private string _senha = string.Empty;

	public LoginViewModel(IAuthService auth, ISecureStore store, IDemoSeedService demoSeed)
	{
		_auth = auth;
		_store = store;
		_demoSeed = demoSeed;
	}

	public async Task InitializeAsync()
	{
		try
		{
			Email = await _store.GetEmailLastLoginAsync() ?? string.Empty;
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

		var empresaId = await _auth.GetEmpresaIdFromTokenAsync();
		if (empresaId is null)
		{
			await Shell.Current.GoToAsync("//tenant-picker");
			return;
		}
		await _store.SetEmpresaIdAsync(empresaId.Value);
		await Shell.Current.GoToAsync("//loja-picker");
	});

	/// <summary>
	/// Bypass do backend: cria sessao demo local + popula cache com produtos
	/// seed. Permite usar o app 100% offline pra teste/demo de UI.
	/// </summary>
	[RelayCommand]
	private Task ContinuarOfflineAsync() => RunAsync(async () =>
	{
		await _auth.LoginOfflineDemoAsync();
		await _demoSeed.SeedIfEmptyAsync();
		await Shell.Current.GoToAsync("//home");
	});
}
