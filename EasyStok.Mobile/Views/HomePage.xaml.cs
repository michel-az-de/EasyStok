using EasyStok.Mobile.Services;
using EasyStok.Mobile.Storage;

namespace EasyStok.Mobile.Views;

public partial class HomePage : ContentPage
{
    private readonly IAutenticacaoService _auth;
    private readonly ISecureStore _store;
    private readonly AppDatabase _db;
    private readonly AppIdentity _identity;

    public HomePage(IAutenticacaoService auth, ISecureStore store, AppDatabase db, AppIdentity identity)
    {
        InitializeComponent();
        _auth = auth;
        _store = store;
        _db = db;
        _identity = identity;
        Header.Bind(identity);
    }
}
