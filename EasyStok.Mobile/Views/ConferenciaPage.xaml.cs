using EasyStok.Mobile.ViewModels;

namespace EasyStok.Mobile.Views;

public partial class ConferenciaPage : ContentPage
{
    public ConferenciaPage(ConferenciaViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
