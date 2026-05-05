using EasyStok.Mobile.Models;
using EasyStok.Mobile.Services;
using EasyStok.Mobile.Storage;
using EasyStok.Mobile.ViewModels;

namespace EasyStok.Mobile.Views;

public partial class ProducaoPage : ContentPage
{
	private readonly ProducaoViewModel _vm;
	private readonly AppIdentity _identity;

	public ProducaoPage(ProducaoViewModel vm, AppIdentity identity)
	{
		InitializeComponent();
		BindingContext = _vm = vm;
		_identity = identity;
		Header.Bind(identity);
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		UiSafe.Fire(() => _vm.InitializeAsync());
	}

	// Stepper inline: +1 direto sem modal. Modal de captura (foto/peso/validade)
	// volta em iteracao futura como acao secundaria (long-press ou menu).
	private void OnIncrementClicked(object? sender, EventArgs e) =>
		UiSafe.Fire(async () =>
		{
			if (sender is not Button btn || btn.CommandParameter is not CachedItemEstoque item) return;
			var capture = new CapturaProducaoResult(1, null, null, null);
			await _vm.HandleIncrementAsync(item, capture);
		});

	private void OnDecrementClicked(object? sender, EventArgs e) =>
		UiSafe.Fire(async () =>
		{
			if (sender is not Button btn || btn.CommandParameter is not CachedItemEstoque item) return;
			await _vm.HandleDecrementAsync(item);
		});
}
