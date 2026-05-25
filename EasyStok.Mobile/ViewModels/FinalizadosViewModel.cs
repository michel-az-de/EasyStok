using CommunityToolkit.Mvvm.ComponentModel;
using EasyStok.Mobile.Storage;
using System.Collections.ObjectModel;

namespace EasyStok.Mobile.ViewModels;

public sealed partial class FinalizadosViewModel : BaseViewModel
{
    private readonly AppDatabase _db;
    private readonly ISecureStore _store;
    private List<CachedPedido> _all = new();

    public ObservableCollection<CachedPedido> Itens { get; } = new();

    [ObservableProperty]
    private DateTime _dataSelecionada = DateTime.Today;

    [ObservableProperty]
    private bool _semDados;

    public FinalizadosViewModel(AppDatabase db, ISecureStore store) { _db = db; _store = store; }

    partial void OnDataSelecionadaChanged(DateTime value) => ApplyFiltro();

    public Task InitializeAsync() => RunAsync(async () =>
    {
        var empresaId = await _store.GetEmpresaIdAsync();
        if (empresaId is null) return;
        var conn = await _db.GetConnectionAsync();
        _all = await conn.Table<CachedPedido>()
            .Where(x => x.EmpresaId == empresaId.Value && x.Status == "entregue")
            .OrderByDescending(x => x.AtualizadoUtc)
            .ToListAsync();
        ApplyFiltro();
    });

    private void ApplyFiltro()
    {
        Itens.Clear();
        var inicio = DataSelecionada.Date;
        var fim = inicio.AddDays(1);
        var filtrados = _all.Where(p => p.AtualizadoUtc >= inicio && p.AtualizadoUtc < fim).ToList();
        foreach (var p in filtrados) Itens.Add(p);
        SemDados = Itens.Count == 0;
    }
}
