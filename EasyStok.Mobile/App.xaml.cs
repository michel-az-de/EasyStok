namespace EasyStok.Mobile;

public partial class App : Application
{
	private readonly IServiceProvider _services;

	public App(IServiceProvider services)
	{
		InitializeComponent();
		_services = services;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// AppShell e resolvido via DI para receber IAuthService no construtor.
		var shell = _services.GetRequiredService<AppShell>();
		return new Window(shell);
	}
}
