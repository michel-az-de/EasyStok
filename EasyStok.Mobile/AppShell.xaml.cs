using EasyStok.Mobile.Services;
using EasyStok.Mobile.Storage;
using EasyStok.Mobile.Views;

namespace EasyStok.Mobile;

public partial class AppShell : Shell
{
    private readonly IAutenticacaoService _auth;
    private readonly ISecureStore _store;
    private readonly SyncEngine _sync;
    private readonly IDemoSeedService _demoSeed;
    private bool _initialized;

    public AppShell(IAutenticacaoService auth, ISecureStore store, SyncEngine sync, IDemoSeedService demoSeed)
    {
        InitializeComponent();
        _auth = auth;
        _store = store;
        _sync = sync;
        _demoSeed = demoSeed;
        RegisterRoutes();
    }

    private static void RegisterRoutes()
    {
        // V1.3 hibrido: rotas nativas residuais (uteis pra debug, mas
        // telas operacionais agora vivem dentro do PWA via WebOpsPage).
        Routing.RegisterRoute("caixa", typeof(CaixaPage));
        Routing.RegisterRoute("finalizados", typeof(FinalizadosPage));
        Routing.RegisterRoute("clientes", typeof(ClientesPage));
        Routing.RegisterRoute("conferencia", typeof(ConferenciaPage));
        Routing.RegisterRoute("compras", typeof(ComprasPage));
        Routing.RegisterRoute("historico", typeof(HistoricoPage));
        Routing.RegisterRoute("estoque", typeof(EstoquePage));
        Routing.RegisterRoute("producao", typeof(ProducaoPage));
        Routing.RegisterRoute("pedidos", typeof(PedidosPage));
        Routing.RegisterRoute("home", typeof(HomePage));
        Routing.RegisterRoute("mais", typeof(MaisPage));
    }

    protected override void OnNavigated(ShellNavigatedEventArgs args)
    {
        base.OnNavigated(args);
        if (_initialized) return;
        _initialized = true;

        UiSafe.Fire(InitializeSessionAsync);
    }

    private async Task InitializeSessionAsync()
    {
        if (!await _auth.EstaAutenticadoAsync())
        {
            await GoToAsync("//login");
            return;
        }

        var empresaId = await _store.GetEmpresaIdAsync()
            ?? await _auth.GetEmpresaIdFromTokenAsync();
        if (empresaId is null)
        {
            await GoToAsync("//tenant-picker");
            return;
        }

        if (await _store.GetLojaIdAsync() is null)
        {
            await GoToAsync("//loja-picker");
            return;
        }

        // Sessao demo idempotente: garante que o seed esta populado
        // (caso o app tenha sido reaberto e Preferences/SQLite estejam vazios).
        if (await _auth.IsDemoAsync())
            await _demoSeed.SeedIfEmptyAsync();

        // V1.3 hibrido: pos-login vai pra WebOpsPage (PWA dentro do app).
        await GoToAsync("//web-ops");
        _sync.Start();
    }
}
