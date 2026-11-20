using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.ValueObjects;
using ClienteEntity = EasyStock.Domain.Entities.Cliente;

namespace EasyStock.Application.UseCases.Atendimento.Inbox;

public sealed record AbrirConversaDoClienteCommand(Guid EmpresaId, Guid UsuarioId, Guid ClienteId, string? Celular = null);

/// <summary>
/// #2871: "Novo pedido" na Ficha, em Pedidos e no Atendimento. O cliente pediu pelo WhatsApp pessoal da dona e a equipe
/// registra o pedido pelo Console: abre (ou reaproveita) a conversa do WhatsApp da loja com o cliente, sem mandar
/// mensagem. A comanda nasce nela, o pedido entra na esteira do Atendimento e o resumo é o primeiro contato.
///
/// <para>Devolve a conversa como linha da inbox: o Console a põe no Balcão antes da próxima leitura da lista, como faz
/// com a conversa achada na busca.</para>
/// </summary>
public sealed class AbrirConversaDoClienteUseCase(
    IClienteRepository clientes, ConversaWhatsAppDoCliente conversaDoCliente, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public async Task<ConversaResumoResult> ExecuteAsync(AbrirConversaDoClienteCommand command, CancellationToken ct = default)
    {
        if (command.EmpresaId == Guid.Empty || command.ClienteId == Guid.Empty)
            throw new UseCaseValidationException("Informe o cliente.");

        var cliente = await clientes.GetByIdAsync(command.EmpresaId, command.ClienteId);
        await GravarCelularSeFaltaAsync(cliente, command.Celular);
        var contato = ConversaWhatsAppDoCliente.ContatoDoCliente(cliente, command.EmpresaId);
        var conversa = await conversaDoCliente.AbrirOuReaproveitarAsync(
            command.EmpresaId, cliente!, contato, ct, assumidaAoCriarPor: command.UsuarioId);
        // O SLA da loja chega na próxima leitura da inbox; a conversa sem fala do cliente não está atrasada.
        return ConversaResumoResult.De(ConversaInboxItem.De(conversa, ultimaMensagemTexto: null),
            relogio.GetUtcNow().UtcDateTime, ConfiguracaoAtendimento.SlaRespostaPadraoMinutos);
    }

    /// <summary>
    /// #2930: cliente sem telefone no cadastro recebe o celular digitado no Novo pedido. Telefone já gravado nunca é trocado
    /// por aqui (isso é na Ficha); número que não é celular BR segue para a recusa de sempre, sem gravar nada.
    /// </summary>
    private async Task GravarCelularSeFaltaAsync(ClienteEntity? cliente, string? celular)
    {
        if (cliente is null || !string.IsNullOrWhiteSpace(cliente.Telefone) || string.IsNullOrWhiteSpace(celular)) return;
        if (Telefone.TryFrom(celular) is not { } telefone || !EhCelularBr(telefone.Value)) return;

        cliente.Telefone = telefone.Value;
        cliente.AlteradoEm = relogio.GetUtcNow().UtcDateTime;
        await clientes.UpdateAsync(cliente);
        await unitOfWork.CommitAsync();
    }

    private static bool EhCelularBr(string digitos)
    {
        try { return NormalizadorTelefone.EhCelularBr(NormalizadorTelefone.NormalizarE164Br(digitos)); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return false; }
    }
}
