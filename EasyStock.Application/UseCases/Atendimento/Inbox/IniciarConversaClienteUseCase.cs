using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using ClienteEntity = EasyStock.Domain.Entities.Cliente;

namespace EasyStock.Application.UseCases.Atendimento.Inbox;

public sealed record IniciarConversaClienteCommand(
    Guid EmpresaId, Guid UsuarioId, Guid ClienteId, Guid TentativaId, string Nome, string Idioma, IReadOnlyList<string> Parametros);

public sealed record ConversaIniciadaResult(Guid ConversaId, MensagemAtendimentoResult Mensagem);

/// <summary>
/// #1408: a loja chama no WhatsApp um cliente cadastrado que nunca escreveu (ou cuja conversa foi encerrada). Sem
/// mensagem de entrada não há janela de 24 h, então só sai modelo aprovado.
///
/// <list type="number">
/// <item>Cliente da empresa, ativo, não bloqueado, com celular BR no cadastro: o contato é o E.164 sem o <c>+</c>.</item>
/// <item>Antes de abrir conversa, tudo o que o envio recusaria: entrada, modelo aprovado e disponível, quantidade de
///   parâmetros, consentimento da finalidade e conexão do WhatsApp (<see cref="CatalogoModelosException"/>, 409 no
///   controller). Recusa aqui não deixa conversa vazia na inbox.</item>
/// <item>A conversa aberta do contato (as duas grafias do nono dígito, #1290) é reaproveitada; sem cliente, ganha o
///   vínculo; de outro cliente, é recusada. Sem conversa aberta, abre uma, sem automática de primeira entrada.</item>
/// <item>O envio é o do <see cref="EnviarModeloConversaUseCase"/>: tentativa idempotente, intenção gravada antes do
///   canal. A resposta do cliente cai nesta conversa pelo lookup do webhook (#1332).</item>
/// </list>
/// </summary>
public sealed class IniciarConversaClienteUseCase(
    IConversaRepository conversas, IClienteRepository clientes, ICatalogoModelosAtendimento catalogo, PoliticaEnvioCliente politica,
    EnviarModeloConversaUseCase envio, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public async Task<ConversaIniciadaResult> ExecuteAsync(IniciarConversaClienteCommand command, CancellationToken ct = default)
    {
        if (command.EmpresaId == Guid.Empty || command.UsuarioId == Guid.Empty || command.ClienteId == Guid.Empty)
            throw new UseCaseValidationException("Informe o cliente.");
        var modelo = EnviarModeloConversaUseCase.ModeloDaTentativa(command.TentativaId, command.Nome, command.Idioma, command.Parametros);

        var cliente = await clientes.GetByIdAsync(command.EmpresaId, command.ClienteId);
        if (cliente is null || cliente.EmpresaId != command.EmpresaId)
            throw new UseCaseValidationException("Cliente não encontrado nesta empresa.");
        if (!cliente.Ativo) throw new UseCaseValidationException("Cliente desativado: reative o cadastro para chamar no WhatsApp.");
        if (cliente.Bloqueado) throw new RegraDeDominioVioladaException("Cliente bloqueado: desbloqueie na Ficha para chamar no WhatsApp.");
        var contato = ContatoWhatsApp(cliente.Telefone);

        var aprovado = await EnviarModeloConversaUseCase.ModeloAprovadoAsync(catalogo, command.EmpresaId, modelo, ct);
        if (!await politica.PodeEnviarAsync(command.EmpresaId, cliente.Id, CanalConversa.WhatsApp,
                EnviarModeloConversaUseCase.FinalidadeDaCategoria(aprovado.Categoria), ct))
            throw new RegraDeDominioVioladaException("O cliente não autorizou esta finalidade neste canal. Confira os consentimentos na Ficha.");

        var conversaId = await ConversaDoClienteAsync(command.EmpresaId, cliente, contato, ct);
        var mensagem = await envio.ExecuteAsync(new EnviarModeloConversaCommand(
            command.EmpresaId, command.UsuarioId, conversaId, command.TentativaId, command.Nome, command.Idioma, command.Parametros), ct);
        return new ConversaIniciadaResult(conversaId, mensagem);
    }

    private static string ContatoWhatsApp(string? telefone)
    {
        const string semCelular = "O cliente não tem celular válido no cadastro. Corrija o telefone na Ficha.";
        string e164;
        try
        {
            e164 = NormalizadorTelefone.NormalizarE164Br(telefone ?? string.Empty);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new UseCaseValidationException(semCelular);
        }
        if (!NormalizadorTelefone.EhCelularBr(e164)) throw new UseCaseValidationException(semCelular);
        return Conversa.NormalizarContato(CanalConversa.WhatsApp, e164);
    }

    /// <summary>
    /// A conversa aberta do contato, travada, ou uma nova. Duas atendentes (ou o cliente escrevendo) no mesmo instante: o
    /// índice único de conversa aberta recusa a segunda gravação, e a segunda volta uma vez e segue na conversa que ficou.
    /// </summary>
    private async Task<Guid> ConversaDoClienteAsync(Guid empresaId, ClienteEntity cliente, string contato, CancellationToken ct)
    {
        try
        {
            return await AbrirOuReaproveitarAsync(empresaId, cliente, contato, ct);
        }
        catch (Exception ex) when (unitOfWork.EhViolacaoDeUnicidade(ex))
        {
            unitOfWork.DescartarAlteracoesPendentes();
            return await AbrirOuReaproveitarAsync(empresaId, cliente, contato, ct);
        }
    }

    private Task<Guid> AbrirOuReaproveitarAsync(Guid empresaId, ClienteEntity cliente, string contato, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionSemRetryAsync(async token =>
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
