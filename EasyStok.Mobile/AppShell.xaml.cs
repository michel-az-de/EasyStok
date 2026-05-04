using EasyStok.Mobile.Services;
using EasyStok.Mobile.Views;

namespace EasyStok.Mobile;

public partial class AppShell : Shell
{
	private readonly IAuthService _auth;
	private bool _initialized;

	public AppShell(IAuthService auth)
	{
		InitializeComponent();
		_auth = auth;
		RegisterRoutes();
	}

	protected override void OnNavigated(ShellNavigatedEventArgs args)
	{
		base.OnNavigated(args);
		if (_initialized) return;
		_initialized = true;

		// Roteia para login se nao houver sessao valida.
		Dispatcher.Dispatch(async () =>
		{
			if (!await _auth.IsAuthenticatedAsync())
				await GoToAsync("//login");
		});
	}

	private static void RegisterRoutes()
	{
		// Auth flow.
		Routing.RegisterRoute("login", typeof(LoginPage));
		Routing.RegisterRoute("tenant-picker", typeof(TenantPickerPage));
		Routing.RegisterRoute("loja-picker", typeof(LojaPickerPage));

		// 11 telas espelhando o PWA.
		Routing.RegisterRoute("producao", typeof(ProducaoPage));
		Routing.RegisterRoute("pedidos", typeof(PedidosPage));
		Routing.RegisterRoute("caixa", typeof(CaixaPage));
		Routing.RegisterRoute("finalizados", typeof(FinalizadosPage));
		Routing.RegisterRoute("clientes", typeof(ClientesPage));
		Routing.RegisterRoute("suporte", typeof(SuportePage));
		Routing.RegisterRoute("conferencia", typeof(ConferenciaPage));
		Routing.RegisterRoute("compras", typeof(ComprasPage));
		Routing.RegisterRoute("historico", typeof(HistoricoPage));
		Routing.RegisterRoute("estoque", typeof(EstoquePage));
	}
}
