using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;

namespace EasyStock.Application.UseCases.Faq
{
    public sealed record RegistrarFeedbackFaqCommand(
        Guid ItemId,
        bool Util,
        string? Comentario,
        string? Ip);

    public sealed class RegistrarFeedbackFaqUseCase(
        IFaqRepository faqRepo,
        IFaqAdminRepository adminRepo,
        IUnitOfWork uow)
    {
        public async Task ExecuteAsync(RegistrarFeedbackFaqCommand cmd, CancellationToken ct = default)
        {
            if (cmd.ItemId == Guid.Empty)
                throw new UseCaseValidationException("Item de FAQ invalido.");
            if (cmd.Comentario is { Length: > 1000 })
                throw new UseCaseValidationException("Comentario excede 1000 caracteres.");

            var item = await adminRepo.ObterItemAsync(cmd.ItemId, ct);
            if (item is null || item.Status != Domain.Enums.FaqStatus.Publicado)
                throw new UseCaseValidationException("Item de FAQ nao disponivel.");

            var ipHash = ObterFaqItemUseCase.HashIp(cmd.Ip);
            var feedback = FaqFeedback.Criar(cmd.ItemId, cmd.Util, ipHash, cmd.Comentario);

            await faqRepo.RegistrarFeedbackAsync(feedback, ct);
            await faqRepo.IncrementarContadoresAsync(
                cmd.ItemId,
                deltaVisualizacao: 0,
                deltaUtil: cmd.Util ? 1 : 0,
                deltaNaoUtil: cmd.Util ? 0 : 1,
                ct);
            await uow.CommitAsync();
        }
    }
}
