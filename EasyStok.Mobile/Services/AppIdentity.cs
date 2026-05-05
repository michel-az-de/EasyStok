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

	[ObservableProperty]
	private string _empresaNome = "Minha empresa";

	[ObservableProperty]
	private string _operadorNome = "Felipe";

	[ObservableProperty]
	private string _lojaCodigo = "T1";

	public AppIdentity()
	{
		EmpresaNome = Preferences.Default.Get(KeyEmpresa, "Minha empresa");
		OperadorNome = Preferences.Default.Get(KeyOperador, "Felipe");
		LojaCodigo = Preferences.Default.Get(KeyLoja, "T1");
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
