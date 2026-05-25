using CommunityToolkit.Mvvm.ComponentModel;
using EasyStok.Mobile.Storage;
using System.Collections.ObjectModel;

namespace EasyStok.Mobile.ViewModels;

public sealed partial class PedidosViewModel : BaseViewModel
{
    private readonly AppDatabase _db;
    private readonly ISecureStore _store;

    public ObservableCollection<CachedPedido> Aguardando { get; } = new();
    public ObservableCollection<CachedPedido> Preparando { get; } = new();
    public ObservableCollection<CachedPedido> Pronto { get; } = new();

    [ObservableProperty]
    private int _totalAbertos;

    public PedidosViewModel(AppDatabase db, ISecureStore store) { _db = db; _store = store; }

    public Task InitializeAsync() => RunAsync(async () =>
    {
        var empresaId = await _store.GetEmpresaIdAsync();
        if (empresaId is null) return;

        var conn = await _db.GetConnectionAsync();
        var rows = await conn.Table<CachedPedido>()
            .Where(x => x.EmpresaId == empresaId.Value)
            .OrderByDescending(x => x.AtualizadoUtc)
            .ToListAsync();

        Aguardando.Clear(); Preparando.Clear(); Pronto.Clear();
        foreach (var p in rows)
        {
            switch (p.Status)
            {
                case "aguardando": Aguardando.Add(p); break;
                case "preparando": Preparando.Add(p); break;
                case "pronto": Pronto.Add(p); break;
            }
        }
        TotalAbertos = Aguardando.Count + Preparando.Count + Pronto.Count;
    });
}
