namespace EasyStock.Application.Ports.Output.Pagamentos;

/// <summary>Dados necessários para criar uma Preference MercadoPago.</summary>
public sealed record CriarPreferenceCommand(
    Guid PedidoId,
    Guid StorefrontId,
    string StorefrontNome,
    decimal ValorTotal,
    IReadOnlyList<PreferenceItemCommand> Items,
    string? ClienteEmail = null);

public sealed record PreferenceItemCommand(
    string Titulo,
    int Quantidade,
    decimal PrecoUnitario);
