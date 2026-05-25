using EasyStok.Mobile.Services;
using EasyStok.Mobile.ViewModels;

namespace EasyStok.Mobile.Views;

public partial class LojaPickerPage : ContentPage
{
    private readonly LojaPickerViewModel _vm;

    public LojaPickerPage(LojaPickerViewModel vm)
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
