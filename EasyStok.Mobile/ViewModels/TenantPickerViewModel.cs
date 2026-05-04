using CommunityToolkit.Mvvm.Input;
using EasyStok.Mobile.Services;

namespace EasyStok.Mobile.ViewModels;

/// <summary>
/// MVP: o backend atual nao expoe endpoint para listar empresas do usuario
/// (so existe /api/auth/me/export que e LGPD). Quando o user pertence a
/// MAIS DE UMA empresa, o login retorna JWT sem claim empresaId — caso
/// raro hoje. Esta tela orienta o user a contatar o suporte para reduzir
/// o vinculo a uma empresa unica, ate o backend ganhar /api/empresas/minhas.
/// Quando o endpoint chegar, este VM lista as empresas e re-loga via
/// AuthService.LoginAsync com EmpresaId selecionada.
/// </summary>
public sealed partial class TenantPickerViewModel : BaseViewModel
{
	private readonly IAuthService _auth;

	public TenantPickerViewModel(IAuthService auth)
	{
		_auth = auth;
	}

	[RelayCommand]
	private Task LogoutAsync() => RunAsync(async () =>
	{
		await _auth.LogoutAsync();
		await Shell.Current.GoToAsync("//login");
	});
}
