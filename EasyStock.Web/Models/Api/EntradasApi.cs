namespace EasyStock.Web.Models.Api;

/// <summary>Resposta da Api ao registrar uma entrada (POST /estoque/entrada).</summary>
public sealed class EntradaCriadaApi
{
    public Guid ItemEstoqueId { get; set; }
    public Guid MovimentacaoId { get; set; }
    public string? DescricaoAnuncio { get; set; }
    public string? ChavePesquisa { get; set; }
}

/// <summary>Item da busca de lotes para o combobox da entrada (GET /api/lotes).</summary>
public sealed class LoteBuscaApi
{
    public string Codigo { get; set; } = "";
    public string? Status { get; set; }
}

/// <summary>Resposta do gerador de codigo de lote (GET /api/lotes/proximo-codigo-entrada).</summary>
public sealed class ProximoCodigoLoteApi
{
    public string Codigo { get; set; } = "";
}
