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

	protected override void OnAppearing()
	{
		base.OnAppearing();
		UiSafe.Fire(LoadKpisAsync);
	}

	private async Task LoadKpisAsync()
	{
		var empresaId = await _store.GetEmpresaIdAsync();
		if (empresaId is null) return;

		var conn = await _db.GetConnectionAsync();

		var totalItens = await conn.Table<CachedItemEstoque>().Where(x => x.EmpresaId == empresaId.Value).CountAsync();
		var alertas = await conn.Table<CachedItemEstoque>().Where(x => x.EmpresaId == empresaId.Value && (x.Status == "critico" || x.Status == "vencido")).CountAsync();
		var pedidosAbertos = await conn.Table<CachedPedido>().Where(x => x.EmpresaId == empresaId.Value && x.Status != "entregue" && x.Status != "cancelado").CountAsync();

		var hoje = DateTime.UtcNow.Date;
		var amanha = hoje.AddDays(1);
		var entries = await conn.Table<CachedCaixaEntry>().Where(x => x.EmpresaId == empresaId.Value && x.AtUtc >= hoje && x.AtUtc < amanha).ToListAsync();
		var vendasHoje = entries.Where(x => x.Tipo == "entrada").Sum(x => x.Valor);

		KpiItens.Text = totalItens.ToString();
		KpiAlertas.Text = alertas.ToString();
		KpiPedidos.Text = pedidosAbertos.ToString();
		KpiVendas.Text = $"R$ {vendasHoje:N2}";
	}
}
