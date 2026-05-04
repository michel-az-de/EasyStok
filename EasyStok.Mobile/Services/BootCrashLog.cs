namespace EasyStok.Mobile.Services;

/// <summary>
/// Logger de crash de boot — escreve em <c>/sdcard/Download/easystok-boot-crash.log</c>
/// quando o crash acontece ANTES de FileSystem.Current estar utilizavel
/// (ex: dentro do MauiProgram.CreateMauiApp). Caminho publico no Download
/// pra que o operador consiga abrir o arquivo via gerenciador de arquivos
/// sem depender de root/adb.
/// </summary>
public static class BootCrashLog
{
	private const string FileName = "easystok-boot-crash.log";

	private static readonly string[] _fallbackPaths = new[]
	{
		"/sdcard/Download/" + FileName,
		"/storage/emulated/0/Download/" + FileName,
		Path.Combine(Path.GetTempPath(), FileName),
	};

	public static void Write(string source, Exception ex)
	{
		var line = $"[{DateTime.UtcNow:O}] {source}: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}\n\n";
		foreach (var path in _fallbackPaths)
		{
			try
			{
				var dir = Path.GetDirectoryName(path);
				if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
				File.AppendAllText(path, line);
				return; // sucesso no primeiro caminho que aceitou
			}
			catch
			{
				// Tenta o proximo caminho.
			}
		}
	}
}
