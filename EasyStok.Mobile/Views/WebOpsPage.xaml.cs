using EasyStok.Mobile.Services;
using EasyStok.Mobile.Storage;

namespace EasyStok.Mobile.Views;

/// <summary>
/// Camada hibrida: HybridWebView local (Resources/Raw/pwa) renderiza o
/// PWA EasyStok inteiro. Login, configuracoes e camera continuam
/// nativas (MAUI). Bridge JS->Native fica para v1.3.1 — por hora
/// usuario sai pelo botao back ou pela rota nativa /suporte.
/// </summary>
public partial class WebOpsPage : ContentPage
{
    private readonly IAutenticacaoService _auth;
    private readonly ISecureStore _store;
    private readonly AppIdentity _identity;

    public WebOpsPage(IAutenticacaoService auth, ISecureStore store, AppIdentity identity)
    {
        InitializeComponent();
        _auth = auth;
        _store = store;
        _identity = identity;
    }
}
