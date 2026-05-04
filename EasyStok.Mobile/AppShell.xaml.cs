using EasyStok.Mobile.Services;
using EasyStok.Mobile.Storage;

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
	}

	protected override void OnNavigated(ShellNavigatedEventArgs args)
	{
		base.OnNavigated(args);
		if (_initialized) return;
		_initialized = true;

		// Decisao da rota inicial baseada no estado da sessao. try/catch
		// porque async void em Dispatcher.Dispatch crasha o app se uma
		// exception escapar (rota inexistente, falha em SecureStorage, etc).
		Dispatcher.Dispatch(async () =>
		{
			try
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

				await GoToAsync("//home");
				_sync.Start();
			}
			catch (Exception ex)
			{
				CrashLog.Write("AppShell.OnNavigated", ex);
			}
		});
	}
}
