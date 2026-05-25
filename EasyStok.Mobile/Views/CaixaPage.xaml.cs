using EasyStok.Mobile.Services;
using EasyStok.Mobile.ViewModels;

namespace EasyStok.Mobile.Views;

public partial class CaixaPage : ContentPage
{
    private readonly CaixaViewModel _vm;

    public CaixaPage(CaixaViewModel vm)
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
