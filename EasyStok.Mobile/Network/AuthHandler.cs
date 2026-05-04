using EasyStok.Mobile.Services;
using EasyStok.Mobile.Storage;
using System.Net;
using System.Net.Http.Headers;

namespace EasyStok.Mobile.Network;

/// <summary>
/// DelegatingHandler que injeta o JWT corrente como Authorization Bearer
/// e tenta refresh transparente uma vez em caso de 401. Se o refresh
/// falhar, limpa a sessao e propaga 401 — quem chamou decide redirecionar
/// pra LoginPage.
/// </summary>
public sealed class AuthHandler : DelegatingHandler
{
	private readonly ISecureStore _store;
	private readonly IServiceProvider _services;
	private static readonly SemaphoreSlim _refreshLock = new(1, 1);

	public AuthHandler(ISecureStore store, IServiceProvider services)
	{
		_store = store;
		_services = services;
	}

	protected override async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request, CancellationToken cancellationToken)
	{
		await AttachTokenAsync(request);
		var resp = await base.SendAsync(request, cancellationToken);

		if (resp.StatusCode != HttpStatusCode.Unauthorized) return resp;

		// 401 — tenta refresh exatamente uma vez.
		resp.Dispose();
		var refreshed = await TryRefreshAsync(cancellationToken);
		if (!refreshed)
		{
			_store.ClearSession();
			// 401 propaga; UI ouve via interceptor / shell e roteia pro login.
			return new HttpResponseMessage(HttpStatusCode.Unauthorized);
		}

		// Reenvia com novo token
		var retry = await CloneAsync(request);
		await AttachTokenAsync(retry);
		return await base.SendAsync(retry, cancellationToken);
	}

	private async Task AttachTokenAsync(HttpRequestMessage request)
	{
		var token = await _store.GetAccessTokenAsync();
		if (!string.IsNullOrEmpty(token))
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
	}

	private async Task<bool> TryRefreshAsync(CancellationToken ct)
	{
		await _refreshLock.WaitAsync(ct);
		try
		{
			// Outra thread pode ter renovado enquanto esperavamos o lock.
			if (await _store.IsAccessTokenValidAsync()) return true;

			// Resolve AuthService sob demanda para evitar ciclo de DI
			// (AuthService depende de IHttpClientFactory que depende deste handler).
			var auth = (IAuthService)_services.GetService(typeof(IAuthService))!;
			return await auth.RefreshAsync(ct);
		}
		finally
		{
			_refreshLock.Release();
		}
	}

	private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage source)
	{
		var clone = new HttpRequestMessage(source.Method, source.RequestUri) { Version = source.Version };
		if (source.Content is not null)
		{
			var ms = new MemoryStream();
			await source.Content.CopyToAsync(ms);
			ms.Position = 0;
			clone.Content = new StreamContent(ms);
			foreach (var h in source.Content.Headers)
				clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
		}
		foreach (var h in source.Headers)
			clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
		return clone;
	}
}
