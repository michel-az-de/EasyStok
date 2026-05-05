using EasyStok.Mobile.Services;
using EasyStok.Mobile.ViewModels;

namespace EasyStok.Mobile.Views;

public partial class EstoquePage : ContentPage
{
	private readonly EstoqueViewModel _vm;

	public EstoquePage(EstoqueViewModel vm, AppIdentity identity)
	{
		InitializeComponent();
		BindingContext = _vm = vm;
		Header.Bind(identity);
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		UiSafe.Fire(() => _vm.InitializeAsync());
	}
}
