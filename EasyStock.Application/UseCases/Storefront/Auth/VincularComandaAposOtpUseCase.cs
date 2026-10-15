using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Storefront.Auth;

public sealed class VinculoComandaInvalidoException() : Exception(
    "Não foi possível confirmar este telefone para a comanda. Reabra o link recebido da Casa.");

/// <summary>O sessionId vem exclusivamente do OTP validado, nunca do corpo HTTP.</summary>
public sealed class VincularComandaAposOtpUseCase(
    IStorefrontRepository lojas, IClienteSessionRepository sessoes,
    ILinkCardapioConversaRepository links, IConversaRepository conversas,
    IClienteRepository clientes, IUnitOfWork uow, TimeProvider relogio)
{
    public async Task ExecuteAsync(string slug, Guid sessionId, string tokenComanda, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tokenComanda) || tokenComanda.Length > 512)
            throw new VinculoComandaInvalidoException();
        var loja = await lojas.GetBySlugAsync(slug, ct);
        var sessao = await sessoes.GetByIdAsync(sessionId, ct);
        if (loja is null || !loja.Ativo || sessao is null || !sessao.EstaValida(relogio) || sessao.EmpresaId != loja.EmpresaId)
            throw new VinculoComandaInvalidoException();
        var candidato = await links.ObterPorTokenHashAsync(AcessoChatSite.HashDoToken(tokenComanda.Trim()), ct);
        if (candidato is null || candidato.EmpresaId != loja.EmpresaId)
            throw new VinculoComandaInvalidoException();

        await uow.ExecuteInTransactionSemRetryAsync(async t =>
        {
            // Mesma ordem de locks usada no envio: conversa, depois link.
            await conversas.TravarParaPedidoAsync(loja.EmpresaId, candidato.ConversaId, t);
            var link = await links.ObterComLockAsync(loja.EmpresaId, candidato.Id, t);
            var conversa = await conversas.ObterPorIdComLockAsync(loja.EmpresaId, candidato.ConversaId, t);
            var cliente = await clientes.GetByIdWithDetailsAsync(loja.EmpresaId, sessao.ClienteId);
            var agora = relogio.GetUtcNow().UtcDateTime;
            if (!sessao.EstaValida(relogio) || link is null || link.EmpresaId != loja.EmpresaId || link.ConversaId != candidato.ConversaId
                || link.UsadoEm is not null || link.PedidoRegistradoId is not null || link.ExpiraEm <= agora
                || conversa is null || conversa.EmpresaId != loja.EmpresaId || !conversa.EstaAberta
                || conversa.Canal != CanalConversa.ChatSite
                || conversa.ClienteId is { } dono && dono != sessao.ClienteId
                || cliente is null || cliente.EmpresaId != loja.EmpresaId || cliente.Bloqueado)
                throw new VinculoComandaInvalidoException();
            var rascunho = ComandaClienteUseCase.Ler<RascunhoComandaCliente>(link.RascunhoJson);
            string telefone;
            try { telefone = NormalizadorTelefone.NormalizarE164Br(rascunho?.Telefone ?? ""); }
            catch (EasyStock.Domain.Exceptions.Storefront.TelefoneInvalidoException) { throw new VinculoComandaInvalidoException(); }
            if (cliente.TelefoneHash != EasyStock.Domain.Entities.Storefront.ClienteOtp.CalcularTelefoneHash(telefone))
                throw new VinculoComandaInvalidoException();
            conversa.VincularCliente(cliente.Id);
            await uow.CommitAsync();
            return true;
        }, ct);
    }
}
