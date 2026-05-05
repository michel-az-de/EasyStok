using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyStok.Mobile.Storage;
using System.Collections.ObjectModel;
using System.Text;

namespace EasyStok.Mobile.ViewModels;

public sealed partial class ComprasViewModel : BaseViewModel
{
	private readonly AppDatabase _db;
	private readonly ISecureStore _store;

	public ObservableCollection<CachedItemEstoque> Itens { get; } = new();

	[ObservableProperty]
	private bool _semDados;

	public ComprasViewModel(AppDatabase db, ISecureStore store)
	{
		_db = db;
		_store = store;
	}

	public Task InitializeAsync() => RunAsync(async () =>
	{
		var empresaId = await _store.GetEmpresaIdAsync();
		if (empresaId is null) { SemDados = true; return; }

		var conn = await _db.GetConnectionAsync();
		var todos = await conn.Table<CachedItemEstoque>()
			.Where(x => x.EmpresaId == empresaId.Value)
			.ToListAsync();

		var faltantes = todos
			.Where(x => x.Qty <= 0 || x.Status == "critico" || x.Status == "vencido")
			.OrderBy(x => x.ProdutoNome)
			.ToList();

		Itens.Clear();
		foreach (var item in faltantes) Itens.Add(item);
		SemDados = Itens.Count == 0;
	});

	[RelayCommand]
	private Task CompartilharAsync() => RunAsync(async () =>
	{
		if (Itens.Count == 0) return;

		var sb = new StringBuilder();
		sb.AppendLine("Lista de compras — EasyStok");
		sb.AppendLine();
		foreach (var item in Itens)
			sb.AppendLine($"• {item.ProdutoNome} ({item.Sku}) — qty: {item.Qty} [{item.Status}]");

		await Share.Default.RequestAsync(new ShareTextRequest
		{
			Title = "Lista de compras",
			Text = sb.ToString(),
		});
	});
}
