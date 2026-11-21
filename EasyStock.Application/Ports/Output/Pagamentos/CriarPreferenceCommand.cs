namespace EasyStock.Application.Ports.Output.Pagamentos;

/// <summary>
/// Dados necessários para criar uma Preference MercadoPago. <see cref="ExpiraEm"/> liga
/// <c>expires</c>/<c>expiration_date_to</c> (S11: 30 min); <see cref="IdempotencyKey"/> vai no header
/// <c>X-Idempotency-Key</c> (uma chave por cobrança do pedido). <see cref="RetornoUrl"/> (#2228) troca as
/// <c>back_urls</c> do site: a cobrança da conversa devolve o cliente ao WhatsApp da loja.
/// </summary>
public sealed record CriarPreferenceCommand(
    Guid PedidoId,
    Guid StorefrontId,
    string StorefrontNome,
    decimal ValorTotal,
    IReadOnlyList<PreferenceItemCommand> Items,
    string? ClienteEmail = null,
    DateTime? ExpiraEm = null,
    string? IdempotencyKey = null,
    string? RetornoUrl = null);

public sealed record PreferenceItemCommand(
    string Titulo,
    int Quantidade,
    decimal PrecoUnitario);
