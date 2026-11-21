namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

/// <summary>Conversa com o agente ativo e o cliente esperando a resposta dele (#2283).</summary>
public sealed record TurnoPendente(Guid EmpresaId, Guid ConversaId);

/// <summary>
/// #2283: a fila do turno do agente vive em memória e some quando a Api reinicia. Esta consulta acha, em todos os
/// tenants, o turno que ficou para trás. Roda com o bypass de RLS ligado pelo host.
/// </summary>
public interface ITurnosPendentesQuery
{
    /// <summary>
    /// Conversas automáticas cuja última mensagem que conta é do cliente e chegou entre <paramref name="desde"/> e
    /// <paramref name="ate"/>. O critério de "última mensagem que conta" é o mesmo do lembrete de cliente sem resposta.
    /// </summary>
    Task<IReadOnlyList<TurnoPendente>> ListarAsync(DateTime desde, DateTime ate, CancellationToken ct = default);
}
