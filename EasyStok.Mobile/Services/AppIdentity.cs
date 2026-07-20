using CommunityToolkit.Mvvm.ComponentModel;

namespace EasyStok.Mobile.Services;

/// <summary>
/// Estado white label persistido em <see cref="Preferences"/>: nome da
/// empresa, operador, codigo curto da loja. Usado pelo BrandHeader em
/// todas as telas. Editavel em Ajustes (SuportePage).
/// </summary>
public sealed partial class AppIdentity : ObservableObject
{
	private const string KeyEmpresa = "easystok.id.empresa";
	private const string KeyOperador = "easystok.id.operador";
	private const string KeyLoja = "easystok.id.loja";
	private const string KeyDeviceId = "easystok.id.device";

	[ObservableProperty]
	private string _empresaNome = "Minha empresa";

	[ObservableProperty]
	private string _operadorNome = "Felipe";

	[ObservableProperty]
	private string _lojaCodigo = "T1";

	/// <summary>
	/// issue 917 Fase B: id estavel do APARELHO (nao da empresa/operador — sobrevive troca
	/// de conta/loja no mesmo device), gerado uma vez e persistido em Preferences. Usado como
	/// prefixo da chave de idempotencia do outbox (<see cref="OutboxFlushService"/>) — sem ele,
	/// o Id local do OutboxItem (int autoincrement POR APARELHO) colidiria entre dois devices
	/// da mesma empresa na unicidade (Key, EmpresaId, MetodoRecurso) do servidor, e o segundo
	/// receberia replay da venda do primeiro. Tambem mandado como header X-Device-Id (ja lido
	/// pela Api em CurrentUserAccessor.DispositivoId) para auditoria.
	/// </summary>
	public string DeviceId { get; }

	public AppIdentity()
	{
		EmpresaNome = Preferences.Default.Get(KeyEmpresa, "Minha empresa");
		OperadorNome = Preferences.Default.Get(KeyOperador, "Felipe");
		LojaCodigo = Preferences.Default.Get(KeyLoja, "T1");

		DeviceId = Preferences.Default.Get(KeyDeviceId, string.Empty);
		if (string.IsNullOrEmpty(DeviceId))
		{
			DeviceId = Guid.NewGuid().ToString("N");
			Preferences.Default.Set(KeyDeviceId, DeviceId);
		}
	}

	partial void OnEmpresaNomeChanged(string value) =>
		Preferences.Default.Set(KeyEmpresa, value ?? string.Empty);

	partial void OnOperadorNomeChanged(string value) =>
		Preferences.Default.Set(KeyOperador, value ?? string.Empty);

	partial void OnLojaCodigoChanged(string value) =>
		Preferences.Default.Set(KeyLoja, value ?? string.Empty);

	public static string SaudacaoPorHora()
	{
		var h = DateTime.Now.Hour;
		return h < 12 ? "Bom dia," : h < 18 ? "Boa tarde," : "Boa noite,";
	}
}
