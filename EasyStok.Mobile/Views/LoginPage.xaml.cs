using EasyStok.Mobile.Services;
using EasyStok.Mobile.ViewModels;

namespace EasyStok.Mobile.Views;

public partial class LoginPage : ContentPage
{
	private readonly LoginViewModel _vm;

	public LoginPage(LoginViewModel vm)
	{
		InitializeComponent();
		BindingContext = _vm = vm;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		UiSafe.Fire(() => _vm.InitializeAsync());
	}
}
