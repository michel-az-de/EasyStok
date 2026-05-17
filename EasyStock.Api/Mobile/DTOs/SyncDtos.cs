using System.Text.Json;

namespace EasyStock.Api.Mobile.DTOs;

/// <summary>
/// Payload enviado pelo app a cada sync (batch de mutations).
/// <para>
/// <c>OperatorName</c> (opcional): nome do operador informado no PWA
/// (ex.: "Felipe", "Thati"). Usado para auditoria server-side — gravado em
/// <c>LastOperatorName</c> de cada entidade alterada. Se vazio, só
/// <c>DeviceId</c> é usado.
/// </para>
/// </summary>
public record SyncPushRequest(string DeviceId, List<MutationDto> Mutations, string? OperatorName = null);

/// <summary>
/// Uma única mudança de estado. <c>Type</c> é "entidade.operacao" (ex: "order.upsert").
/// <c>Payload</c> é o objeto serializado conforme o tipo.
/// </summary>
public record MutationDto(string Id, string DeviceId, string Type, JsonElement Payload, long Ts);

/// <summary>
/// Resposta do push. <c>AcceptedIds</c> lista os IDs das mutations aplicadas.
/// Rejeitadas (conflito irreconciliável) vão em <c>Rejected</c>.
/// </summary>
public record SyncPushResponse(List<string> AcceptedIds, List<SyncConflict>? Rejected = null);

/// <summary>
/// Mutation rejeitada. <c>Reason</c> tem prefixo: "conflict:..." (last-write-loser),
/// "migrate:N" (schema antigo, PWA precisa transformar pra v{N}), ou texto livre
/// pra outros erros. <c>WinningPayload</c> (C3): quando reason e' "conflict:..."
/// e existe a versao server vencedora, vai aqui — PWA exibe diff visual ao operador
/// (versao local x versao server) antes de sobrescrever.
/// </summary>
public record SyncConflict(string MutationId, string Reason, JsonElement? WinningPayload = null);

/// <summary>
/// Resposta do pull: mudanças feitas no servidor ou por outros devices
/// após o timestamp <c>since</c>.
/// </summary>
public record SyncPullResponse(long ServerTime, List<MutationDto> Mutations);
