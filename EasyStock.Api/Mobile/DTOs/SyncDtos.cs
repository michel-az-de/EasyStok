using System.Text.Json;

namespace EasyStock.Api.Mobile.DTOs;

/// <summary>Payload enviado pelo app a cada sync (batch de mutations).</summary>
public record SyncPushRequest(string DeviceId, List<MutationDto> Mutations);

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

public record SyncConflict(string MutationId, string Reason);

/// <summary>
/// Resposta do pull: mudanças feitas no servidor ou por outros devices
/// após o timestamp <c>since</c>.
/// </summary>
public record SyncPullResponse(long ServerTime, List<MutationDto> Mutations);
