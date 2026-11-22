using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Reenvio;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Inbox;

public sealed record ReagirMensagemCommand(Guid EmpresaId, Guid UsuarioId, Guid ConversaId, Guid MensagemId, string? Emoji);

public sealed record ApagarMensagemCommand(Guid EmpresaId, Guid UsuarioId, Guid ConversaId, Guid MensagemId);

/// <summary>
/// #2833: a loja reage a uma mensagem ou apaga para todos a que ela enviou. A regra do domínio é conferida antes de
/// chamar o canal, e só o que o canal confirmou é gravado: com falha no canal, nada muda. A mensagem fica travada
/// (<c>FOR UPDATE</c>) para dois cliques não chamarem o canal duas vezes.
/// </summary>
public sealed class AcoesNaMensagemUseCase(
    IConversaRepository conversaRepository,
    ResolvedorCanal resolvedorCanal,
    IUnitOfWork unitOfWork,
    TimeProvider relogio)
{
    public Task<MensagemAtendimentoResult> ReagirAsync(ReagirMensagemCommand cmd, CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionSemRetryAsync(async _ =>
        {
            var (conversa, mensagem) = await CarregarAsync(cmd.EmpresaId, cmd.ConversaId, cmd.MensagemId, ct);
            var emoji = mensagem.ValidarReacao(cmd.Emoji);
            await NoCanalAsync(conversa, acoes => acoes.ReagirAsync(conversa.ContatoIdExterno, mensagem.ExternoId!, emoji, ct));
            mensagem.Reagir(emoji);
            await unitOfWork.CommitAsync();
            return MensagemAtendimentoResult.De(mensagem);
        }, ct);

    public Task<MensagemAtendimentoResult> ApagarParaTodosAsync(ApagarMensagemCommand cmd, CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionSemRetryAsync(async _ =>
        {
            var (conversa, mensagem) = await CarregarAsync(cmd.EmpresaId, cmd.ConversaId, cmd.MensagemId, ct);
            var agora = relogio.GetUtcNow().UtcDateTime;
            mensagem.ValidarApagarParaTodos(agora);
            await NoCanalAsync(conversa, acoes => acoes.ApagarParaTodosAsync(conversa.ContatoIdExterno, mensagem.ExternoId!, ct));
            mensagem.ApagarParaTodos(cmd.UsuarioId, agora);
            await unitOfWork.CommitAsync();
            return MensagemAtendimentoResult.De(mensagem);
        }, ct);

    private async Task<(Conversa Conversa, Mensagem Mensagem)> CarregarAsync(
        Guid empresaId, Guid conversaId, Guid mensagemId, CancellationToken ct)
    {
        var mensagem = await conversaRepository.ObterMensagemComLockAsync(empresaId, conversaId, mensagemId, ct)
                       ?? throw new MensagemNaoEncontradaException(mensagemId);
        var conversa = await conversaRepository.ObterPorIdAsync(empresaId, conversaId, ct)
                       ?? throw new ConversaNaoEncontradaException(conversaId);
        return (conversa, mensagem);
    }

    private async Task NoCanalAsync(Conversa conversa, Func<ICanalComAcoesNaMensagem, Task> acao)
    {
        if (resolvedorCanal.Obter(conversa.Canal) is not ICanalComAcoesNaMensagem acoes)
            throw new AcaoNaoSuportadaWhatsAppException("Este canal não reage nem apaga mensagens.");
        try
        {
            await acao(acoes);
        }
        catch (WhatsAppCloudException ex) when (ex.Codigo == WhatsAppCloudException.CodigoForaDaJanela)
        {
            throw new ForaDaJanelaAtendimentoException("Fora da janela de 24 h do WhatsApp.", ex);
        }
        catch (WhatsAppCloudException ex)
        {
            throw new FalhaEnvioCanalException(ex.Message, ex);
        }
    }
}
