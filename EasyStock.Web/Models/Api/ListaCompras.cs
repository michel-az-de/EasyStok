namespace EasyStock.Web.Models.Api;

public record ListaCompras
{
    public required string Id { get; init; }
    public Guid EmpresaId { get; init; }
    public Guid? LojaId { get; init; }
    public required string Nome { get; init; }
    public required string Status { get; init; }
    public string? Observacoes { get; init; }
    public Guid? CriadaPorUserId { get; init; }
    public string? CriadaPorNome { get; init; }
    public string? Origem { get; init; }
    public int TotalItens { get; init; }
    public int ItensFeitos { get; init; }
    public int ItensPendentes { get; init; }
    public DateTime CriadoEm { get; init; }
    public DateTime AlteradoEm { get; init; }
    public DateTime? ArquivadoEm { get; init; }
}

public record ListaComprasDetalhe
{
    public required ListaCompras Lista { get; init; }
    public List<ItemListaComprasDto> Itens { get; init; } = new();
}

public record ItemListaComprasDto(
    string Id, string ListaComprasId, Guid? ProdutoId,
    string Texto, decimal? Quantidade, string? Unidade,
    string? Observacao, string? Categoria,
    bool Done, DateTime? DoneEm, Guid? DonePorUserId, string? DonePorNome,
    DateTime CriadoEm, DateTime AlteradoEm
);
