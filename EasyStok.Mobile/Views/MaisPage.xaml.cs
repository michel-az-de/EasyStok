using EasyStok.Mobile.Services;

namespace EasyStok.Mobile.Views;

public partial class MaisPage : ContentPage
{
	public MaisPage()
	{
		InitializeComponent();
	}

	// Rotas registradas em AppShell como rotas globais; navegamos via push
	// relativo ("rota") em vez de absoluto ("//rota") pra que o usuario
	// possa voltar pra Mais usando o botao back.
	private void OnTapCaixa(object? sender, TappedEventArgs e) =>
		UiSafe.Fire(() => Shell.Current.GoToAsync("caixa"));

	private void OnTapConferencia(object? sender, TappedEventArgs e) =>
		UiSafe.Fire(() => Shell.Current.GoToAsync("conferencia"));

	private void OnTapCompras(object? sender, TappedEventArgs e) =>
		UiSafe.Fire(() => Shell.Current.GoToAsync("compras"));

	private void OnTapFinalizados(object? sender, TappedEventArgs e) =>
		UiSafe.Fire(() => Shell.Current.GoToAsync("finalizados"));

	private void OnTapClientes(object? sender, TappedEventArgs e) =>
		UiSafe.Fire(() => Shell.Current.GoToAsync("clientes"));

	private void OnTapHistorico(object? sender, TappedEventArgs e) =>
		UiSafe.Fire(() => Shell.Current.GoToAsync("historico"));

	private void OnTapSuporte(object? sender, TappedEventArgs e) =>
		UiSafe.Fire(() => Shell.Current.GoToAsync("suporte"));
}
