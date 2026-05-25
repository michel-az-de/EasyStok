using CommunityToolkit.Maui;
using EasyStok.Mobile.Network;
using EasyStok.Mobile.Services;
using EasyStok.Mobile.Storage;
using EasyStok.Mobile.ViewModels;
using EasyStok.Mobile.Views;
using Microsoft.Extensions.Logging;

namespace EasyStok.Mobile;

public static class MauiProgram
{
    private const string AuthClientName = "easystok-api";
    private const string NoAuthClientName = "easystok-api-noauth";

    public static MauiApp CreateMauiApp()
    {
        try
        {
            return BuildApp();
        }
        catch (Exception ex)
        {
            BootCrashLog.Write("MauiProgram.CreateMauiApp", ex);
            throw;
        }
    }

    private static MauiApp BuildApp()
    {
        SQLitePCL.Batteries_V2.Init();

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                // Tipografia oficial EasyStok:
                //   Inter (variable) -> body/labels/botoes
                //   Fraunces / FrauncesItalic (variable) -> display/accent
                fonts.AddFont("Inter.ttf", "Inter");
                fonts.AddFont("Fraunces.ttf", "Fraunces");
                fonts.AddFont("FrauncesItalic.ttf", "FrauncesItalic");
            });

        // WebView platform-specific config: PWA usa localStorage + Service Worker
        // + fetch de assets locais — sem essas configuracoes o PWA roda quebrado
        // dentro de file:///android_asset/.
        Microsoft.Maui.Handlers.WebViewHandler.Mapper.AppendToMapping("EasyStokWebViewSettings", (handler, view) =>
        {
#if ANDROID
            try
            {
                var nativeWebView = handler.PlatformView;
                var settings = nativeWebView.Settings;
                settings.JavaScriptEnabled = true;
                settings.DomStorageEnabled = true;          // localStorage / sessionStorage
                settings.DatabaseEnabled = true;
                settings.AllowFileAccess = true;
                settings.AllowContentAccess = true;
#pragma warning disable CA1422 // deprecated em API 30+ mas ainda funciona
                settings.AllowFileAccessFromFileURLs = true;
                settings.AllowUniversalAccessFromFileURLs = true;
#pragma warning restore CA1422
                settings.MixedContentMode = Android.Webkit.MixedContentHandling.AlwaysAllow;
                settings.LoadWithOverviewMode = false;
                settings.UseWideViewPort = false;
                settings.JavaScriptCanOpenWindowsAutomatically = true;
                settings.SetSupportZoom(false);
                settings.BuiltInZoomControls = false;
                settings.DisplayZoomControls = false;
                nativeWebView.SetBackgroundColor(Android.Graphics.Color.Transparent);
                nativeWebView.OverScrollMode = Android.Views.OverScrollMode.Never;
            }
            catch (Exception ex) { CrashLog.Write("WebViewHandler.Mapper", ex); }
#endif
        });

        // Storage seguro + DB local + caches + outbox
        builder.Services.AddSingleton<ISecureStore, SecureStore>();
        builder.Services.AddSingleton<AppDatabase>();
        builder.Services.AddSingleton<IEstoqueCache, EstoqueCache>();
        builder.Services.AddSingleton<IOutboxRepository, OutboxRepository>();

        builder.Services.AddTransient<AutenticacaoHandler>();

        builder.Services.AddHttpClient(NoAuthClientName, http =>
        {
            http.BaseAddress = new Uri(AppConfig.GetBaseUrl());
            http.Timeout = TimeSpan.FromSeconds(15);
        });

        builder.Services.AddHttpClient(AuthClientName, http =>
        {
            http.BaseAddress = new Uri(AppConfig.GetBaseUrl());
            http.Timeout = TimeSpan.FromSeconds(15);
        }).AddHttpMessageHandler<AutenticacaoHandler>();

        builder.Services.AddSingleton<IAutenticacaoService, AutenticacaoService>();
        builder.Services.AddSingleton<IEmpresaService, EmpresaService>();
        builder.Services.AddSingleton<IPermissaoService, PermissaoService>();
        builder.Services.AddSingleton<IEstoqueService, EstoqueService>();
        builder.Services.AddSingleton<IOutboxFlushService, OutboxFlushService>();
        builder.Services.AddSingleton<IEstoqueMutationService, EstoqueMutationService>();
        builder.Services.AddSingleton<IDemoSeedService, DemoSeedService>();
        builder.Services.AddSingleton<SyncEngine>();
        builder.Services.AddSingleton<ThemeService>();
        builder.Services.AddSingleton<AppIdentity>();

        builder.Services.AddSingleton<AppShell>();

        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<TenantPickerViewModel>();
        builder.Services.AddTransient<LojaPickerViewModel>();
        builder.Services.AddTransient<ProducaoViewModel>();
        builder.Services.AddTransient<SuporteViewModel>();
        builder.Services.AddTransient<EstoqueViewModel>();
        builder.Services.AddTransient<ComprasViewModel>();
        builder.Services.AddTransient<HistoricoViewModel>();
        builder.Services.AddTransient<PedidosViewModel>();
        builder.Services.AddTransient<ClientesViewModel>();
        builder.Services.AddTransient<CaixaViewModel>();
        builder.Services.AddTransient<FinalizadosViewModel>();
        builder.Services.AddTransient<ConferenciaViewModel>();

        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<TenantPickerPage>();
        builder.Services.AddTransient<LojaPickerPage>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<ProducaoPage>();
        builder.Services.AddTransient<PedidosPage>();
        builder.Services.AddTransient<CaixaPage>();
        builder.Services.AddTransient<FinalizadosPage>();
        builder.Services.AddTransient<ClientesPage>();
        builder.Services.AddTransient<SuportePage>();
        builder.Services.AddTransient<ConferenciaPage>();
        builder.Services.AddTransient<ComprasPage>();
        builder.Services.AddTransient<HistoricoPage>();
        builder.Services.AddTransient<EstoquePage>();
        builder.Services.AddTransient<MaisPage>();
        builder.Services.AddTransient<WebOpsPage>();
        // ProducaoCapturaPage e instanciado direto via `new` (recebe parametro
        // CachedItemEstoque), nao registrado no DI.

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
