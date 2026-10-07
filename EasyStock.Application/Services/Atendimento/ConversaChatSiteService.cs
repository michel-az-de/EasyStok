using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

public sealed class ConversaChatSiteService(IConversaRepository conversas, IUnitOfWork unitOfWork)
{
    /// <summary>Nome do contato quando o visitante não preencheu o formulário (#1430).</summary>
    public const string NomeSemFormulario = "Visitante do site";

    public Task<Conversa> ObterOuCriarAsync(SessaoChatSite sessao, Guid? clienteVerificadoId, DateTime agora,
        CancellationToken ct = default) => unitOfWork.ExecuteInTransactionSemRetryAsync(async token =>
    {
        var contato = clienteVerificadoId is { } cliente ? $"cliente:{cliente:N}" : sessao.ContatoIdExterno;
        await conversas.TravarContatoAsync(sessao.EmpresaId, sessao.ContatoIdExterno, token);
        if (clienteVerificadoId.HasValue) await conversas.TravarContatoAsync(sessao.EmpresaId, contato, token);
        Conversa? conversa = null;
        if (clienteVerificadoId is { } id)
            conversa = await conversas.ObterAbertaPorClienteNoCanalAsync(sessao.EmpresaId, id, CanalConversa.ChatSite, token);
        if (conversa is null && sessao.ConversaId is { } conversaId)
            conversa = await conversas.ObterPorIdAsync(sessao.EmpresaId, conversaId, token);
        conversa ??= await conversas.ObterAbertaPorContatoAsync(sessao.EmpresaId, CanalConversa.ChatSite, sessao.ContatoIdExterno, token);
        if (conversa is not null && (conversa.EmpresaId != sessao.EmpresaId || !conversa.EstaAberta
            || conversa.Canal != CanalConversa.ChatSite || (clienteVerificadoId.HasValue && conversa.ClienteId.HasValue && conversa.ClienteId != clienteVerificadoId)))
            conversa = null;
        conversa ??= await conversas.ObterAbertaPorContatoAsync(sessao.EmpresaId, CanalConversa.ChatSite, contato, token);
        if (conversa is not null && (conversa.EmpresaId != sessao.EmpresaId || conversa.Canal != CanalConversa.ChatSite
            || (clienteVerificadoId.HasValue && conversa.ClienteId.HasValue && conversa.ClienteId != clienteVerificadoId)))
            throw new RegraDeDominioVioladaException("Conversa não pertence a esta sessão.");
        if (conversa is null)
        {
            conversa = Conversa.Abrir(sessao.EmpresaId, contato, agora, sessao.ContatoInformado?.Nome ?? NomeSemFormulario,
                clienteVerificadoId, CanalConversa.ChatSite);
            conversa.Assumir(agora);
            await conversas.AddAsync(conversa, token);
        }
        if (clienteVerificadoId is { } verificado) conversa.VincularCliente(verificado);
        // #1430: a conversa nasce (ou fica) com o que o visitante informou no formulário antes do chat.
        if (sessao.ContatoInformado is { } informado && conversa.ContatoInformadoEm is null)
            conversa.RegistrarContatoInformado(informado);
        sessao.VincularConversa(conversa.Id);
        sessao.RegistrarUso(agora);
        await unitOfWork.CommitAsync();
        return conversa;
    }, ct);
}
