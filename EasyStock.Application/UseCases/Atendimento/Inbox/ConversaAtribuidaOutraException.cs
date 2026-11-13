using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Inbox;

/// <summary>ATD-1 (#2305): a conversa é de outra atendente; assumir, responder ou transferir pedem confirmação (409).</summary>
public sealed class ConversaAtribuidaOutraException(string? nomeDaOutra) : Exception(
    $"Conversa com {(string.IsNullOrWhiteSpace(nomeDaOutra) ? "outra atendente" : nomeDaOutra.Trim())}. Confirme para assumir.")
{
    public const string Codigo = "CONVERSA_DE_OUTRA";
}

/// <summary>Recusa a tomada de conversa de outra atendente sem confirmação explícita. Livre ou própria passa direto.</summary>
internal static class GuardaAtribuicaoConversa
{
    public static async Task GarantirAsync(IAtendenteRepository? atendentes, Conversa conversa, Guid usuarioId, bool confirmada, CancellationToken ct)
    {
        if (confirmada || conversa.AssumidaPorUsuarioId is not { } dona || dona == usuarioId) return;
        var nome = atendentes is null ? null : (await atendentes.ObterUsuarioAtivoAsync(conversa.EmpresaId, dona, ct))?.Nome;
        throw new ConversaAtribuidaOutraException(nome);
    }
}
