using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.Fiscal.ProcessarWebhookFocusNFe;

public sealed record ProcessarWebhookFocusNFeCommand(
    string ChaveAcesso,
    string Status,
    string? Protocolo,
    DateTime? DhEvento,
    string? XmlEvento,
    string? Codigo,
    string? Motivo,
    string? CorrelationId) : ICommand;

public sealed record ProcessarWebhookFocusNFeResult(bool Processado);
