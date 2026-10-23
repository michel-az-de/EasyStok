using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Inbox;

public sealed record IniciarConversaClienteCommand(
    Guid EmpresaId, Guid UsuarioId, Guid ClienteId, Guid TentativaId, string Nome, string Idioma, IReadOnlyList<string> Parametros);

public sealed record ConversaIniciadaResult(Guid ConversaId, MensagemAtendimentoResult Mensagem);

/// <summary>
/// #1408: a loja chama no WhatsApp um cliente cadastrado que nunca escreveu (ou cuja conversa foi encerrada). Sem
/// mensagem de entrada não há janela de 24 h, então só sai modelo aprovado.
///
/// <list type="number">
/// <item>Cliente da empresa, não bloqueado, com telefone BR válido: o contato é o E.164 sem o <c>+</c>.</item>
/// <item>O modelo é conferido no catálogo antes de abrir conversa: modelo inválido ou WhatsApp desconectado
///   (<see cref="CatalogoModelosException"/>, 409 no controller) não deixa conversa vazia na inbox.</item>
/// <item>A conversa aberta do contato (as duas grafias do nono dígito, #1290) é reaproveitada; sem cliente, ganha o
///   vínculo; de outro cliente, é recusada. Sem conversa aberta, abre uma, sem automática de primeira entrada.</item>
/// <item>O envio é o do <see cref="EnviarModeloConversaUseCase"/>: tentativa idempotente, consentimento, intenção
///   gravada antes do canal. A resposta do cliente cai nesta conversa pelo lookup do webhook (#1332).</item>
/// </list>
/// </summary>
public sealed class IniciarConversaClienteUseCase(
    IConversaRepository conversas, IClienteRepository clientes, ICatalogoModelosAtendimento catalogo,
    EnviarModeloConversaUseCase envio, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public async Task<ConversaIniciadaResult> ExecuteAsync(IniciarConversaClienteCommand command, CancellationToken ct = default)
    {
        if (command.EmpresaId == Guid.Empty || command.UsuarioId == Guid.Empty || command.ClienteId == Guid.Empty)
            throw new UseCaseValidationException("Informe o cliente.");

        var cliente = await clientes.GetByIdAsync(command.EmpresaId, command.ClienteId);
        if (cliente is null || cliente.EmpresaId != command.EmpresaId)
            throw new UseCaseValidationException("Cliente não encontrado nesta empresa.");
        if (cliente.Bloqueado) throw new RegraDeDominioVioladaException("Cliente bloqueado: desbloqueie na Ficha para chamar no WhatsApp.");
        var contato = ContatoWhatsApp(cliente.Telefone);

        // Antes de abrir qualquer conversa: modelo do catálogo aprovado e conexão de pé. O envio confere de novo.
        var aprovado = (await catalogo.ListarAsync(command.EmpresaId, ct))
            .FirstOrDefault(m => m.Nome == command.Nome?.Trim() && m.Idioma == command.Idioma?.Trim())
            ?? throw new UseCaseValidationException("Modelo não está aprovado nesta conta e idioma. Atualize a lista.");
        if (aprovado.Indisponivel is not null) throw new UseCaseValidationException(aprovado.Indisponivel);

        var conversaId = await ConversaDoClienteAsync(command.EmpresaId, cliente, contato, ct);
        var mensagem = await envio.ExecuteAsync(new EnviarModeloConversaCommand(
            command.EmpresaId, command.UsuarioId, conversaId, command.TentativaId, command.Nome, command.Idioma, command.Parametros), ct);
        return new ConversaIniciadaResult(conversaId, mensagem);
    }

    private static string ContatoWhatsApp(string? telefone)
    {
        const string semTelefone = "O cliente não tem telefone celular válido no cadastro. Corrija o telefone na Ficha.";
        if (string.IsNullOrWhiteSpace(telefone)) throw new UseCaseValidationException(semTelefone);
        try
        {
            return Conversa.NormalizarContato(CanalConversa.WhatsApp, NormalizadorTelefone.NormalizarE164Br(telefone));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new UseCaseValidationException(semTelefone);
        }
    }

    /// <summary>A conversa aberta do contato, travada, ou uma nova. Duas atendentes ao mesmo tempo: o índice único decide.</summary>
    private async Task<Guid> ConversaDoClienteAsync(Guid empresaId, Domain.Entities.Cliente cliente, string contato, CancellationToken ct)
    {
        try
        {
            return await unitOfWork.ExecuteInTransactionSemRetryAsync(async token =>
            {
                var agora = relogio.GetUtcNow().UtcDateTime;
                var conversa = await conversas.ObterAbertaPorContatoComLockAsync(empresaId, CanalConversa.WhatsApp, contato, token);
                if (conversa is null)
                {
                    conversa = Conversa.Abrir(empresaId, contato, agora, cliente.Nome, cliente.Id);
                    await conversas.AddAsync(conversa, token);
                }
                else if (conversa.ClienteId is null)
                {
                    conversa.VincularCliente(cliente.Id);
                }
                else if (conversa.ClienteId != cliente.Id)
                {
                    throw new RegraDeDominioVioladaException(
                        "Este telefone está numa conversa aberta de outro cliente. Confira o cadastro na Ficha.");
                }

                await unitOfWork.CommitAsync();
                return conversa.Id;
            }, ct);
        }
        catch (Exception ex) when (unitOfWork.EhViolacaoDeUnicidade(ex))
        {
            // A outra atendente abriu a conversa do mesmo contato no mesmo instante: segue nela.
            unitOfWork.DescartarAlteracoesPendentes();
            var aberta = await conversas.ObterAbertaPorContatoAsync(empresaId, CanalConversa.WhatsApp, contato, ct);
            if (aberta is null || aberta.ClienteId != cliente.Id) throw;
            return aberta.Id;
        }
    }
}
