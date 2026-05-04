namespace EasyStok.Mobile.Models;

/// <summary>
/// Resultado do popup de captura de producao. Todos os campos sao opcionais
/// — operador pode confirmar so com a quantidade (default 1) sem foto/peso/
/// validade, replicando a UX do PWA Casa da Baba.
/// </summary>
public sealed record CapturaProducaoResult(
	int Quantidade,
	decimal? PesoG,
	DateTime? Validade,
	string? FotoPath);
