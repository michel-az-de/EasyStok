using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyStok.Mobile.Services;
using EasyStok.Mobile.Storage;
using System.Collections.ObjectModel;

namespace EasyStok.Mobile.ViewModels;

public sealed partial class CaixaViewModel : BaseViewModel
{
	private readonly AppDatabase _db;
	private readonly ISecureStore _store;

	public ObservableCollection<CachedCaixaEntry> Entries { get; } = new();

	[ObservableProperty]
	private decimal _saldoDoDia;

	[ObservableProperty]
	private decimal _entradasDoDia;

	[ObservableProperty]
	private decimal _saidasDoDia;

	[ObservableProperty]
	private bool _semDados;

	public CaixaViewModel(AppDatabase db, ISecureStore store) { _db = db; _store = store; }

	public Task InitializeAsync() => RunAsync(async () =>
	{
		var empresaId = await _store.GetEmpresaIdAsync();
		if (empresaId is null) return;

		var hoje = DateTime.UtcNow.Date;
		var amanha = hoje.AddDays(1);

		var conn = await _db.GetConnectionAsync();
		var todos = await conn.Table<CachedCaixaEntry>()
			.Where(x => x.EmpresaId == empresaId.Value)
			.OrderByDescending(x => x.AtUtc)
			.ToListAsync();

		var doDia = todos.Where(x => x.AtUtc >= hoje && x.AtUtc < amanha).ToList();
		EntradasDoDia = doDia.Where(x => x.Tipo == "entrada").Sum(x => x.Valor);
		SaidasDoDia = doDia.Where(x => x.Tipo == "saida").Sum(x => x.Valor);
		SaldoDoDia = EntradasDoDia - SaidasDoDia;

		Entries.Clear();
		foreach (var e in todos) Entries.Add(e);
		SemDados = Entries.Count == 0;
	});

	[RelayCommand]
	private Task NovaEntradaAsync() => RunAsync(async () =>
	{
		await UiSafe.ShowSnackbarAsync("Lançamento manual disponível em breve.");
	});
}
