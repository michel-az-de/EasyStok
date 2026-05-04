using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyStok.Mobile.Services;
using EasyStok.Mobile.Storage;
using System.Collections.ObjectModel;

namespace EasyStok.Mobile.ViewModels;

public sealed partial class ProducaoViewModel : BaseViewModel
{
	private readonly IEstoqueService _estoque;
	private readonly EasyStok.Mobile.Storage.ISecureStore _store;

	public ObservableCollection<CachedItemEstoque> Itens { get; } = new();

	[ObservableProperty]
	private bool _semDados;

	[ObservableProperty]
	private string? _statusLine;

	public ProducaoViewModel(IEstoqueService estoque, EasyStok.Mobile.Storage.ISecureStore store)
	{
		_estoque = estoque;
		_store = store;
	}

	public Task InitializeAsync() => RunAsync(async () =>
	{
		var empresaId = await _store.GetEmpresaIdAsync();
		if (empresaId is null)
		{
			ErrorMessage = "Empresa nao definida.";
			return;
		}

		// Cache primeiro (renderiza imediato, mesmo offline).
		await LoadFromCacheAsync(empresaId.Value);

		// Pull em background atualiza com a API.
		var refresh = await _estoque.RefreshAsync(empresaId.Value);
		if (!refresh.Success)
		{
			StatusLine = refresh.Error;
		}
		else
		{
			StatusLine = $"{refresh.Imported} itens atualizados";
		}

		await LoadFromCacheAsync(empresaId.Value);
	});

	[RelayCommand]
	private Task RefreshAsync() => RunAsync(async () =>
	{
		var empresaId = await _store.GetEmpresaIdAsync();
		if (empresaId is null) return;

		var refresh = await _estoque.RefreshAsync(empresaId.Value);
		StatusLine = refresh.Success
			? $"{refresh.Imported} itens atualizados"
			: refresh.Error;
		await LoadFromCacheAsync(empresaId.Value);
	});

	private async Task LoadFromCacheAsync(Guid empresaId)
	{
		var cached = await _estoque.GetCachedAsync(empresaId);
		Itens.Clear();
		foreach (var item in cached)
			Itens.Add(item);
		SemDados = Itens.Count == 0;
	}
}
