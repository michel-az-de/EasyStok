using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Comanda;

public sealed record ChatDaComandaResult(string Slug, Guid ConversaId, Guid SessaoId, string Token, DateTime ExpiraEm);

public sealed class RetomarChatDaComandaUseCase(IConversaRepository conversas,
    IStorefrontRepository lojas, AcessoChatSite acesso, ISessaoChatSiteRepository sessoes, IUnitOfWork uow, TimeProvider relogio,
    ILinkCardapioConversaRepository links, ITenantContextAccessor tenant)
{
    public async Task<ChatDaComandaResult> ExecuteAsync(string token, string? chatToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 512) throw new LinkCardapioConversaIndisponivelException();
        var link = await links.ObterPorTokenHashAsync(AcessoChatSite.HashDoToken(token.Trim()), ct);
        if (link is null || link.ExpiraEm <= relogio.GetUtcNow().UtcDateTime) throw new LinkCardapioConversaIndisponivelException();
        tenant.SetCurrentTenant(link.EmpresaId);
        return await uow.ExecuteInTransactionSemRetryAsync<ChatDaComandaResult>(async t =>
        {
            var conversa = await conversas.ObterPorIdComLockAsync(link.EmpresaId, link.ConversaId, t);
            link = await links.ObterComLockAsync(link.EmpresaId, link.Id, t)
                ?? throw new LinkCardapioConversaIndisponivelException();
            if (conversa is null || conversa.EmpresaId != link.EmpresaId || conversa.Id != link.ConversaId
                || !conversa.EstaAberta || conversa.Canal != CanalConversa.ChatSite || conversa.VinculoEmRevisao
                || link.ExpiraEm <= relogio.GetUtcNow().UtcDateTime)
                throw new LinkCardapioConversaIndisponivelException();
            var loja = await lojas.GetByEmpresaAsync(link.EmpresaId, ct);
            if (loja is null || loja.EmpresaId != link.EmpresaId) throw new ChatSiteIndisponivelException();
            loja = await acesso.ResolverLojaAsync(loja.Slug, ct);
            if (loja.EmpresaId != link.EmpresaId) throw new ChatSiteIndisponivelException();
            var agora = relogio.GetUtcNow().UtcDateTime;
            if (!string.IsNullOrWhiteSpace(chatToken))
            {
                try
                {
                    var atual = await acesso.ResolverSessaoAsync(loja.Slug, chatToken, agora, ct);
                    if (atual.ConversaId == conversa.Id)
                        return new(loja.Slug, conversa.Id, atual.Id, chatToken, atual.ExpiraEm);
                }
                catch (SessaoChatSiteInvalidaException) { /* O link valido autoriza uma sessao vinculada, inclusive em outra aba. */ }
            }
            var novoToken = AcessoChatSite.NovoToken();
            var sessao = SessaoChatSite.Abrir(link.EmpresaId, loja.Id, AcessoChatSite.HashDoToken(novoToken), agora);
            sessao.VincularConversa(conversa.Id);
            await sessoes.AddAsync(sessao, ct);
            await uow.CommitAsync();
            return new(loja.Slug, conversa.Id, sessao.Id, novoToken, sessao.ExpiraEm);
        }, ct);
    }
}
