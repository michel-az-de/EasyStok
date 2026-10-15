using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #1792: quem grava sobre a conversa (não lidas, situação) trava a linha e relê antes de mexer. Sem isso dois
/// processos que leram a mesma linha gravam, cada um, o valor que tinham em memória. Exige transação explícita.
/// </summary>
public static class ConversaTravada
{
    /// <summary>A conversa aberta do contato, travada. Encerrada enquanto esperava a trava: nenhuma.</summary>
    public static async Task<Conversa?> ObterAbertaPorContatoComLockAsync(
        this IConversaRepository conversas, Guid empresaId, CanalConversa canal, string contatoIdExterno, CancellationToken ct = default)
    {
        var aberta = await conversas.ObterAbertaPorContatoAsync(empresaId, canal, contatoIdExterno, ct);
        if (aberta is null) return null;
        var travada = await conversas.ObterPorIdComLockAsync(empresaId, aberta.Id, ct);
        return travada is { EstaAberta: true } ? travada : null;
    }
}
