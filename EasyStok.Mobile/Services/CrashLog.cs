namespace EasyStok.Mobile.Services;

/// <summary>
/// Logger de crash de ultima linha — escreve excecoes em
/// <c>FileSystem.AppDataDirectory/crash.log</c>. Usado por handlers
/// globais (UnhandledException, UnobservedTaskException) e por
/// catch-all em entry points (App.CreateWindow, AppShell.OnNavigated).
/// Append-only, melhor esforco — falhas no proprio logger sao
/// engolidas para nao mascarar o erro real.
/// </summary>
public static class CrashLog
{
	private static readonly object _lock = new();

	public static void Write(string source, Exception ex)
	{
		try
		{
			var path = Path.Combine(FileSystem.Current.AppDataDirectory, "crash.log");
			var line = $"[{DateTime.UtcNow:O}] {source}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n\n";
			lock (_lock)
			{
				File.AppendAllText(path, line);
			}
		}
		catch
		{
			// Best-effort. Nao engole o crash original.
		}
	}

	public static string LogPath =>
		Path.Combine(FileSystem.Current.AppDataDirectory, "crash.log");

	public static string? ReadAll()
	{
		try
		{
			return File.Exists(LogPath) ? File.ReadAllText(LogPath) : null;
		}
		catch { return null; }
	}

	public static void Clear()
	{
		try { if (File.Exists(LogPath)) File.Delete(LogPath); } catch { }
	}
}
