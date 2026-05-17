using System.ComponentModel.DataAnnotations;
using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.Fiscal.ProcessarWebhookFocusNFe;

/// <summary>
/// Payload do webhook Focus NFe ja validado (HMAC ok) e parseado. Webhook NAO
/// tem JWT — o caller (controller) usa o validator HMAC. Use case faz bypass RLS
/// porque nao tem contexto de tenant: descobre pela <see cref="ChaveAcesso"/>.
/// </summary>
public sealed record ProcessarWebhookFocusNFeCommand(
    [property: Required][property: MinLength(44)][property: MaxLength(44)] string ChaveAcesso,
    [property: Required][property: MaxLength(40)] string StatusGateway,
    string? ProtocoloAutorizacao,
    string? MotivoRejeicao,
    string? XmlAssinadoUrl,
    string? DanfeUrl,
    DateTime? DataEvento) : ICommand;

public sealed record ProcessarWebhookFocusNFeResult(
    bool Aplicado,
    Guid? NfeId,
    string? StatusFinal);
