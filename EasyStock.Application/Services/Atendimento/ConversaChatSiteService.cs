using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

public sealed class ConversaChatSiteService(IConversaRepository conversas, IUnitOfWork unitOfWork,
    ISessaoChatSiteRepository sessoes, IPedidoRepository? pedidos = null)
{
    /// <summary>Nome do contato quando o visitante não preencheu o formulário (#1430).</summary>
    public const string NomeSemFormulario = "Visitante do site";

    public const string MotivoFilaHumana = "Visitante do site aguardando atendimento da equipe.";
    public Task<Conversa> ObterOuCriarAsync(SessaoChatSite sessao, Guid? clienteVerificadoId, DateTime agora,
        CancellationToken ct = default) => unitOfWork.ExecuteInTransactionSemRetryAsync(async token =>
    {
        var contato = clienteVerificadoId is { } cliente ? $"cliente:{cliente:N}" : sessao.ContatoIdExterno;
        await conversas.TravarContatoAsync(sessao.EmpresaId, sessao.ContatoIdExterno, token);
        if (clienteVerificadoId.HasValue) await conversas.TravarContatoAsync(sessao.EmpresaId, contato, token);
        var sessaoAtual = await GarantirSessaoAsync(sessao, agora, token);
        Conversa? conversa = null;
        if (clienteVerificadoId is { } id)
            conversa = await conversas.ObterAbertaPorClienteNoCanalAsync(sessao.EmpresaId, id, CanalConversa.ChatSite, token);
        if (conversa is null && sessaoAtual.ConversaId is { } conversaId)
            conversa = await conversas.ObterPorIdAsync(sessao.EmpresaId, conversaId, token);
        conversa ??= await conversas.ObterAbertaPorContatoAsync(sessao.EmpresaId, CanalConversa.ChatSite, sessao.ContatoIdExterno, token);
        if (conversa is not null && (conversa.EmpresaId != sessao.EmpresaId || !conversa.EstaAberta
            || conversa.Canal != CanalConversa.ChatSite || (clienteVerificadoId.HasValue && conversa.ClienteId.HasValue && conversa.ClienteId != clienteVerificadoId)))
            conversa = null;
        conversa ??= await conversas.ObterAbertaPorContatoAsync(sessao.EmpresaId, CanalConversa.ChatSite, contato, token);
        if (conversa is null && clienteVerificadoId is null && sessaoAtual.ConversaId is { } anterior)
            conversa = await ReabrirSePedidoEmAndamentoAsync(sessao.EmpresaId, anterior, agora, token);
        if (conversa is not null && (conversa.EmpresaId != sessao.EmpresaId || conversa.Canal != CanalConversa.ChatSite
            || (clienteVerificadoId.HasValue && conversa.ClienteId.HasValue && conversa.ClienteId != clienteVerificadoId)))
            throw new RegraDeDominioVioladaException("Conversa não pertence a esta sessão.");
        if (conversa is null)
        {
            // Mesmo encerrada, a conversa da credencial serializa a revogação com esta criação.
            if (sessaoAtual.ConversaId is { } original)
                _ = await conversas.ObterPorIdComLockAsync(sessao.EmpresaId, original, token)
                    ?? throw new RegraDeDominioVioladaException("Conversa não encontrada.");
            await GarantirSessaoAsync(sessao, agora, token);
            conversa = Conversa.Abrir(sessao.EmpresaId, contato, agora, sessao.ContatoInformado?.Nome ?? NomeSemFormulario,
                clienteVerificadoId, CanalConversa.ChatSite);
            conversa.Escalar(agora, MotivoFilaHumana);
            await conversas.AddAsync(conversa, token);
        }
        else
        {
            conversa = await conversas.ObterPorIdComLockAsync(sessao.EmpresaId, conversa.Id, token)
                ?? throw new RegraDeDominioVioladaException("Conversa não encontrada.");
            await GarantirSessaoAsync(sessao, agora, token);
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

    public async Task TravarIdentidadePedidoAsync(SessaoChatSite sessao, Guid conversaId, Guid? clienteId,
        long revisao, DateTime agora, CancellationToken ct)
    {
        var conversa = await conversas.ObterPorIdComLockAsync(sessao.EmpresaId, conversaId, ct);
        await GarantirSessaoAsync(sessao, agora, ct);
        if (conversa is null || conversa.EmpresaId != sessao.EmpresaId || !conversa.EstaAberta
            || conversa.VinculoEmRevisao || conversa.ClienteId != clienteId || conversa.VinculoRevisao != revisao)
            throw new RegraDeDominioVioladaException("A identidade mudou. Reabra o atendimento antes de enviar o pedido.");
    }

    internal async Task<SessaoChatSite> GarantirSessaoAsync(SessaoChatSite sessao, DateTime agora, CancellationToken ct)
    {
        var atual = await sessoes.ObterSnapshotPorTokenHashAsync(sessao.EmpresaId, sessao.TokenHash, ct);
        if (atual is null || atual.Id != sessao.Id || atual.EmpresaId != sessao.EmpresaId || !atual.EstaValida(agora))
            throw new RegraDeDominioVioladaException("A sessão mudou. Reabra o atendimento para continuar.");
        return atual;
    }

    /// <summary>
    /// #1918: a conversa da sessão foi encerrada com o pedido ainda em andamento. Ela volta à fila humana com o pedido
    /// (como no WhatsApp, #1765-5) em vez de nascer outra sem ele. Relida sob trava: o console pode tê-la reaberto.
    /// </summary>
    private async Task<Conversa?> ReabrirSePedidoEmAndamentoAsync(Guid empresaId, Guid conversaId, DateTime agora, CancellationToken ct)
    {
        if (pedidos is null) return null;
        var encerrada = await conversas.ObterPorIdComLockAsync(empresaId, conversaId, ct);
        if (encerrada is null || encerrada.EmpresaId != empresaId || encerrada.Canal != CanalConversa.ChatSite
            || encerrada.PedidoEmAndamentoId is not { } pedidoId)
            return null;
        var pedido = await pedidos.GetByIdAsync(empresaId, pedidoId);
        if (pedido is null || pedido.EmpresaId != empresaId || pedido.EstaFinalizado) return null;
        encerrada.Reabrir(agora);
        return encerrada;
    }

    /// <summary>
    /// Pedido feito pela página do chat: fica em andamento na conversa, conta como entrada do cliente e deixa a nota
    /// interna <paramref name="resumo"/>. Repetição do mesmo pedido (checkpoint refeito) não grava de novo.
    /// #1797: a conversa é relida sob a trava. A leitura do início do checkout é velha: gravá-la desfazia o marcar-lida.
    /// Encerrada no meio, o pedido fica nela (já foi criado) sem contar como entrada.
    /// </summary>
    public Task RegistrarPedidoAsync(Guid empresaId, Guid conversaId, Guid pedidoId, string resumo, DateTime agora,
        CancellationToken ct = default) => unitOfWork.ExecuteInTransactionSemRetryAsync(async token =>
    {
        if (await conversas.TravarParaPedidoAsync(empresaId, conversaId, token) == pedidoId)
        {
            await unitOfWork.CommitAsync();
            return false;
        }
        var conversa = await conversas.ObterPorIdComLockAsync(empresaId, conversaId, token)
            ?? throw new InvalidOperationException("Conversa do pedido não encontrada.");
        conversa.DefinirPedidoEmAndamento(pedidoId);
        if (conversa.EstaAberta) conversa.RegistrarEntrada(agora);
        await conversas.AddMensagemAsync(Mensagem.Saida(empresaId, conversa.Id, AutorMensagem.Sistema, agora,
            TipoConteudoMensagem.Texto, resumo), token);
        await unitOfWork.CommitAsync();
        return true;
    }, ct);
}
