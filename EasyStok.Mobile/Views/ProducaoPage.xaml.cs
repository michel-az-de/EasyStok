using EasyStok.Mobile.ViewModels;

namespace EasyStok.Mobile.Views;

public partial class ProducaoPage : ContentPage
{
	private readonly ProducaoViewModel _vm;

	public ProducaoPage(ProducaoViewModel vm)
	{
		InitializeComponent();
		BindingContext = _vm = vm;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await _vm.InitializeAsync();
	}
}
