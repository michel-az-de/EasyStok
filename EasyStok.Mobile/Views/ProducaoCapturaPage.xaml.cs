using EasyStok.Mobile.Models;
using EasyStok.Mobile.Services;
using EasyStok.Mobile.Storage;

namespace EasyStok.Mobile.Views;

/// <summary>
/// Pagina modal que coleta quantidade + peso + validade + foto antes de
/// registrar uma entrada de estoque. Substitui o Popup do CommunityToolkit
/// (que crashava em runtime). Apresentada via Navigation.PushModalAsync;
/// resultado entregue por <see cref="ResultTask"/> (TaskCompletionSource).
/// </summary>
public partial class ProducaoCapturaPage : ContentPage
{
    private readonly TaskCompletionSource<CapturaProducaoResult?> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? _fotoPath;
    private bool _closed;

    public ProducaoCapturaPage(CachedItemEstoque item)
    {
        InitializeComponent();
        ProdutoLabel.Text = item.ProdutoNome;
        ValidadePicker.Date = DateTime.Today.AddDays(7);
    }

    /// <summary>Aguarde isto pra obter o resultado (null = cancelado).</summary>
    public Task<CapturaProducaoResult?> ResultTask => _tcs.Task;

    protected override bool OnBackButtonPressed()
    {
        // Back fisico = cancelar
        CloseWith(null);
        return true;
    }

    private void OnLimparValidade(object? sender, EventArgs e) =>
        UiSafe.Fire(() =>
        {
            ValidadePicker.Date = DateTime.Today;
            return Task.CompletedTask;
        });

    private void OnCapturarFoto(object? sender, EventArgs e) =>
        UiSafe.Fire(async () =>
        {
            HideError();

            if (!MediaPicker.Default.IsCaptureSupported)
            {
                ShowError("Câmera indisponível neste device.");
                return;
            }

            // Permissao runtime — sem isso, CapturePhotoAsync lanca SecurityException
            // nativa que NAO e PermissionException, escapa do catch generico e crasha.
            var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.Camera>();
                if (status != PermissionStatus.Granted)
                {
                    ShowError("Permissão de câmera negada.");
                    return;
                }
            }

            var photo = await MediaPicker.Default.CapturePhotoAsync();
            if (photo is null) return;

            var dir = Path.Combine(FileSystem.Current.AppDataDirectory, "photos");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, $"{Guid.NewGuid():N}.jpg");

            using (var src = await photo.OpenReadAsync())
            using (var fs = File.Create(dest))
                await src.CopyToAsync(fs);

            _fotoPath = dest;
            FotoPreview.Source = ImageSource.FromFile(dest);
        });

    private void OnCancelar(object? sender, EventArgs e) =>
        UiSafe.Fire(() => CloseAsync(null));

    private void OnConfirmar(object? sender, EventArgs e) =>
        UiSafe.Fire(async () =>
        {
            HideError();

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
                    ShowError("Peso inválido.");
                    return;
                }
                peso = p;
            }

            DateTime? validade = ValidadePicker.Date == DateTime.Today
                ? null
                : ValidadePicker.Date.ToUniversalTime();

            var result = new CapturaProducaoResult(qty, peso, validade, _fotoPath);
            await CloseAsync(result);
        });

    private async Task CloseAsync(CapturaProducaoResult? result)
    {
        if (_closed) return;
        _closed = true;
        _tcs.TrySetResult(result);
        if (Navigation.ModalStack.Contains(this))
            await Navigation.PopModalAsync();
    }

    private void CloseWith(CapturaProducaoResult? result)
    {
        if (_closed) return;
        _closed = true;
        _tcs.TrySetResult(result);
    }

    private void ShowError(string text)
    {
        ErroLabel.Text = text;
        ErroBorder.IsVisible = true;
    }

    private void HideError() => ErroBorder.IsVisible = false;
}
