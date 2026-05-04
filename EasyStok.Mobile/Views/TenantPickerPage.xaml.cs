using EasyStok.Mobile.ViewModels;

namespace EasyStok.Mobile.Views;

public partial class TenantPickerPage : ContentPage
{
	public TenantPickerPage(TenantPickerViewModel vm)
	{
		InitializeComponent();
		BindingContext = vm;
	}
}
