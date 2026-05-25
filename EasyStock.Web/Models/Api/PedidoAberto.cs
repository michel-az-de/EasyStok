using System.Text.Json.Serialization;

namespace EasyStock.Web.Models.Api;

public record PedidoAberto
{
    public required string PedidoId { get; init; }
    public DateTime DataPedido { get; init; }
    public DateTime? PrevisaoEntrega { get; init; }
    public decimal? ValorEstimado { get; init; }
    [JsonConverter(typeof(EnumStringOrIntConverter))]
    public int Status { get; init; }
    public string? Canal { get; init; }
    public string? Tracking { get; init; }
    public string? Observacoes { get; init; }
    public required string FornecedorId { get; init; }
    public required string FornecedorNome { get; init; }

    public string StatusLabel => Status switch
    {
        1 => "Aberto",
        2 => "Em Trânsito",
        3 => "Recebido",
        4 => "Cancelado",
        _ => Status.ToString()
    };

    public string StatusCssClass => Status switch
    {
        1 => "bg-yellow-100 text-yellow-700",
        2 => "bg-blue-100 text-blue-700",
        3 => "bg-green-100 text-green-700",
        4 => "bg-red-100 text-red-700",
        _ => "bg-gray-100 text-gray-600"
    };
}
