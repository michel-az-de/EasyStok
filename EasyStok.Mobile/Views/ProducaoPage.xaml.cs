using EasyStok.Mobile.Services;
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

	protected override void OnAppearing()
	{
		base.OnAppearing();
		UiSafe.Fire(() => _vm.InitializeAsync());
	}

	private void OnIncrementClicked(object? sender, EventArgs e) =>
		UiSafe.Fire(async () =>
		{
			if (sender is not Button btn || btn.CommandParameter is not CachedItemEstoque item) return;

			var page = new ProducaoCapturaPage(item);
			await Navigation.PushModalAsync(page);
			var result = await page.ResultTask;
			if (result is null) return;

			await _vm.HandleIncrementAsync(item, result);
		});

	private void OnDecrementClicked(object? sender, EventArgs e) =>
		UiSafe.Fire(async () =>
		{
			if (sender is not Button btn || btn.CommandParameter is not CachedItemEstoque item) return;
			await _vm.HandleDecrementAsync(item);
		});
}
