using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.ChatSite;

namespace EasyStock.Application.UseCases.Storefront.Auth;

public sealed class EncerrarSessaoUseCase(
    IStorefrontRepository storefrontRepository,
    IClienteSessionRepository sessionRepository,
    ISessaoChatSiteRepository chatRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task ExecuteAsync(string slug, Guid? sessionId, string? chatToken = null, CancellationToken ct = default)
    {
        if (sessionId is null && string.IsNullOrWhiteSpace(chatToken)) return;
        var storefront = await storefrontRepository.GetBySlugAsync(slug, ct);
        if (storefront is null) return;
        var mudou = false;
        if (sessionId is not null)
        {
            var session = await sessionRepository.GetByIdAsync(sessionId.Value, ct);
            if (session is not null && session.EmpresaId == storefront.EmpresaId && !session.Revogada)
            {
                session.Revogar("logout");
                await sessionRepository.UpdateAsync(session, ct);
                mudou = true;
            }
        }
        if (!string.IsNullOrWhiteSpace(chatToken))
        {
            var hash = AcessoChatSite.HashDoToken(chatToken.Trim());
            var chat = await chatRepository.ObterPorTokenHashAsync(storefront.EmpresaId, hash, ct);
            var agora = timeProvider.GetUtcNow().UtcDateTime;
            if (chat is not null && chat.EmpresaId == storefront.EmpresaId && chat.StorefrontId == storefront.Id && chat.TokenHash == hash)
            {
                chat.Encerrar(agora);
                mudou = true;
            }
        }
        if (mudou) await unitOfWork.CommitAsync();
    }
}
