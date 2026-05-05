using EasyStok.Mobile.Services;
using EasyStok.Mobile.Storage;
using EasyStok.Mobile.Views;

namespace EasyStok.Mobile;

public partial class AppShell : Shell
{
	private readonly IAuthService _auth;
	private readonly ISecureStore _store;
	private readonly SyncEngine _sync;
	private readonly IDemoSeedService _demoSeed;
	private bool _initialized;

	public AppShell(IAuthService auth, ISecureStore store, SyncEngine sync, IDemoSeedService demoSeed)
	{
		InitializeComponent();
		_auth = auth;
		_store = store;
		_sync = sync;
		_demoSeed = demoSeed;
		RegisterRoutes();
	}

	private static void RegisterRoutes()
	{
		// Rotas das telas acessadas via "Mais" — push relativo (sem //) sobre
		// a Tab corrente. Voltar com back retorna pra Tab Mais.
		Routing.RegisterRoute("caixa", typeof(CaixaPage));
		Routing.RegisterRoute("finalizados", typeof(FinalizadosPage));
		Routing.RegisterRoute("clientes", typeof(ClientesPage));
		Routing.RegisterRoute("suporte", typeof(SuportePage));
		Routing.RegisterRoute("conferencia", typeof(ConferenciaPage));
		Routing.RegisterRoute("compras", typeof(ComprasPage));
		Routing.RegisterRoute("historico", typeof(HistoricoPage));
	}

	protected override void OnNavigated(ShellNavigatedEventArgs args)
	{
		base.OnNavigated(args);
		if (_initialized) return;
		_initialized = true;

		UiSafe.Fire(InitializeSessionAsync);
	}

	private async Task InitializeSessionAsync()
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

		// Sessao demo idempotente: garante que o seed esta populado
		// (caso o app tenha sido reaberto e Preferences/SQLite estejam vazios).
		if (await _auth.IsDemoAsync())
			await _demoSeed.SeedIfEmptyAsync();

		await GoToAsync("//home");
		_sync.Start();
	}
}
