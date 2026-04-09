namespace EasyStock.Web.Models.Api;

public record Notificacao
{
    public required string Id { get; init; }
    public required string Tipo { get; init; }
    public required string Titulo { get; init; }
    public required string Mensagem { get; init; }
    public string? ReferenciaId { get; init; }
    public bool Lida { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
