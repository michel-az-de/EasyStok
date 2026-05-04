using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyStok.Mobile.Models;
using EasyStok.Mobile.Services;
using EasyStok.Mobile.Storage;
using System.Collections.ObjectModel;

namespace EasyStok.Mobile.ViewModels;

public sealed partial class ProducaoViewModel : BaseViewModel
{
	private readonly IEstoqueService _estoque;
	private readonly IEstoqueMutationService _mutations;
	private readonly IOutboxRepository _outbox;
	private readonly ISecureStore _store;

	public ObservableCollection<CachedItemEstoque> Itens { get; } = new();

	[ObservableProperty]
	private bool _semDados;

	[ObservableProperty]
	private string? _statusLine;

	[ObservableProperty]
	private int _pendingMutations;

	public ProducaoViewModel(
		IEstoqueService estoque,
		IEstoqueMutationService mutations,
		IOutboxRepository outbox,
		ISecureStore store)
	{
		_estoque = estoque;
		_mutations = mutations;
		_outbox = outbox;
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

		await LoadFromCacheAsync(empresaId.Value);
		await UpdatePendingAsync();

		var refresh = await _estoque.RefreshAsync(empresaId.Value);
		StatusLine = refresh.Success
			? $"{refresh.Imported} itens atualizados"
			: refresh.Error;

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
		await UpdatePendingAsync();
	});

	// Chamados pelo code-behind apos confirmar o popup de captura.
	public async Task HandleIncrementAsync(CachedItemEstoque item, CapturaProducaoResult capture)
	{
		if (item is null) return;
		try
		{
			await _mutations.IncrementAsync(item, capture);
			ReplaceInCollection(item.Id);
			await UpdatePendingAsync();
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	public async Task HandleDecrementAsync(CachedItemEstoque item)
	{
		if (item is null || item.Qty <= 0) return;
		try
		{
			await _mutations.DecrementAsync(item);
			ReplaceInCollection(item.Id);
			await UpdatePendingAsync();
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task LoadFromCacheAsync(Guid empresaId)
	{
		var cached = await _estoque.GetCachedAsync(empresaId);
		Itens.Clear();
		foreach (var item in cached)
			Itens.Add(item);
		SemDados = Itens.Count == 0;
	}

	private async Task UpdatePendingAsync()
	{
		PendingMutations = await _outbox.CountAsync();
	}

	private async void ReplaceInCollection(string itemId)
	{
		// Pos optimistic update no SQLite, recarrega so o item afetado.
		var empresaId = await _store.GetEmpresaIdAsync();
		if (empresaId is null) return;
		var cached = await _estoque.GetCachedAsync(empresaId.Value);
		var fresh = cached.FirstOrDefault(x => x.Id == itemId);
		if (fresh is null) return;
		var idx = Itens.ToList().FindIndex(x => x.Id == itemId);
		if (idx >= 0) Itens[idx] = fresh;
	}
}
