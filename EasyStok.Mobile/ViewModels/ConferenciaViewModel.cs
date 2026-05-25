using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyStok.Mobile.Storage;

namespace EasyStok.Mobile.ViewModels;

public sealed partial class ConferenciaViewModel : BaseViewModel
{
    private readonly AppDatabase _db;
    private readonly ISecureStore _store;

    [ObservableProperty]
    private string _codigo = string.Empty;

    [ObservableProperty]
    private CachedPedido? _resultado;

    [ObservableProperty]
    private string? _avisoNaoEncontrado;

    public ConferenciaViewModel(AppDatabase db, ISecureStore store) { _db = db; _store = store; }

    [RelayCommand]
    private Task BuscarAsync() => RunAsync(async () =>
    {
        Resultado = null;
        AvisoNaoEncontrado = null;

        if (string.IsNullOrWhiteSpace(Codigo))
        {
            ErrorMessage = "Informe o código.";
            return;
        }

        var empresaId = await _store.GetEmpresaIdAsync();
        if (empresaId is null) return;

        var alvo = Codigo.Trim();
        var conn = await _db.GetConnectionAsync();
        var found = await conn.Table<CachedPedido>()
            .Where(x => x.EmpresaId == empresaId.Value && (x.ShortCode == alvo || x.Id == alvo))
            .FirstOrDefaultAsync();

        if (found is null)
            AvisoNaoEncontrado = $"Nenhum pedido com código \"{alvo}\".";
        else
            Resultado = found;
    });
}
