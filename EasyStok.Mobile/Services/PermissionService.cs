using EasyStok.Mobile.Models;

namespace EasyStok.Mobile.Services;

/// <summary>
/// Cache em memoria das claims do JWT corrente (nivel + permissoes).
/// E carregado lazy a partir do token armazenado e invalidado a cada
/// login ou refresh. ViewModels e Converters consultam aqui pra
/// decidir visibilidade/habilitacao de UI.
/// </summary>
public sealed class PermissaoService : IPermissaoService
{
	private readonly IAutenticacaoService _auth;
	private NivelAcesso? _nivelCache;
	private HashSet<string>? _permissoesCache;
	private string? _tokenSnapshot;

	public PermissaoService(IAutenticacaoService auth)
	{
		_auth = auth;
	}

	public async Task<NivelAcesso> GetNivelAsync()
	{
		await EnsureLoadedAsync();
		return _nivelCache ?? NivelAcesso.Visualizador;
	}

	public async Task<bool> HasPermissionAsync(string permissao)
	{
		await EnsureLoadedAsync();
		return _permissoesCache?.Contains(permissao) == true;
	}

	public async Task<bool> HasMinNivelAsync(NivelAcesso minimo)
	{
		var nivel = await GetNivelAsync();
		return nivel.TemAcessoMinimo(minimo);
	}

	public void Invalidate()
	{
		_nivelCache = null;
		_permissoesCache = null;
		_tokenSnapshot = null;
	}

	private async Task EnsureLoadedAsync()
	{
		// Invalida cache se o access token mudou (login/refresh).
		var nivelRaw = await _auth.GetNivelFromTokenAsync();
		if (_nivelCache is not null && _tokenSnapshot == nivelRaw)
			return;

		_nivelCache = NivelAcessoExtensions.ParseOrDefault(nivelRaw);
		var perms = await _auth.GetPermissoesFromTokenAsync();
		_permissoesCache = new HashSet<string>(perms, StringComparer.OrdinalIgnoreCase);
		_tokenSnapshot = nivelRaw;
	}
}

public interface IPermissaoService
{
	Task<NivelAcesso> GetNivelAsync();
	Task<bool> HasPermissionAsync(string permissao);
	Task<bool> HasMinNivelAsync(NivelAcesso minimo);
	void Invalidate();
}
