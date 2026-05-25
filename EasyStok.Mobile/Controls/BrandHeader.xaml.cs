using EasyStok.Mobile.Services;

namespace EasyStok.Mobile.Controls;

public partial class BrandHeader : ContentView
{
    public static readonly BindableProperty EmpresaNomeProperty =
        BindableProperty.Create(nameof(EmpresaNome), typeof(string), typeof(BrandHeader), "Minha empresa");

    public static readonly BindableProperty OperadorNomeProperty =
        BindableProperty.Create(nameof(OperadorNome), typeof(string), typeof(BrandHeader), "Felipe");

    public static readonly BindableProperty LojaCodigoProperty =
        BindableProperty.Create(nameof(LojaCodigo), typeof(string), typeof(BrandHeader), "T1");

    public static readonly BindableProperty SaudacaoProperty =
        BindableProperty.Create(nameof(Saudacao), typeof(string), typeof(BrandHeader), "Boa noite,");

    public string EmpresaNome { get => (string)GetValue(EmpresaNomeProperty); set => SetValue(EmpresaNomeProperty, value); }
    public string OperadorNome { get => (string)GetValue(OperadorNomeProperty); set => SetValue(OperadorNomeProperty, value); }
    public string LojaCodigo { get => (string)GetValue(LojaCodigoProperty); set => SetValue(LojaCodigoProperty, value); }
    public string Saudacao { get => (string)GetValue(SaudacaoProperty); set => SetValue(SaudacaoProperty, value); }

    public BrandHeader()
    {
        InitializeComponent();
    }

    public void Bind(AppIdentity identity)
    {
        EmpresaNome = identity.EmpresaNome;
        OperadorNome = identity.OperadorNome;
        LojaCodigo = identity.LojaCodigo;
        Saudacao = AppIdentity.SaudacaoPorHora();
        identity.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppIdentity.EmpresaNome)) EmpresaNome = identity.EmpresaNome;
            if (e.PropertyName == nameof(AppIdentity.OperadorNome)) OperadorNome = identity.OperadorNome;
            if (e.PropertyName == nameof(AppIdentity.LojaCodigo)) LojaCodigo = identity.LojaCodigo;
        };
    }

    private async void OnEditarOperadorTapped(object? sender, TappedEventArgs e) =>
        UiSafe.Fire(async () =>
        {
            var page = Application.Current?.Windows?.FirstOrDefault()?.Page;
            if (page is null) return;
            var novo = await page.DisplayPromptAsync("Operador", "Seu nome (aparece em pedidos e histórico):", initialValue: OperadorNome, maxLength: 40);
            if (!string.IsNullOrWhiteSpace(novo))
                OperadorNome = novo.Trim();
            await Task.CompletedTask;
        });

    private void OnAjustesTapped(object? sender, TappedEventArgs e) =>
        UiSafe.Fire(() => Shell.Current.GoToAsync("suporte"));
}
