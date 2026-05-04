using EasyStok.Mobile.Services;
using EasyStok.Mobile.Storage;

namespace EasyStok.Mobile.Views;

public partial class HomePage : ContentPage
{
	private readonly IAuthService _auth;
	private readonly ISecureStore _store;

	public HomePage(IAuthService auth, ISecureStore store)
	{
		InitializeComponent();
		_auth = auth;
		_store = store;
	}

	private async void OnIrParaProducao(object? sender, TappedEventArgs e)
	{
		await Shell.Current.GoToAsync("//producao");
	}

	private async void OnSair(object? sender, TappedEventArgs e)
	{
		await _auth.LogoutAsync();
		await Shell.Current.GoToAsync("//login");
	}
}
