using EasyStok.Mobile.ViewModels;

namespace EasyStok.Mobile.Views;

public partial class LojaPickerPage : ContentPage
{
	private readonly LojaPickerViewModel _vm;

	public LojaPickerPage(LojaPickerViewModel vm)
	{
		InitializeComponent();
		BindingContext = _vm = vm;
		LojaPickerRoot.BindingContext = vm;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await _vm.InitializeAsync();
	}
}
