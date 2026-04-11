namespace EasyStock.Web.Models.Api;

public record Notificacao
{
    public required string Id { get; init; }
    public required string Tipo { get; init; }
    public required string Titulo { get; init; }
    public required string Mensagem { get; init; }
    public string Severidade { get; init; } = "Media";
    public string? ReferenciaId { get; init; }
    public bool Lida { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    public string AcaoUrl => Tipo switch
    {
        "EstoqueCritico" or "EstoqueBaixo" => $"/estoque?item={ReferenciaId}",
        "ProdutoVencido" => $"/estoque?item={ReferenciaId}",
        "ValidadeProxima" or "ProximoVencimento" => $"/estoque?item={ReferenciaId}",
        "ProdutoParado" => $"/estoque?item={ReferenciaId}",
        "ReposicaoSugerida" or "Reposicao" => $"/entradas/reposicao?item={ReferenciaId}",
        "PedidoAtrasado" => "/fornecedores",
        "PedidoRecebido" => "/fornecedores",
        _ => "/estoque"
    };

    public string AcaoLabel => Tipo switch
    {
        "EstoqueCritico" or "EstoqueBaixo" => "Ver no estoque",
        "ProdutoVencido" => "Ver no estoque",
        "ValidadeProxima" or "ProximoVencimento" => "Ver validade",
        "ProdutoParado" => "Ver item",
        "ReposicaoSugerida" or "Reposicao" => "Repor estoque",
        "PedidoAtrasado" => "Ver pedido",
        "PedidoRecebido" => "Ver pedido",
        _ => "Ver detalhes"
    };
}

public record NotificacaoResumo
{
    public int TotalNaoLidas { get; init; }
    public int Criticas { get; init; }
    public int Altas { get; init; }
    public int Medias { get; init; }
    public int Informativas { get; init; }
    public Dictionary<string, int> PorTipo { get; init; } = new();
}
