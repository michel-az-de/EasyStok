using CommunityToolkit.Maui.Views;
using EasyStok.Mobile.Models;
using EasyStok.Mobile.Storage;
using EasyStok.Mobile.ViewModels;

namespace EasyStok.Mobile.Views;

public partial class ProducaoPage : ContentPage
{
	private readonly ProducaoViewModel _vm;

	public ProducaoPage(ProducaoViewModel vm)
	{
		InitializeComponent();
		BindingContext = _vm = vm;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await _vm.InitializeAsync();
	}

	// Codigo-behind orquestra o popup pra evitar acoplar IPopupService no
	// VM (o popup precisa abrir relativo a Page; manter esse passo aqui
	// deixa o VM testavel sem dependencia de UI).
	private async void OnIncrementClicked(object? sender, EventArgs e)
	{
		if (sender is not Button btn || btn.CommandParameter is not CachedItemEstoque item) return;

		var popup = new ProducaoCapturaPopup(item);
		var raw = await this.ShowPopupAsync(popup);
		if (raw is not CapturaProducaoResult result) return;

		await _vm.HandleIncrementAsync(item, result);
	}

	private async void OnDecrementClicked(object? sender, EventArgs e)
	{
		if (sender is not Button btn || btn.CommandParameter is not CachedItemEstoque item) return;
		await _vm.HandleDecrementAsync(item);
	}
}
