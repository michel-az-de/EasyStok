using EasyStok.Mobile.Storage;
using Microsoft.Extensions.Logging;

namespace EasyStok.Mobile.Services;

/// <summary>
/// Loop periodico que mantem o app sincronizado com o backend:
///   - Cada 30s, drena o outbox (mutations pendentes) e da pull no
///     /api/estoque para a empresa ativa.
///   - Reage a <see cref="Connectivity.ConnectivityChanged"/> — quando
///     a rede volta, dispara flush+pull imediato sem esperar o tick.
///   - Invalida o PermissionService apos cada refresh para que claims
///     atualizadas (apos refresh do JWT) sejam relidas.
///
/// Espelha o ciclo do sync.js do PWA (flush + pull a cada 30s + flush
/// imediato no evento online).
/// </summary>
public sealed class SyncEngine : IDisposable
{
	private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

	private readonly IOutboxFlushService _flush;
	private readonly IEstoqueService _estoque;
	private readonly ISecureStore _store;
	private readonly IPermissionService _permissions;
	private readonly ILogger<SyncEngine> _logger;

	private CancellationTokenSource? _cts;
	private Task? _loopTask;

	public SyncEngine(
		IOutboxFlushService flush,
		IEstoqueService estoque,
		ISecureStore store,
		IPermissionService permissions,
		ILogger<SyncEngine> logger)
	{
		_flush = flush;
		_estoque = estoque;
		_store = store;
		_permissions = permissions;
		_logger = logger;
	}

	public void Start()
	{
		if (_loopTask is not null) return;
		_cts = new CancellationTokenSource();
		_loopTask = Task.Run(() => RunLoopAsync(_cts.Token));
		Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
		_logger.LogInformation("SyncEngine iniciado");
	}

	public void Stop()
	{
		Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
		_cts?.Cancel();
		_cts = null;
		_loopTask = null;
		_logger.LogInformation("SyncEngine parado");
	}

	public void Dispose() => Stop();

	private async Task RunLoopAsync(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			try { await TickAsync(ct); }
			catch (OperationCanceledException) { break; }
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Tick do SyncEngine falhou");
			}

			try { await Task.Delay(TickInterval, ct); }
			catch (OperationCanceledException) { break; }
		}
	}

	private async Task TickAsync(CancellationToken ct)
	{
		if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet) return;
		if (!await _store.IsAccessTokenValidAsync())
		{
			// Sem sessao valida: AuthHandler tentaria refresh em request real,
			// mas nao queremos disparar flush/pull as cegas.
			return;
		}

		await _flush.FlushAsync(ct);

		var empresaId = await _store.GetEmpresaIdAsync();
		if (empresaId is not null)
			await _estoque.RefreshAsync(empresaId.Value, ct);

		_permissions.Invalidate();
	}

	private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
	{
		if (e.NetworkAccess != NetworkAccess.Internet) return;
		_logger.LogDebug("Rede voltou — flush imediato");
		_ = Task.Run(async () =>
		{
			try { await TickAsync(CancellationToken.None); }
			catch (Exception ex) { _logger.LogWarning(ex, "Flush por reconexao falhou"); }
		});
	}
}
