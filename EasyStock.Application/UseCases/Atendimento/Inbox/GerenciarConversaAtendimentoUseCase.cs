using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Inbox;

public sealed record AcaoConversaCommand(Guid EmpresaId, Guid UsuarioId, Guid ConversaId);

/// <summary>
/// Ações da dona sobre a conversa (S07): assumir sem escrever, devolver ao agente (D4: a retomada é
/// decisão dela, nunca por tempo), encerrar e marcar como lida. Cada ação confirma sozinha.
/// </summary>
public sealed class GerenciarConversaAtendimentoUseCase(IConversaRepository conversaRepository, IUnitOfWork unitOfWork)
{
    public const string PrefixoLiberacao = "atendimento automático retomado pelo usuário ";

    public Task<ConversaSituacaoResult> AssumirAsync(AcaoConversaCommand command, CancellationToken ct = default) =>
        ExecutarAsync(command, (conversa, agora) =>
        {
            conversa.Assumir(agora, command.UsuarioId);
            return Task.CompletedTask;
        }, ct);

    /// <summary>Volta a <see cref="SituacaoConversa.Automatica"/> e deixa nota interna com quem liberou.</summary>
    public Task<ConversaSituacaoResult> LiberarAutomaticoAsync(AcaoConversaCommand command, CancellationToken ct = default) =>
        ExecutarAsync(command, (conversa, agora) =>
        {
            conversa.LiberarAutomatico();
            return conversaRepository.AddMensagemAsync(
                Mensagem.Saida(command.EmpresaId, conversa.Id, AutorMensagem.Sistema, agora,
                    TipoConteudoMensagem.Texto, PrefixoLiberacao + command.UsuarioId),
                ct);
        }, ct);

    /// <summary>Terminal: a próxima mensagem do contato abre outra conversa Automatica (S05).</summary>
    public Task<ConversaSituacaoResult> EncerrarAsync(AcaoConversaCommand command, CancellationToken ct = default) =>
        ExecutarAsync(command, (conversa, agora) =>
        {
            conversa.Encerrar(agora);
            return Task.CompletedTask;
        }, ct);

    public Task<ConversaSituacaoResult> MarcarLidaAsync(AcaoConversaCommand command, CancellationToken ct = default) =>
        ExecutarAsync(command, (conversa, _) =>
        {
            conversa.MarcarLida();
            return Task.CompletedTask;
        }, ct);

    private async Task<ConversaSituacaoResult> ExecutarAsync(
        AcaoConversaCommand command, Func<Conversa, DateTime, Task> acao, CancellationToken ct)
    {
        var conversa = await conversaRepository.ObterPorIdAsync(command.EmpresaId, command.ConversaId, ct)
            ?? throw new ConversaNaoEncontradaException(command.ConversaId);

        await acao(conversa, DateTime.UtcNow);
        await unitOfWork.CommitAsync();
        return ConversaSituacaoResult.De(conversa);
    }
}
