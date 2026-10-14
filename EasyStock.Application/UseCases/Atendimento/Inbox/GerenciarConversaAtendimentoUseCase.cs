using EasyStock.Application.Events.Atendimento;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Inbox;

/// <param name="UsuarioNome">Nome de quem agiu, para as notas internas que a dona lê no fio (#1474: nunca o id).</param>
public sealed record AcaoConversaCommand(Guid EmpresaId, Guid UsuarioId, Guid ConversaId, string? UsuarioNome = null)
{
    /// <summary>Nome para a nota; sem nome, "a equipe" (o id não diz nada a quem lê).</summary>
    public string QuemAgiu => string.IsNullOrWhiteSpace(UsuarioNome) ? "a equipe" : UsuarioNome.Trim();
}

/// <summary>
/// Ações da dona sobre a conversa (S07): assumir sem escrever, devolver ao agente (D4: a retomada é
/// decisão dela, nunca por tempo), encerrar e marcar como lida. Cada ação confirma sozinha.
/// Encerrar enfileira <see cref="ConversaEncerradaEvent"/> no outbox do mesmo commit (S42, ADR-0030).
/// </summary>
public sealed class GerenciarConversaAtendimentoUseCase(
    IConversaRepository conversaRepository, IUnitOfWork unitOfWork, IPublicadorEventoIntegracao? publicador = null,
    IQueueService? fila = null)
{
    public const string PrefixoLiberacao = "atendimento automático retomado por ";

    public Task<ConversaSituacaoResult> AssumirAsync(AcaoConversaCommand command, CancellationToken ct = default) =>
        ExecutarAsync(command, (conversa, agora) =>
        {
            conversa.Assumir(agora, command.UsuarioId);
            return Task.CompletedTask;
        }, ct);

    /// <summary>Volta a <see cref="SituacaoConversa.Automatica"/> e deixa nota interna com quem liberou.</summary>
    public async Task<ConversaSituacaoResult> LiberarAutomaticoAsync(AcaoConversaCommand command, CancellationToken ct = default)
    {
        var retomou = false;
        var resultado = await ExecutarAsync(command, (conversa, agora) =>
        {
            retomou = conversa.Situacao != SituacaoConversa.Automatica;
            conversa.LiberarAutomatico();
            return conversaRepository.AddMensagemAsync(
                Mensagem.Saida(command.EmpresaId, conversa.Id, AutorMensagem.Sistema, agora,
                    TipoConteudoMensagem.Texto, PrefixoLiberacao + command.QuemAgiu),
                ct);
        }, ct);
        // A entrada pendente já pode estar na conversa quando a atendente devolve à IA.
        if (retomou && fila is not null)
            await fila.EnqueueAsync(FilaAtendimentoNomes.TurnoAgente, new ProcessarTurnoAgenteJob(command.EmpresaId, command.ConversaId));
        return resultado;
    }

    public const string PrefixoLiberacaoForaDeArea = "entrega fora da área liberada por ";

    /// <summary>
    /// S14: a dona libera a entrega fora da área. Grava <c>foraDeAreaLiberado=true</c> e o motivo no contexto
    /// da conversa (o próximo <c>criar_pedido</c> marca o pedido para aprovação, S12) e deixa nota interna.
    /// </summary>
    public Task<ConversaSituacaoResult> LiberarForaDeAreaAsync(AcaoConversaCommand command, string? motivo, CancellationToken ct = default) =>
        ExecutarAsync(command, (conversa, agora) =>
        {
            var motivoLimpo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
            ContextoConversaJson.Gravar(conversa, ContextoConversaJson.ForaDeAreaLiberado, true);
            ContextoConversaJson.Gravar(conversa, ContextoConversaJson.ForaDeAreaMotivo, motivoLimpo);
            var texto = PrefixoLiberacaoForaDeArea + command.QuemAgiu + (motivoLimpo is null ? "" : $": {motivoLimpo}");
            if (texto.Length > Mensagem.TextoTamanhoMaximo) texto = texto[..Mensagem.TextoTamanhoMaximo];
            return conversaRepository.AddMensagemAsync(
                Mensagem.Saida(command.EmpresaId, conversa.Id, AutorMensagem.Sistema, agora, TipoConteudoMensagem.Texto, texto),
                ct);
        }, ct);

    /// <summary>
    /// Sem reabertura explícita, a próxima mensagem do contato abre outra conversa Automatica (S05). Idempotente também no
    /// evento: conversa já encerrada não publica de novo (a automática de encerramento sairia 2x, #1292).
    /// </summary>
    public Task<ConversaSituacaoResult> EncerrarAsync(AcaoConversaCommand command, CancellationToken ct = default) =>
        ExecutarAsync(command, (conversa, agora) =>
        {
            var estavaAberta = conversa.EstaAberta;
            conversa.Encerrar(agora);
            return publicador is null || !estavaAberta
                ? Task.CompletedTask
                : publicador.PublicarAsync(command.EmpresaId, ConversaEncerradaEvent.TipoEvento, "Conversa", conversa.Id,
                    new ConversaEncerradaEvent(conversa.Id, conversa.ClienteId), ct: ct);
        }, ct);

    public Task<ConversaSituacaoResult> MarcarLidaAsync(AcaoConversaCommand command, CancellationToken ct = default) =>
        ExecutarAsync(command, (conversa, _) =>
        {
            conversa.MarcarLida();
            return Task.CompletedTask;
        }, ct);

    /// <summary>
    /// #1792: a ação parte da linha travada e relida. Sem a trava, uma entrada gravada sobre a leitura velha desfazia o
    /// marcar como lida, e assumir sobre uma conversa já encerrada em outra aba a reabria.
    /// </summary>
    private Task<ConversaSituacaoResult> ExecutarAsync(
        AcaoConversaCommand command, Func<Conversa, DateTime, Task> acao, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionSemRetryAsync(async token =>
        {
            var conversa = await conversaRepository.ObterPorIdComLockAsync(command.EmpresaId, command.ConversaId, token)
                ?? throw new ConversaNaoEncontradaException(command.ConversaId);

            await acao(conversa, DateTime.UtcNow);
            await unitOfWork.CommitAsync();
            return ConversaSituacaoResult.De(conversa);
        }, ct);
}
