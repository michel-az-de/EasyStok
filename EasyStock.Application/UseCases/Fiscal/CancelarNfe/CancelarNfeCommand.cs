using System.ComponentModel.DataAnnotations;
using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.Fiscal.CancelarNfe;

/// <summary>
/// Solicita cancelamento de NFC-e ja autorizada. SEFAZ aceita cancelamento
/// em ate 24h (prazo padrao) — apos esse periodo o caller recebe erro
/// <see cref="EasyStock.Application.Ports.Output.Fiscal.GatewayFiscalRejeitadaException"/>.
///
/// <para>
/// <b>Motivo:</b> SEFAZ exige minimo 15 caracteres.
/// </para>
/// </summary>
public sealed record CancelarNfeCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid NfeId,
    [property: Required][property: MinLength(15)][property: MaxLength(255)] string Motivo,
    Guid? UsuarioId = null,
    [property: MaxLength(120)] string? UsuarioNome = null,
    [property: MaxLength(20)] string? Origem = "web") : ICommand;
