using CommunityToolkit.Mvvm.ComponentModel;
using EasyStok.Mobile.Storage;
using System.Collections.ObjectModel;

namespace EasyStok.Mobile.ViewModels;

public sealed partial class HistoricoViewModel : BaseViewModel
{
    private readonly AppDatabase _db;

    public ObservableCollection<AuditLogEntry> Entries { get; } = new();

    [ObservableProperty]
    private bool _semDados;

    public HistoricoViewModel(AppDatabase db) { _db = db; }

    public Task InitializeAsync() => RunAsync(async () =>
    {
        var conn = await _db.GetConnectionAsync();
        var rows = await conn.Table<AuditLogEntry>()
            .OrderByDescending(x => x.AtUtc)
            .Take(1000)
            .ToListAsync();
        Entries.Clear();
        foreach (var row in rows) Entries.Add(row);
        SemDados = Entries.Count == 0;
    });
}
