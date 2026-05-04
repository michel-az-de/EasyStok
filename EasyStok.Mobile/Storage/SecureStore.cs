using EasyStok.Mobile.Models;
using System.Text.Json;

namespace EasyStok.Mobile.Storage;

/// <summary>
/// Wrapper sobre <see cref="SecureStorage"/> com chaves tipadas para o estado
/// de autenticacao. Tudo aqui vai pro Keystore do Android (criptografado).
/// </summary>
public sealed class SecureStore : ISecureStore
{
	private const string KeyAccessToken = "easystok.access_token";
	private const string KeyAccessTokenExpiresAt = "easystok.access_token_expires_at";
	private const string KeyRefreshToken = "easystok.refresh_token";
	private const string KeyUsuario = "easystok.usuario_json";
	private const string KeyEmpresaId = "easystok.empresa_id";
	private const string KeyLojaId = "easystok.loja_id";
	private const string KeyEmailLastLogin = "easystok.email_last_login";
	private const string KeyBiometricsEnabled = "easystok.biometrics_enabled";

	public Task<string?> GetAccessTokenAsync() => SecureStorage.Default.GetAsync(KeyAccessToken);
	public Task<string?> GetRefreshTokenAsync() => SecureStorage.Default.GetAsync(KeyRefreshToken);

	public async Task SaveSessionAsync(LoginResponse login)
	{
		var expiresAt = DateTimeOffset.UtcNow.AddSeconds(login.ExpiresIn).ToUnixTimeSeconds();
		await SecureStorage.Default.SetAsync(KeyAccessToken, login.Token);
		await SecureStorage.Default.SetAsync(KeyAccessTokenExpiresAt, expiresAt.ToString());
		await SecureStorage.Default.SetAsync(KeyRefreshToken, login.RefreshToken);
		await SecureStorage.Default.SetAsync(KeyUsuario, JsonSerializer.Serialize(login.Usuario));
		await SecureStorage.Default.SetAsync(KeyEmailLastLogin, login.Usuario.Email);
	}

	public async Task UpdateAccessTokenAsync(string accessToken, string refreshToken, int expiresInSeconds)
	{
		var expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds).ToUnixTimeSeconds();
		await SecureStorage.Default.SetAsync(KeyAccessToken, accessToken);
		await SecureStorage.Default.SetAsync(KeyAccessTokenExpiresAt, expiresAt.ToString());
		await SecureStorage.Default.SetAsync(KeyRefreshToken, refreshToken);
	}

	public async Task<UsuarioInfo?> GetUsuarioAsync()
	{
		var json = await SecureStorage.Default.GetAsync(KeyUsuario);
		return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<UsuarioInfo>(json);
	}

	public async Task<DateTimeOffset?> GetAccessTokenExpiresAtAsync()
	{
		var raw = await SecureStorage.Default.GetAsync(KeyAccessTokenExpiresAt);
		return long.TryParse(raw, out var unix) ? DateTimeOffset.FromUnixTimeSeconds(unix) : null;
	}

	public async Task<bool> IsAccessTokenValidAsync()
	{
		var token = await GetAccessTokenAsync();
		if (string.IsNullOrEmpty(token)) return false;
		var exp = await GetAccessTokenExpiresAtAsync();
		// Consider valid until 60s before actual exp to give the refresh handler room.
		return exp.HasValue && exp.Value > DateTimeOffset.UtcNow.AddSeconds(60);
	}

	public Task<string?> GetEmailLastLoginAsync() => SecureStorage.Default.GetAsync(KeyEmailLastLogin);

	public async Task<Guid?> GetEmpresaIdAsync()
	{
		var raw = await SecureStorage.Default.GetAsync(KeyEmpresaId);
		return Guid.TryParse(raw, out var g) ? g : null;
	}

	public Task SetEmpresaIdAsync(Guid empresaId) =>
		SecureStorage.Default.SetAsync(KeyEmpresaId, empresaId.ToString());

	public async Task<Guid?> GetLojaIdAsync()
	{
		var raw = await SecureStorage.Default.GetAsync(KeyLojaId);
		return Guid.TryParse(raw, out var g) ? g : null;
	}

	public Task SetLojaIdAsync(Guid lojaId) =>
		SecureStorage.Default.SetAsync(KeyLojaId, lojaId.ToString());

	public async Task<bool> GetBiometricsEnabledAsync()
	{
		var raw = await SecureStorage.Default.GetAsync(KeyBiometricsEnabled);
		return raw == "1";
	}

	public Task SetBiometricsEnabledAsync(bool enabled) =>
		SecureStorage.Default.SetAsync(KeyBiometricsEnabled, enabled ? "1" : "0");

	public void ClearSession()
	{
		// SecureStorage.Default.Remove* sao sync; tratamos como void.
		SecureStorage.Default.Remove(KeyAccessToken);
		SecureStorage.Default.Remove(KeyAccessTokenExpiresAt);
		SecureStorage.Default.Remove(KeyRefreshToken);
		SecureStorage.Default.Remove(KeyUsuario);
		SecureStorage.Default.Remove(KeyEmpresaId);
		SecureStorage.Default.Remove(KeyLojaId);
		// KeyEmailLastLogin e KeyBiometricsEnabled preservam-se entre sessoes.
	}
}

public interface ISecureStore
{
	Task<string?> GetAccessTokenAsync();
	Task<string?> GetRefreshTokenAsync();
	Task SaveSessionAsync(LoginResponse login);
	Task UpdateAccessTokenAsync(string accessToken, string refreshToken, int expiresInSeconds);
	Task<UsuarioInfo?> GetUsuarioAsync();
	Task<DateTimeOffset?> GetAccessTokenExpiresAtAsync();
	Task<bool> IsAccessTokenValidAsync();
	Task<string?> GetEmailLastLoginAsync();
	Task<Guid?> GetEmpresaIdAsync();
	Task SetEmpresaIdAsync(Guid empresaId);
	Task<Guid?> GetLojaIdAsync();
	Task SetLojaIdAsync(Guid lojaId);
	Task<bool> GetBiometricsEnabledAsync();
	Task SetBiometricsEnabledAsync(bool enabled);
	void ClearSession();
}
