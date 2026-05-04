using EasyStok.Mobile.Services;
using EasyStok.Mobile.Storage;
using EasyStok.Mobile.Views;

namespace EasyStok.Mobile;

public partial class AppShell : Shell
{
	private readonly IAuthService _auth;
	private readonly ISecureStore _store;
	private readonly SyncEngine _sync;
	private bool _initialized;

	public AppShell(IAuthService auth, ISecureStore store, SyncEngine sync)
	{
		InitializeComponent();
		_auth = auth;
		_store = store;
		_sync = sync;
		RegisterRoutes();
	}

	protected override void OnNavigated(ShellNavigatedEventArgs args)
	{
		base.OnNavigated(args);
		if (_initialized) return;
		_initialized = true;

		// Decide a rota inicial baseado no estado da sessao:
		//   - sem token e sem refresh    -> //login
		//   - autenticado mas sem empresa -> //tenant-picker
		//   - empresa OK mas sem loja     -> //loja-picker
		//   - tudo OK                    -> //home (default ja esta na rota inicial)
		Dispatcher.Dispatch(async () =>
		{
			if (!await _auth.IsAuthenticatedAsync())
			{
				await GoToAsync("//login");
				return;
			}

			var empresaId = await _store.GetEmpresaIdAsync()
				?? await _auth.GetEmpresaIdFromTokenAsync();
			if (empresaId is null)
			{
				await GoToAsync("//tenant-picker");
				return;
			}

			if (await _store.GetLojaIdAsync() is null)
			{
				await GoToAsync("//loja-picker");
				return;
			}
			// Tudo certo — segue na home (rota default do ShellContent inicial).
			// Liga o SyncEngine pra manter outbox + cache em dia.
			_sync.Start();
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
