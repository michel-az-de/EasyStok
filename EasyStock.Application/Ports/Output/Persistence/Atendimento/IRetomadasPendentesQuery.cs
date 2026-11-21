namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

/// <summary>
/// #3068: acha, em todos os tenants, a conversa que está com a equipe e em que o cliente espera sem resposta humana.
/// Roda com o bypass de RLS ligado pelo host. A consulta só indica candidatas: quem decide a retomada é o turno do
/// agente, com a regra de <c>RetomadaDoAssistente</c>.
/// </summary>
public interface IRetomadasPendentesQuery
{
    /// <summary>
    /// Conversas assumidas, em canal com agente e com o Assistente ligado na loja, cuja última mensagem do cliente
    /// chegou entre <paramref name="desde"/> e <paramref name="ate"/>, ficou sem resposta da equipe e ainda não foi
    /// retomada. Cliente bloqueado, contato encaminhado à responsável e vínculo em revisão ficam de fora.
    /// </summary>
    Task<IReadOnlyList<TurnoPendente>> ListarAsync(DateTime desde, DateTime ate, CancellationToken ct = default);
}
