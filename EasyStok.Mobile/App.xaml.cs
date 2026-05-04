using EasyStok.Mobile.Services;

namespace EasyStok.Mobile;

public partial class App : Application
{
	private readonly IServiceProvider _services;

	public App(IServiceProvider services)
	{
		// Handlers globais antes do InitializeComponent — pega tudo que vier
		// dali pra frente. AppDomain captura crashes hard (UI thread, ANR);
		// TaskScheduler captura excecoes em Tasks descartadas (fire-and-forget).
		AppDomain.CurrentDomain.UnhandledException += (s, e) =>
			CrashLog.Write("AppDomain.UnhandledException", e.ExceptionObject as Exception ?? new Exception("unknown"));
		TaskScheduler.UnobservedTaskException += (s, e) =>
		{
			CrashLog.Write("TaskScheduler.UnobservedTaskException", e.Exception);
			e.SetObserved();
		};

		InitializeComponent();
		_services = services;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		try
		{
			var shell = _services.GetRequiredService<AppShell>();
			return new Window(shell);
		}
		catch (Exception ex)
		{
			CrashLog.Write("App.CreateWindow", ex);
			// Janela placeholder pra app nao morrer silenciosa — mostra erro pra
			// o operador ao inves de tela preta.
			var fallback = new ContentPage
			{
				BackgroundColor = Colors.Black,
				Content = new VerticalStackLayout
				{
					Padding = 24,
					Spacing = 8,
					VerticalOptions = LayoutOptions.Center,
					Children =
					{
						new Label { Text = "Falha ao iniciar.", TextColor = Colors.White, FontSize = 18 },
						new Label { Text = ex.Message, TextColor = Colors.OrangeRed, FontSize = 12 },
					}
				}
			};
			return new Window(fallback);
		}
	}
}
