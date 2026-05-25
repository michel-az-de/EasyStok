using EasyStok.Mobile.Services;
using EasyStok.Mobile.ViewModels;

namespace EasyStok.Mobile.Views;

public partial class PedidosPage : ContentPage
{
    private readonly PedidosViewModel _vm;

    public PedidosPage(PedidosViewModel vm)
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
