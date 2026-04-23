using System;
using System.Collections.Generic;
using System.Text.Json;

namespace EasyStock.Mobile.DTOs;

/// <summary>
/// Payload enviado pelo app a cada sync. Uma batch de mutations.
/// </summary>
public record SyncPushRequest(string DeviceId, List<MutationDto> Mutations);

/// <summary>
/// Uma unica mudanca de estado. Type e "entidade.operacao" (ex: "order.upsert", "product.upsert").
/// Payload e o objeto serializado conforme o tipo.
/// </summary>
public record MutationDto(string Id, string DeviceId, string Type, JsonElement Payload, long Ts);

/// <summary>
/// Resposta do push. acceptedIds lista os ids das mutations aceitas.
/// Se o servidor rejeitar alguma (ex: conflito irreconciliavel), retorna em rejected.
/// </summary>
public record SyncPushResponse(List<string> AcceptedIds, List<SyncConflict>? Rejected = null);

public record SyncConflict(string MutationId, string Reason);

/// <summary>
/// Resposta do pull: mudancas feitas no servidor ou por outros devices
/// apos o timestamp "since".
/// </summary>
public record SyncPullResponse(long ServerTime, List<MutationDto> Mutations);
