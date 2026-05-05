using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyStok.Mobile.Storage;
using System.Collections.ObjectModel;

namespace EasyStok.Mobile.ViewModels;

public sealed partial class EstoqueViewModel : BaseViewModel
{
	private readonly AppDatabase _db;
	private readonly ISecureStore _store;
	private List<CachedItemEstoque> _all = new();

	public ObservableCollection<CachedItemEstoque> Itens { get; } = new();

	[ObservableProperty]
	private string _filtro = "todos"; // todos | ok | atencao | critico | vencido

	[ObservableProperty]
	private bool _semDados;

	public EstoqueViewModel(AppDatabase db, ISecureStore store)
	{
		_db = db;
		_store = store;
	}

	public Task InitializeAsync() => RunAsync(async () =>
	{
		var empresaId = await _store.GetEmpresaIdAsync();
		if (empresaId is null) { SemDados = true; return; }

		var conn = await _db.GetConnectionAsync();
		_all = await conn.Table<CachedItemEstoque>()
			.Where(x => x.EmpresaId == empresaId.Value)
			.OrderBy(x => x.ProdutoNome)
			.ToListAsync();
		ApplyFiltro();
	});

	[RelayCommand]
	private void SetFiltro(string novo)
	{
		Filtro = novo;
		ApplyFiltro();
	}

	private void ApplyFiltro()
	{
		Itens.Clear();
		var filtrados = Filtro == "todos"
			? _all
			: _all.Where(x => x.Status == Filtro).ToList();
		foreach (var item in filtrados) Itens.Add(item);
		SemDados = Itens.Count == 0;
	}
}
