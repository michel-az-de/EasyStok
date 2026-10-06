using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.ChatSite;

namespace EasyStock.Application.UseCases.Storefront.Auth;

public sealed class VincularChatAposLoginUseCase(
    AcessoChatSite acesso,
    IClienteSessionRepository sessoes,
    ConversaChatSiteService conversas,
    TimeProvider timeProvider)
{
    public async Task ExecuteAsync(string slug, Guid sessionId, string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        var agora = timeProvider.GetUtcNow().UtcDateTime;
        var sessaoCliente = await sessoes.GetByIdAsync(sessionId, ct);
        if (sessaoCliente is null || !sessaoCliente.EstaValida(timeProvider)) return;
        var chat = await acesso.ResolverSessaoAsync(slug, token, agora, ct);
        if (chat.EmpresaId != sessaoCliente.EmpresaId) throw new SessaoChatSiteInvalidaException();
        if (chat.ConversaId is not null)
            await conversas.ObterOuCriarAsync(chat, sessaoCliente.ClienteId, agora, ct);
    }
}
