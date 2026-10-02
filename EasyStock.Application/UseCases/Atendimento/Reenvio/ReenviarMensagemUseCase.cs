using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Reenvio;

/// <summary>
/// S57 (#1355): reenvia um texto que falhou, pelo botão do console ou pelo serviço de fundo. Fora da janela
/// de 24 h não envia e encerra o reenvio automático (o caminho é um modelo aprovado). Se falhar de novo, a
/// falha é registrada e, sendo temporária, reagendada pela próxima espera.
/// </summary>
public sealed class ReenviarMensagemUseCase(
    IConversaRepository conversaRepository,
    ResolvedorCanal resolvedorCanal,
    IUnitOfWork unitOfWork,
    TimeProvider relogio)
{
    public async Task<MensagemAtendimentoResult> ExecuteAsync(
        Guid empresaId, Guid conversaId, Guid mensagemId, CancellationToken ct = default)
    {
        var mensagem = await conversaRepository.ObterMensagemParaAlterarAsync(empresaId, conversaId, mensagemId, ct)
            ?? throw new MensagemNaoEncontradaException(mensagemId);
        if (!mensagem.PodeReenviar)
            throw new UseCaseValidationException("Só texto que falhou pode ser reenviado.");
        var conversa = await conversaRepository.ObterPorIdAsync(empresaId, conversaId, ct)
            ?? throw new ConversaNaoEncontradaException(conversaId);

        var agora = relogio.GetUtcNow().UtcDateTime;
        if (!conversa.DentroDaJanela(agora))
        {
            mensagem.RegistrarFalhaEnvio("Fora da janela de 24 h: só um modelo aprovado alcança o cliente.",
                TipoFalhaEnvio.Permanente, agora);
            await unitOfWork.CommitAsync();
            return MensagemAtendimentoResult.De(mensagem);
        }

        mensagem.ReservarReenvio();
        try
        {
            var externoId = await resolvedorCanal.Obter(conversa.Canal)
                .EnviarTextoAsync(conversa.ContatoIdExterno, mensagem.Texto!, ct);
            mensagem.RegistrarReenviada(externoId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            mensagem.RegistrarFalhaEnvio(ex.Message, ClassificadorFalhaEnvio.Classificar(ex), agora);
        }

        await unitOfWork.CommitAsync();
        return MensagemAtendimentoResult.De(mensagem);
    }
}

/// <summary>Mensagem inexistente, de outra conversa ou de outra empresa: 404.</summary>
public sealed class MensagemNaoEncontradaException(Guid mensagemId)
    : Exception($"Mensagem {mensagemId} não encontrada.")
{
    public Guid MensagemId { get; } = mensagemId;
}
