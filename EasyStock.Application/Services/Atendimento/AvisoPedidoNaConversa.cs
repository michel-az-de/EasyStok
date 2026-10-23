using System.Security.Cryptography;
using System.Text;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #1991-1: aviso de pedido (S13) ao cliente do chat do site. O outbox de notificações só entrega por WhatsApp,
/// com telefone; no chat do site a mensagem gravada na conversa é o que o visitante recebe (<see cref="CanalChatSite"/>).
///
/// <para>
/// Mesmas regras do aviso pelo WhatsApp: só sai com a rotina do tipo ativa (desativar é o rollback) e com o texto
/// do mesmo modelo (o que a dona editou vale nos dois canais). Quem chama já conferiu bloqueio, avisos desligados e
/// consentimento. A chave <c>pedido|marco</c> vira o <c>ExternoId</c>: o evento reprocessado (at-least-once) não
/// repete a mensagem. Só estagia na unidade de trabalho, como o <c>EventoNotificacao</c>: quem chama commita.
/// </para>
/// </summary>
public sealed class AvisoPedidoNaConversa(
    IRotinaRepository rotinas,
    ITemplateRepository templates,
    IRendererTemplate renderer,
    IConversaRepository conversas,
    TimeProvider relogio,
    ILogger<AvisoPedidoNaConversa> logger)
{
    public const string PrefixoIdExterno = CanalChatSite.PrefixoIdExterno + "aviso:";

    /// <summary>A conversa recebe o aviso por aqui em vez do WhatsApp.</summary>
    public static bool Atende(Conversa conversa) => conversa.Canal == CanalConversa.ChatSite;

    /// <summary>Id externo do aviso: prefixo e o hash da chave (cabe no limite da coluna qualquer que seja o marco).</summary>
    public static string IdExterno(string chave) =>
        PrefixoIdExterno + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(chave)))[..32];

    /// <summary>Grava o aviso na conversa. Devolve se o aviso está na conversa (agora ou de antes).</summary>
    public async Task<bool> EntregarAsync(
        TipoEventoNotificacao tipo, Conversa conversa, IDictionary<string, object?> variaveis, string chave, CancellationToken ct)
    {
        var externoId = IdExterno(chave);
        if (await conversas.ObterMensagemPorExternoIdAsync(conversa.EmpresaId, externoId, ct) is not null) return true;

        var rotina = (await rotinas.ListarAtivasAsync(tipo, conversa.EmpresaId, ct))
            .Where(r => CanaisDaRotina.Ler(r).Contains(CanalNotificacao.WhatsApp))
            .OrderByDescending(r => r.EmpresaId.HasValue)
            .FirstOrDefault();
        if (rotina is null)
        {
            logger.LogInformation("Aviso {Tipo} no chat do site não saiu: rotina desativada", tipo);
            return false;
        }

        var modelo = await templates.GetAtivoAsync(rotina.TemplateCodigo, CanalNotificacao.WhatsApp, conversa.EmpresaId, ct)
            ?? await templates.GetAtivoAsync(rotina.TemplateCodigo, CanalNotificacao.WhatsApp, null, ct)
            ?? await templates.GetAtivoPorTipoAsync(tipo, CanalNotificacao.WhatsApp, conversa.EmpresaId, ct);
        var texto = modelo is null ? string.Empty : (await renderer.RenderizarAsync(modelo.CorpoTemplate, variaveis, ct)).Trim();
        if (texto.Length == 0)
        {
            logger.LogWarning("Aviso {Tipo} no chat do site não saiu: sem modelo de texto", tipo);
            return false;
        }

        var agora = relogio.GetUtcNow().UtcDateTime;
        // Como a automática: conversa encerrada recebe no histórico sem reabrir nada.
        if (conversa.EstaAberta) conversa.RegistrarSaida(agora);
        await conversas.AddMensagemAsync(Mensagem.Saida(conversa.EmpresaId, conversa.Id, AutorMensagem.Sistema, agora,
            TipoConteudoMensagem.Texto, texto, externoId), ct);
        logger.LogInformation("Aviso {Tipo} gravado no chat do site da conversa {ConversaId}", tipo, conversa.Id);
        return true;
    }
}
