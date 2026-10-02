using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Inbox;

/// <summary>
/// "Enviar cardápio" do console (#1353): gera o link do cardápio da loja ligado à conversa, o mesmo
/// que o agente manda (<see cref="LinkCardapioConversaService"/>, S48). Conversa de outra empresa é 404
/// e não grava link.
/// </summary>
public sealed class GerarLinkCardapioConversaUseCase(
    IConversaRepository conversaRepository,
    LinkCardapioConversaService linkCardapio,
    IUnitOfWork unitOfWork,
    TimeProvider relogio)
{
    public async Task<LinkCardapioConversaGerado> ExecuteAsync(Guid empresaId, Guid conversaId, CancellationToken ct = default)
    {
        _ = await conversaRepository.ObterPorIdAsync(empresaId, conversaId, ct)
            ?? throw new ConversaNaoEncontradaException(conversaId);

        var link = await linkCardapio.GerarAsync(empresaId, conversaId, relogio.GetUtcNow().UtcDateTime, ct);
        await unitOfWork.CommitAsync();
        return link;
    }
}
