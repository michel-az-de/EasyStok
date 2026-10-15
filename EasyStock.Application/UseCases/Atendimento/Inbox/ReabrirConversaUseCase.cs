using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Inbox;

public sealed class ReabrirConversaUseCase(
    IConversaRepository conversas, IClienteRepository clientes, IUnitOfWork unitOfWork, TimeProvider relogio,
    IPedidoRepository? pedidos = null)
{
    /// <summary>
    /// Início da nota que registra a reabertura. O avaliador de lembretes a reconhece por ele (#1550): a
    /// entrada anterior à reabertura não conta como cliente sem resposta.
    /// </summary>
    public const string PrefixoNotaReabertura = "Conversa reaberta por ";
    public const string ConflitoContato = "Este contato já tem outra conversa aberta neste canal. Continue por ela no Balcão.";

    public async Task<ConversaSituacaoResult> ExecuteAsync(AcaoConversaCommand command, CancellationToken ct = default)
    {
        if (command.EmpresaId == Guid.Empty || command.UsuarioId == Guid.Empty || command.ConversaId == Guid.Empty)
            throw new UseCaseValidationException("Empresa, usuário e conversa são obrigatórios.");
        try
        {
            return await unitOfWork.ExecuteInTransactionSemRetryAsync(async token =>
            {
                var conversa = Validar(await conversas.ObterPorIdAsync(command.EmpresaId, command.ConversaId, token), command);
                // Serializa reaberturas de históricos diferentes, incluindo as duas grafias do celular no WhatsApp.
                var contato = conversa.Canal == CanalConversa.WhatsApp
                    ? NormalizadorTelefone.VariantesContatoWhatsApp(conversa.ContatoIdExterno).Order(StringComparer.Ordinal).First()
                    : conversa.ContatoIdExterno;
                await conversas.TravarContatoAsync(command.EmpresaId, contato, token);
                if (conversa.Canal == CanalConversa.ChatSite && conversa.ClienteId is { } clienteSite)
                    await conversas.TravarContatoAsync(command.EmpresaId, $"cliente:{clienteSite:N}", token);
                conversa = Validar(await conversas.ObterPorIdComLockAsync(command.EmpresaId, command.ConversaId, token), command);

                // Uma repetição após perda da resposta não refaz a nota nem altera quem já assumiu.
                if (conversa.EstaAberta) return ConversaSituacaoResult.De(conversa);
                if (conversa.ClienteId is { } clienteId)
                {
                    var cliente = await clientes.GetByIdAsync(command.EmpresaId, clienteId);
                    if (cliente is null || cliente.EmpresaId != command.EmpresaId)
                        throw new UseCaseValidationException("Cliente da conversa não encontrado.");
                    if (cliente.Bloqueado)
                        throw new RegraDeDominioVioladaException("Cliente bloqueado: desbloqueie o cadastro antes de reabrir a conversa.");
                }
                var aberta = await conversas.ObterAbertaPorContatoAsync(command.EmpresaId, conversa.Canal, conversa.ContatoIdExterno, token);
                if (aberta is null && conversa.Canal == CanalConversa.ChatSite && conversa.ClienteId is { } clienteVerificado)
                    aberta = await conversas.ObterAbertaPorClienteNoCanalAsync(command.EmpresaId, clienteVerificado, conversa.Canal, token);
                if (aberta is not null && aberta.Id != conversa.Id)
                    throw new RegraDeDominioVioladaException(ConflitoContato);

                var agora = relogio.GetUtcNow().UtcDateTime;
                var encerradaEm = conversa.EncerradaEm;
                conversa.Reabrir(agora);
                // #1839: atendimento encerrado vira história. Pedido já entregue ou cancelado sai do Resumo e o
                // novo atendimento começa sem pedido; o antigo segue em Cliente e histórico. Em andamento fica.
                if (pedidos is not null && conversa.PedidoEmAndamentoId is { } pedidoId
                    && await pedidos.GetByIdAsync(command.EmpresaId, pedidoId) is { } pedido
                    && pedido.EmpresaId == command.EmpresaId && pedido.EstaFinalizado)
                    conversa.DefinirPedidoEmAndamento(null);
                var nota = Mensagem.Saida(command.EmpresaId, conversa.Id, AutorMensagem.Sistema, agora,
                    TipoConteudoMensagem.Texto, $"{PrefixoNotaReabertura}{command.QuemAgiu}. Encerramento anterior: {encerradaEm:dd/MM/yyyy HH:mm:ss} UTC.");
                nota.RegistrarAutorNotaInterna(command.UsuarioId);
                await conversas.AddMensagemAsync(nota, token);
                await unitOfWork.CommitAsync();
                return ConversaSituacaoResult.De(conversa);
            }, ct);
        }
        catch (Exception ex) when (unitOfWork.EhViolacaoDeUnicidade(ex))
        {
            // Uma entrada do canal pode abrir outra conversa depois da consulta. O índice é a última defesa.
            unitOfWork.DescartarAlteracoesPendentes();
            throw new RegraDeDominioVioladaException(ConflitoContato);
        }
    }

    private static Conversa Validar(Conversa? conversa, AcaoConversaCommand command) =>
        conversa is not null && conversa.EmpresaId == command.EmpresaId && conversa.Id == command.ConversaId
            ? conversa : throw new ConversaNaoEncontradaException(command.ConversaId);
}
