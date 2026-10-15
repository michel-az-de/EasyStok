using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using ClienteEntity = EasyStock.Domain.Entities.Cliente;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Programadas;

public sealed record MensagemProgramadaResult(
    Guid Id, Guid ClienteId, Guid? ConversaId, CanalConversa Canal, FinalidadeContato Finalidade,
    string? Texto, ModeloMensagem? Modelo, DateTime AgendadaPara, SituacaoMensagemProgramada Situacao,
    int Tentativas, string? Erro, DateTime? EnviadaEm)
{
    internal static MensagemProgramadaResult De(MensagemProgramada m) => new(
        m.Id, m.ClienteId, m.ConversaId, m.Canal, m.Finalidade, m.Texto, m.Modelo, m.AgendadaPara,
        m.Situacao, m.Tentativas, m.Erro, m.EnviadaEm);
}

public sealed class MensagemProgramadaNaoEncontradaException(Guid id)
    : Exception($"Mensagem programada {id} não encontrada.");

public sealed record AgendarMensagemProgramadaCommand(
    Guid EmpresaId, Guid UsuarioId, Guid ClienteId, Guid? ConversaId, CanalConversa Canal,
    FinalidadeContato Finalidade, string? Texto, ModeloMensagem? Modelo, DateTime AgendadaPara);

/// <summary>
/// S39: agenda a mensagem já conferindo tudo o que dá para conferir agora — destino, janela do
/// canal no horário do envio e consentimento (S38). O disparo confere de novo.
/// </summary>
public sealed class AgendarMensagemProgramadaUseCase(
    IClienteRepository clientes,
    IConversaRepository conversas,
    IMensagemProgramadaRepository repository,
    PoliticaEnvioCliente politica,
    IUnitOfWork unitOfWork,
    TimeProvider relogio)
{
    public async Task<MensagemProgramadaResult> ExecuteAsync(AgendarMensagemProgramadaCommand command, CancellationToken ct = default)
    {
        var cliente = await clientes.GetByIdAsync(command.EmpresaId, command.ClienteId)
            ?? throw new UseCaseValidationException($"Cliente {command.ClienteId} não encontrado nesta empresa.");
        if (cliente.Bloqueado)
            throw new UseCaseValidationException("Cliente bloqueado: desbloqueie o cadastro antes de programar mensagens.");

        MensagemProgramada mensagem;
        try
        {
            var conversa = await DestinoMensagemProgramada.ConversaAsync(
                conversas, command.EmpresaId, cliente, command.Canal, command.ConversaId, ct);
            _ = DestinoMensagemProgramada.Contato(cliente, command.Canal, conversa);

            mensagem = MensagemProgramada.Agendar(command.EmpresaId, command.ClienteId, conversa?.Id, command.Canal,
                command.Finalidade, command.Texto, command.Modelo, command.AgendadaPara, command.UsuarioId,
                relogio.GetUtcNow().UtcDateTime);
            mensagem.GarantirPodeSairPor(conversa, mensagem.AgendadaPara);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }

        if (!await politica.PodeEnviarAsync(command.EmpresaId, command.ClienteId, command.Canal, command.Finalidade, ct))
            throw new UseCaseValidationException(
                $"O cliente não autorizou {DescreverFinalidade(command.Finalidade)} pelo canal {command.Canal}.");

        await repository.AddAsync(mensagem, ct);
        await unitOfWork.CommitAsync();
        return MensagemProgramadaResult.De(mensagem);
    }

    internal static string DescreverFinalidade(FinalidadeContato f) =>
        f == FinalidadeContato.Marketing ? "novidades (marketing)" : "avisos (transacional)";
}

public sealed class ListarMensagensProgramadasUseCase(IMensagemProgramadaRepository repository)
{
    public const int LimitePadrao = 100;

    public async Task<IReadOnlyList<MensagemProgramadaResult>> ExecuteAsync(
        Guid empresaId, Guid? clienteId, SituacaoMensagemProgramada? situacao, CancellationToken ct = default) =>
        (await repository.ListarAsync(empresaId, clienteId, situacao, LimitePadrao, ct)).Select(MensagemProgramadaResult.De).ToList();
}

/// <summary>S39: cancela uma agendada (<c>CANCELAR_PROGRAMADO</c> do protótipo). A que já está saindo não cancela.</summary>
public sealed class CancelarMensagemProgramadaUseCase(
    IMensagemProgramadaRepository repository, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public Task<MensagemProgramadaResult> ExecuteAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // A reserva do disparador e o cancelamento precisam disputar a mesma linha antes
            // de ler a situação; uma leitura anterior ao lock poderia desfazer uma reserva.
            var mensagem = await repository.ObterComLockAsync(empresaId, id, token);
            if (mensagem is null || mensagem.EmpresaId != empresaId) throw new MensagemProgramadaNaoEncontradaException(id);
            try
            {
                mensagem.Cancelar(relogio.GetUtcNow().UtcDateTime);
            }
            catch (RegraDeDominioVioladaException ex)
            {
                throw new UseCaseValidationException(ex.Message);
            }
            await unitOfWork.CommitAsync();
            return MensagemProgramadaResult.De(mensagem);
        }, ct);
}

/// <summary>
/// Para onde vai a mensagem (S39): a conversa aberta do canal, quando há; senão o cadastro
/// (telefone para WhatsApp e SMS, e-mail para e-mail). Instagram, Messenger e chat do site só
/// existem por conversa: o id do contato nesses canais só se conhece por ela.
/// </summary>
internal static class DestinoMensagemProgramada
{
    public static async Task<Conversa?> ConversaAsync(
        IConversaRepository conversas, Guid empresaId, ClienteEntity cliente, CanalConversa canal, Guid? conversaId, CancellationToken ct)
    {
        if (conversaId is { } id)
        {
            var conversa = await conversas.ObterPorIdAsync(empresaId, id, ct)
                ?? throw new RegraDeDominioVioladaException($"Conversa {id} não encontrada.");
            if (conversa.ClienteId != cliente.Id || conversa.Canal != canal)
                throw new RegraDeDominioVioladaException("A conversa informada não é deste cliente neste canal.");
            if (conversa.EstaAberta) return conversa;

            // #1290: a conversa do agendamento foi encerrada; o cliente pode ter voltado a falar numa
            // nova, do mesmo contato. Uma aberta já vinculada a outro cliente não serve.
            var atual = await conversas.ObterAbertaPorContatoAsync(empresaId, canal, conversa.ContatoIdExterno, ct);
            return atual is not null && (atual.ClienteId is null || atual.ClienteId == cliente.Id) ? atual : null;
        }

        var contatoCadastro = ContatoDoCadastro(cliente, canal);
        return contatoCadastro is null ? null : await conversas.ObterAbertaPorContatoAsync(empresaId, canal, contatoCadastro, ct);
    }

    public static string Contato(ClienteEntity cliente, CanalConversa canal, Conversa? conversa)
    {
        if (conversa is not null) return conversa.ContatoIdExterno;
        var doCadastro = ContatoDoCadastro(cliente, canal)
            ?? throw new RegraDeDominioVioladaException(canal switch
            {
                CanalConversa.WhatsApp or CanalConversa.Sms => "O cliente não tem telefone no cadastro.",
                CanalConversa.Email => "O cliente não tem e-mail no cadastro.",
                _ => $"No {canal} a mensagem precisa de uma conversa aberta com o cliente.",
            });
        return Conversa.NormalizarContato(canal, doCadastro);
    }

    private static string? ContatoDoCadastro(ClienteEntity cliente, CanalConversa canal)
    {
        switch (canal)
        {
            case CanalConversa.WhatsApp or CanalConversa.Sms when !string.IsNullOrWhiteSpace(cliente.Telefone):
                try { return NormalizadorTelefone.NormalizarE164Br(cliente.Telefone); }
                catch (Exception) { return null; }
            case CanalConversa.Email when !string.IsNullOrWhiteSpace(cliente.Email):
                return cliente.Email;
            default:
                return null;
        }
    }
}
