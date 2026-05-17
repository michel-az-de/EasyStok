using EasyStock.Domain.Fiscal;

namespace EasyStock.Application.UseCases.Fiscal.EmitirNfce;

/// <summary>
/// Resultado de <see cref="EmitirNfceUseCase.ExecuteAsync"/>. Indica o status final
/// da emissao apos commit:
/// <list type="bullet">
///   <item><see cref="StatusNfe.Autorizada"/>: chave + protocolo + URL DANFE preenchidos.</item>
///   <item><see cref="StatusNfe.Rejeitada"/>: MotivoRejeicao preenchido; ChaveAcesso pode estar vazia.</item>
///   <item><see cref="StatusNfe.FalhaTransiente"/>: falha de rede/SEFAZ down; job de contingencia ira reprocessar.</item>
/// </list>
/// </summary>
public sealed record EmitirNfceResult(
    Guid NfeId,
    StatusNfe Status,
    string? ChaveAcesso,
    string? ProtocoloAutorizacao,
    DateTime? DataAutorizacao,
    string? MotivoRejeicao,
    string? DanfeUrl);
