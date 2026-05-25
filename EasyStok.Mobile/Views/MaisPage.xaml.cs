using EasyStok.Mobile.Services;

namespace EasyStok.Mobile.Views;

public partial class MaisPage : ContentPage
{
    private readonly AppIdentity _identity;

    public MaisPage(AppIdentity identity)
    {
        InitializeComponent();
        _identity = identity;
        Header.Bind(identity);
    }

    private void OnTapCaixa(object? sender, TappedEventArgs e) => UiSafe.Fire(() => Shell.Current.GoToAsync("caixa"));
    private void OnTapConferencia(object? sender, TappedEventArgs e) => UiSafe.Fire(() => Shell.Current.GoToAsync("conferencia"));
    private void OnTapCompras(object? sender, TappedEventArgs e) => UiSafe.Fire(() => Shell.Current.GoToAsync("compras"));
    private void OnTapFinalizados(object? sender, TappedEventArgs e) => UiSafe.Fire(() => Shell.Current.GoToAsync("finalizados"));
    private void OnTapClientes(object? sender, TappedEventArgs e) => UiSafe.Fire(() => Shell.Current.GoToAsync("clientes"));
    private void OnTapHistorico(object? sender, TappedEventArgs e) => UiSafe.Fire(() => Shell.Current.GoToAsync("historico"));
    private void OnTapSuporte(object? sender, TappedEventArgs e) => UiSafe.Fire(() => Shell.Current.GoToAsync("suporte"));
    private void OnTapEstoque(object? sender, TappedEventArgs e) => UiSafe.Fire(() => Shell.Current.GoToAsync("//estoque"));
}
