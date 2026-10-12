using System.Security.Cryptography;
using System.Text;
using EasyStock.Application.Ports.Output.Notifications;

namespace EasyStock.Application.UseCases.Notifications.Plataforma;

/// <summary>
/// Quem responde ao número de plataforma (N6) recebe uma resposta automática por remetente a cada 24 h, em texto livre
/// dentro da janela de serviço. Não grava <c>Conversa</c>, <c>Mensagem</c> nem <c>webhook_recebido</c>, e não responde
/// <c>reaction</c>, <c>system</c> nem <c>unsupported</c>. O controle é um contador no cache com o hash do número.
/// </summary>
public sealed class ResponderMensagemRecebidaPlataformaUseCase(
    IClienteWhatsAppPlataforma cliente,
    ICacheService cache,
    ILogger<ResponderMensagemRecebidaPlataformaUseCase> logger)
{
    public const string TextoResposta =
        "Este número só envia avisos do EasyStok e não é monitorado. " +
        "Para ajustar os avisos que você recebe, acesse Preferências no EasyStok.";

    private static readonly TimeSpan Janela = TimeSpan.FromHours(24);

    private static readonly HashSet<string> TiposSemResposta = new(StringComparer.Ordinal) { "reaction", "system", "unsupported" };

    public async Task ExecuteAsync(MensagemRecebidaPlataforma mensagem, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(mensagem.De) || TiposSemResposta.Contains(mensagem.Tipo)) return;

        // Hash do número: o telefone nunca vira chave de cache nem entra no log.
        var chave = "plataforma:autoresposta:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(mensagem.De)));
        if (await cache.IncrementAsync(chave) != 1) return;
        await cache.SetExpiryAsync(chave, Janela);

        var r = await cliente.EnviarTextoPlataformaAsync(mensagem.De, TextoResposta, ct);
        if (r.Desfecho != DesfechoEnvio.Enviado)
            logger.LogWarning("Resposta automática da plataforma não saiu: {Desfecho} (código {Codigo}).", r.Desfecho, r.CodigoMeta);
    }
}
