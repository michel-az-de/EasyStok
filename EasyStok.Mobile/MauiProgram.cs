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
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("Manrope.ttf", "Manrope");
				fonts.AddFont("Fraunces.ttf", "Fraunces");
				fonts.AddFont("FrauncesItalic.ttf", "FrauncesItalic");
			});

		// Storage seguro + DB local + caches + outbox
		builder.Services.AddSingleton<ISecureStore, SecureStore>();
		builder.Services.AddSingleton<AppDatabase>();
		builder.Services.AddSingleton<IEstoqueCache, EstoqueCache>();
		builder.Services.AddSingleton<IOutboxRepository, OutboxRepository>();

		// HTTP clients:
		// - "easystok-api-noauth" para login/refresh (nao injeta Authorization)
		// - "easystok-api"        para chamadas autenticadas (com AuthHandler)
		builder.Services.AddTransient<AuthHandler>();

		builder.Services.AddHttpClient(NoAuthClientName, http =>
		{
			http.BaseAddress = new Uri(AppConfig.GetBaseUrl());
			http.Timeout = TimeSpan.FromSeconds(15);
		});

		builder.Services.AddHttpClient(AuthClientName, http =>
		{
			http.BaseAddress = new Uri(AppConfig.GetBaseUrl());
			http.Timeout = TimeSpan.FromSeconds(15);
		}).AddHttpMessageHandler<AuthHandler>();

		// Servicos de dominio
		builder.Services.AddSingleton<IAuthService, AuthService>();
		builder.Services.AddSingleton<ICompanyService, CompanyService>();
		builder.Services.AddSingleton<IPermissionService, PermissionService>();
		builder.Services.AddSingleton<IEstoqueService, EstoqueService>();
		builder.Services.AddSingleton<IOutboxFlushService, OutboxFlushService>();
		builder.Services.AddSingleton<IEstoqueMutationService, EstoqueMutationService>();
		builder.Services.AddSingleton<SyncEngine>();

		// Shell (singleton — uma instancia por app)
		builder.Services.AddSingleton<AppShell>();

		// ViewModels (transient — nova instancia por Page)
		builder.Services.AddTransient<LoginViewModel>();
		builder.Services.AddTransient<TenantPickerViewModel>();
		builder.Services.AddTransient<LojaPickerViewModel>();
		builder.Services.AddTransient<ProducaoViewModel>();

		// Views (transient)
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

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
