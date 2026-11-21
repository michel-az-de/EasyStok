using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Inbox;

public sealed record IniciarConversaClienteCommand(
    Guid EmpresaId, Guid UsuarioId, Guid ClienteId, Guid TentativaId, string Nome, string Idioma, IReadOnlyList<string> Parametros,
    bool Confirmada = false);

public sealed record ConversaIniciadaResult(Guid ConversaId, MensagemAtendimentoResult Mensagem);

/// <summary>
/// #1408: a loja chama no WhatsApp um cliente cadastrado que nunca escreveu (ou cuja conversa foi encerrada). Sem
/// mensagem de entrada não há janela de 24 h, então só sai modelo aprovado.
///
/// <list type="number">
/// <item>Cliente apto e contato como em <see cref="ConversaWhatsAppDoCliente"/>.</item>
/// <item>Antes de abrir conversa, tudo o que o envio recusaria: entrada, modelo aprovado e disponível, quantidade de
///   parâmetros, consentimento da finalidade e conexão do WhatsApp (<see cref="CatalogoModelosException"/>, 409 no
///   controller). Recusa aqui não deixa conversa vazia na inbox.</item>
/// <item>A conversa aberta do contato é reaproveitada ou uma nova é aberta (<see cref="ConversaWhatsAppDoCliente"/>).</item>
/// <item>O envio é o do <see cref="EnviarModeloConversaUseCase"/>: tentativa idempotente, intenção gravada antes do
///   canal. A resposta do cliente cai nesta conversa pelo lookup do webhook (#1332).</item>
/// </list>
/// </summary>
public sealed class IniciarConversaClienteUseCase(
    IClienteRepository clientes, ICatalogoModelosAtendimento catalogo, PoliticaEnvioCliente politica,
    EnviarModeloConversaUseCase envio, ConversaWhatsAppDoCliente conversaDoCliente)
{
    public async Task<ConversaIniciadaResult> ExecuteAsync(IniciarConversaClienteCommand command, CancellationToken ct = default)
    {
        if (command.EmpresaId == Guid.Empty || command.UsuarioId == Guid.Empty || command.ClienteId == Guid.Empty)
            throw new UseCaseValidationException("Informe o cliente.");
        var modelo = EnviarModeloConversaUseCase.ModeloDaTentativa(command.TentativaId, command.Nome, command.Idioma, command.Parametros);

        var cliente = await clientes.GetByIdAsync(command.EmpresaId, command.ClienteId);
        var contato = ConversaWhatsAppDoCliente.ContatoDoCliente(cliente, command.EmpresaId);

        var aprovado = await EnviarModeloConversaUseCase.ModeloAprovadoAsync(catalogo, command.EmpresaId, modelo, ct);
        if (!await politica.PodeEnviarAsync(command.EmpresaId, cliente!.Id, CanalConversa.WhatsApp,
                EnviarModeloConversaUseCase.FinalidadeDaCategoria(aprovado.Categoria), ct))
            throw new RegraDeDominioVioladaException("O cliente não autorizou esta finalidade neste canal. Confira os consentimentos na Ficha.");

        var conversaId = (await conversaDoCliente.AbrirOuReaproveitarAsync(command.EmpresaId, cliente, contato, ct)).Id;
        var mensagem = await envio.ExecuteAsync(new EnviarModeloConversaCommand(
            command.EmpresaId, command.UsuarioId, conversaId, command.TentativaId, command.Nome, command.Idioma, command.Parametros,
            // #2440: a conversa aberta reaproveitada pode ser de outra atendente; pede a mesma confirmação do texto.
            command.Confirmada), ct);
        return new ConversaIniciadaResult(conversaId, mensagem);
    }
}
