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

	private void OnIrParaProducao(object? sender, TappedEventArgs e) =>
		UiSafe.Fire(() => Shell.Current.GoToAsync("//producao"));

	private void OnSair(object? sender, TappedEventArgs e) =>
		UiSafe.Fire(async () =>
		{
			await _auth.LogoutAsync();
			await Shell.Current.GoToAsync("//login");
		});
}
