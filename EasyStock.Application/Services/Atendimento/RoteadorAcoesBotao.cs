using EasyStock.Application.Services.Atendimento.AcoesBotao;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// Resolve botões com id <c>acao:&lt;nome&gt;:&lt;payload&gt;</c> sem chamar o LLM (S06): confirmações de
/// um toque e, depois, a avaliação (S26). O payload pode conter <c>:</c>; só o primeiro separa o nome.
/// Não faz commit: o chamador confirma.
/// </summary>
public sealed class RoteadorAcoesBotao(IEnumerable<IAcaoBotaoHandler> handlers, ILogger<RoteadorAcoesBotao> logger)
{
    public const string Prefixo = "acao:";

    private readonly Dictionary<string, IAcaoBotaoHandler> _handlers =
        handlers.ToDictionary(h => h.Nome, StringComparer.Ordinal);

    public static bool TryInterpretar(string? botaoId, out string nome, out string payload)
    {
        nome = payload = string.Empty;
        if (botaoId is null || !botaoId.StartsWith(Prefixo, StringComparison.Ordinal)) return false;

        var resto = botaoId[Prefixo.Length..];
        var separador = resto.IndexOf(':');
        nome = separador < 0 ? resto : resto[..separador];
        payload = separador < 0 ? string.Empty : resto[(separador + 1)..];
        return nome.Length > 0;
    }

    /// <summary>Verdadeiro quando havia handler para a ação e ele rodou.</summary>
    public async Task<bool> ExecutarAsync(Guid empresaId, Conversa conversa, string botaoId, DateTime agora, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conversa);

        if (!TryInterpretar(botaoId, out var nome, out var payload) || !_handlers.TryGetValue(nome, out var handler))
        {
            // O id vem do cliente: sem quebra de linha no log (log forging, mesmo cuidado da S03).
            var nomeLog = nome.Replace("\r", string.Empty).Replace("\n", string.Empty);
            logger.LogWarning("Roteador de botões: ação sem handler ({Nome}) na conversa {ConversaId}.", nomeLog, conversa.Id);
            return false;
        }

        await handler.ExecutarAsync(empresaId, conversa, payload, agora, ct);
        return true;
    }
}
