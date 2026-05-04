namespace EasyStok.Mobile.Models;

/// <summary>
/// Espelha o enum <c>EasyStock.Domain.Enums.NivelAcesso</c> do backend.
/// Ordem importa: numeros menores tem mais privilegios. SuperAdmin pode
/// tudo; Visualizador, quase nada.
/// </summary>
public enum NivelAcesso
{
	SuperAdmin = 0,
	Admin = 1,
	Gerente = 2,
	Operador = 3,
	Visualizador = 4
}

public static class NivelAcessoExtensions
{
	public static NivelAcesso ParseOrDefault(string? raw, NivelAcesso fallback = NivelAcesso.Visualizador) =>
		Enum.TryParse<NivelAcesso>(raw, ignoreCase: true, out var v) ? v : fallback;

	/// <summary>true se <paramref name="atual"/> tem privilegio igual ou maior que <paramref name="minimo"/>.</summary>
	public static bool TemAcessoMinimo(this NivelAcesso atual, NivelAcesso minimo) =>
		(int)atual <= (int)minimo;
}

/// <summary>
/// Espelha o enum <c>EasyStock.Domain.Enums.Permissao</c>.
/// Strings batem com os valores serializados nas claims "permissao" do JWT.
/// </summary>
public static class Permissoes
{
	public const string GerenciarLojas = "GerenciarLojas";
	public const string GerenciarUsuarios = "GerenciarUsuarios";
	public const string GerenciarProdutos = "GerenciarProdutos";
	public const string GerenciarEstoque = "GerenciarEstoque";
	public const string GerenciarFornecedores = "GerenciarFornecedores";
	public const string VisualizarRelatorios = "VisualizarRelatorios";
	public const string GerarRelatorioVendas = "GerarRelatorioVendas";
	public const string AcessarInteligencia = "AcessarInteligencia";
	public const string VisualizarTickets = "VisualizarTickets";
	public const string ResponderTickets = "ResponderTickets";
	public const string GerenciarTickets = "GerenciarTickets";
}
