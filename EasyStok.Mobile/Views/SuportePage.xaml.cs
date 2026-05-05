using EasyStok.Mobile.Services;
using EasyStok.Mobile.ViewModels;

namespace EasyStok.Mobile.Views;

public partial class SuportePage : ContentPage
{
	private readonly SuporteViewModel _vm;

	public SuportePage(SuporteViewModel vm)
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
