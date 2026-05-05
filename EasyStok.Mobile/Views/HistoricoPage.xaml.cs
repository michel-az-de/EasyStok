using EasyStok.Mobile.Services;
using EasyStok.Mobile.ViewModels;

namespace EasyStok.Mobile.Views;

public partial class HistoricoPage : ContentPage
{
	private readonly HistoricoViewModel _vm;

	public HistoricoPage(HistoricoViewModel vm)
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
