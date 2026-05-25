using CommunityToolkit.Mvvm.ComponentModel;
using EasyStok.Mobile.Storage;
using System.Collections.ObjectModel;

namespace EasyStok.Mobile.ViewModels;

public sealed partial class ClientesViewModel : BaseViewModel
{
    private readonly AppDatabase _db;
    private readonly ISecureStore _store;
    private List<CachedCliente> _all = new();

    public ObservableCollection<CachedCliente> Itens { get; } = new();

    [ObservableProperty]
    private string _busca = string.Empty;

    [ObservableProperty]
    private bool _semDados;

    public ClientesViewModel(AppDatabase db, ISecureStore store) { _db = db; _store = store; }

    partial void OnBuscaChanged(string value) => ApplyBusca();

    public Task InitializeAsync() => RunAsync(async () =>
    {
        var empresaId = await _store.GetEmpresaIdAsync();
        if (empresaId is null) return;
        var conn = await _db.GetConnectionAsync();
        _all = await conn.Table<CachedCliente>()
            .Where(x => x.EmpresaId == empresaId.Value)
            .OrderBy(x => x.Nome)
            .ToListAsync();
        ApplyBusca();
    });

    private void ApplyBusca()
    {
        Itens.Clear();
        var q = (Busca ?? string.Empty).Trim().ToLowerInvariant();
        var filtrados = string.IsNullOrEmpty(q)
            ? _all
            : _all.Where(c => c.Nome.ToLowerInvariant().Contains(q) ||
                              (c.Telefone?.Contains(q) == true)).ToList();
        foreach (var c in filtrados) Itens.Add(c);
        SemDados = Itens.Count == 0;
    }
}
