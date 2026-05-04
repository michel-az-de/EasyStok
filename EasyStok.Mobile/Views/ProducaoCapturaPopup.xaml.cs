using CommunityToolkit.Maui.Views;
using EasyStok.Mobile.Models;
using EasyStok.Mobile.Storage;

namespace EasyStok.Mobile.Views;

/// <summary>
/// Popup que coleta quantidade + peso + validade + foto antes de
/// registrar uma entrada de estoque. Todos campos opcionais exceto
/// quantidade (default 1). Retorna <see cref="CapturaProducaoResult"/>
/// no Close, ou <c>null</c> se cancelado.
/// </summary>
public partial class ProducaoCapturaPopup : Popup
{
	private string? _fotoPath;

	public ProducaoCapturaPopup(CachedItemEstoque item)
	{
		InitializeComponent();
		ProdutoLabel.Text = item.ProdutoNome;
		ValidadePicker.Date = DateTime.Today.AddDays(7);
	}

	private void OnLimparValidade(object? sender, EventArgs e)
	{
		ValidadePicker.Date = DateTime.Today;
	}

	private async void OnCapturarFoto(object? sender, EventArgs e)
	{
		try
		{
			if (!MediaPicker.Default.IsCaptureSupported)
			{
				ShowError("Camera indisponivel neste device.");
				return;
			}

			var photo = await MediaPicker.Default.CapturePhotoAsync();
			if (photo is null) return;

			// Persiste localmente em AppDataDirectory/photos/{guid}.jpg
			var dir = Path.Combine(FileSystem.Current.AppDataDirectory, "photos");
			Directory.CreateDirectory(dir);
			var dest = Path.Combine(dir, $"{Guid.NewGuid():N}.jpg");

			using (var src = await photo.OpenReadAsync())
			using (var fs = File.Create(dest))
				await src.CopyToAsync(fs);

			_fotoPath = dest;
			FotoPreview.Source = ImageSource.FromFile(dest);
			HideError();
		}
		catch (PermissionException)
		{
			ShowError("Permissao de camera negada.");
		}
		catch (Exception ex)
		{
			ShowError($"Falha ao capturar: {ex.Message}");
		}
	}

	private async void OnCancelar(object? sender, EventArgs e)
	{
		await CloseAsync(result: null);
	}

	private async void OnConfirmar(object? sender, EventArgs e)
	{
		if (!int.TryParse(QuantidadeEntry.Text, out var qty) || qty <= 0)
		{
			ShowError("Quantidade deve ser maior que zero.");
			return;
		}

		decimal? peso = null;
		if (!string.IsNullOrWhiteSpace(PesoEntry.Text))
		{
			if (!decimal.TryParse(PesoEntry.Text, out var p) || p < 0)
			{
				ShowError("Peso invalido.");
				return;
			}
			peso = p;
		}

		// Validade: se DatePicker estiver no dia atual (foi limpo) considera null.
		DateTime? validade = ValidadePicker.Date == DateTime.Today
			? null
			: ValidadePicker.Date.ToUniversalTime();

		var result = new CapturaProducaoResult(qty, peso, validade, _fotoPath);
		await CloseAsync(result);
	}

	private void ShowError(string text)
	{
		ErroLabel.Text = text;
		ErroLabel.IsVisible = true;
	}

	private void HideError() => ErroLabel.IsVisible = false;
}
