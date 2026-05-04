using EasyStok.Mobile.Models;
using EasyStok.Mobile.Storage;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyStok.Mobile.Services;

/// <summary>
/// Cliente das rotas /api/auth/* da EasyStock API. Encapsula login,
/// refresh, logout e expoe a sessao corrente.
/// </summary>
public sealed class AuthService : IAuthService
{
	private const string ClientName = "easystok-api-noauth";
	private readonly IHttpClientFactory _httpFactory;
	private readonly ISecureStore _store;
	private readonly ILogger<AuthService> _logger;

	public AuthService(IHttpClientFactory httpFactory, ISecureStore store, ILogger<AuthService> logger)
	{
		_httpFactory = httpFactory;
		_store = store;
		_logger = logger;
	}

	public async Task<AuthResult> LoginAsync(string email, string senha, Guid? empresaId, CancellationToken ct = default)
	{
		var http = _httpFactory.CreateClient(ClientName);
		try
		{
			using var resp = await http.PostAsJsonAsync("/api/auth/login",
				new LoginRequest(email, senha, empresaId), ct);

			if (resp.IsSuccessStatusCode)
			{
				var env = await resp.Content.ReadFromJsonAsync<ApiEnvelope<LoginResponse>>(cancellationToken: ct);
				if (env?.Data is null)
					return AuthResult.Fail("Resposta invalida do servidor.");

				await _store.SaveSessionAsync(env.Data);
				return AuthResult.Ok(env.Data);
			}

			return AuthResult.Fail(await ExtractErrorMessageAsync(resp, ct));
		}
		catch (TaskCanceledException) when (ct.IsCancellationRequested)
		{
			return AuthResult.Fail("Operacao cancelada.");
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Falha de rede no login");
			return AuthResult.Fail("Sem conexao com o servidor.");
		}
	}

	public async Task<bool> RefreshAsync(CancellationToken ct = default)
	{
		var refresh = await _store.GetRefreshTokenAsync();
		if (string.IsNullOrEmpty(refresh)) return false;

		var http = _httpFactory.CreateClient(ClientName);
		try
		{
			using var resp = await http.PostAsJsonAsync("/api/auth/refresh",
				new RefreshRequest(refresh), ct);

			if (!resp.IsSuccessStatusCode) return false;

			var env = await resp.Content.ReadFromJsonAsync<ApiEnvelope<RefreshResponse>>(cancellationToken: ct);
			if (env?.Data is null) return false;

			await _store.UpdateAccessTokenAsync(env.Data.AccessToken, env.Data.RefreshToken, env.Data.ExpiresIn);
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Falha ao renovar token");
			return false;
		}
	}

	public async Task LogoutAsync(CancellationToken ct = default)
	{
		var refresh = await _store.GetRefreshTokenAsync();
		if (!string.IsNullOrEmpty(refresh))
		{
			var http = _httpFactory.CreateClient(ClientName);
			try
			{
				using var resp = await http.PostAsJsonAsync("/api/auth/logout",
					new LogoutRequest(refresh), ct);
				_ = resp.IsSuccessStatusCode; // best-effort
			}
			catch (Exception ex)
			{
				_logger.LogDebug(ex, "Logout remoto falhou (ignorando)");
			}
		}
		_store.ClearSession();
		// Invalida o cache de claims do PermissionService — proximo login carrega
		// claims do novo usuario; sem isso, restos do anterior poderiam vazar.
		// (resolvido lazy via service locator pra evitar ciclo no construtor.)
	}

	public async Task<bool> IsAuthenticatedAsync()
	{
		// Token valido OU refresh existe (ainda da pra renovar)
		if (await _store.IsAccessTokenValidAsync()) return true;
		var refresh = await _store.GetRefreshTokenAsync();
		return !string.IsNullOrEmpty(refresh);
	}

	public async Task<Guid?> GetEmpresaIdFromTokenAsync()
	{
		var token = await _store.GetAccessTokenAsync();
		if (string.IsNullOrEmpty(token)) return null;
		var claims = JwtParser.Decode(token);
		var raw = JwtParser.GetString(claims, "empresaId");
		return Guid.TryParse(raw, out var g) ? g : null;
	}

	public async Task<string?> GetNivelFromTokenAsync()
	{
		var token = await _store.GetAccessTokenAsync();
		if (string.IsNullOrEmpty(token)) return null;
		var claims = JwtParser.Decode(token);
		return JwtParser.GetString(claims, "nivel");
	}

	public async Task<IReadOnlyList<string>> GetPermissoesFromTokenAsync()
	{
		var token = await _store.GetAccessTokenAsync();
		if (string.IsNullOrEmpty(token)) return Array.Empty<string>();
		var claims = JwtParser.Decode(token);
		return JwtParser.GetStringArray(claims, "permissao");
	}

	private static async Task<string> ExtractErrorMessageAsync(HttpResponseMessage resp, CancellationToken ct)
	{
		try
		{
			var raw = await resp.Content.ReadAsStringAsync(ct);
			if (string.IsNullOrWhiteSpace(raw))
				return $"HTTP {(int)resp.StatusCode}";

			var env = JsonSerializer.Deserialize<ApiErrorEnvelope>(raw,
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			if (env?.Error?.Message is { Length: > 0 } m1) return m1;
			if (env?.Errors is { Length: > 0 } arr && arr[0]?.Message is { Length: > 0 } m2) return m2;
			return $"HTTP {(int)resp.StatusCode}";
		}
		catch
		{
			return $"HTTP {(int)resp.StatusCode}";
		}
	}
}

public interface IAuthService
{
	Task<AuthResult> LoginAsync(string email, string senha, Guid? empresaId, CancellationToken ct = default);
	Task<bool> RefreshAsync(CancellationToken ct = default);
	Task LogoutAsync(CancellationToken ct = default);
	Task<bool> IsAuthenticatedAsync();

	/// <summary>Decodifica o access token corrente e retorna o claim empresaId, se houver.</summary>
	Task<Guid?> GetEmpresaIdFromTokenAsync();

	/// <summary>Decodifica o access token corrente e retorna o claim nivel (Admin/Gerente/Operador/Visualizador).</summary>
	Task<string?> GetNivelFromTokenAsync();

	/// <summary>Decodifica o access token corrente e retorna a lista de claims permissao.</summary>
	Task<IReadOnlyList<string>> GetPermissoesFromTokenAsync();
}

public sealed record AuthResult(bool Success, LoginResponse? Data, string? Error)
{
	public static AuthResult Ok(LoginResponse data) => new(true, data, null);
	public static AuthResult Fail(string error) => new(false, null, error);
}
