using EasyStock.Domain.Entities.Banners;

namespace EasyStock.Application.UseCases.Banners;

public sealed record CriarBannerCommand(BannerConteudo Dados, Guid? CriadoPorUsuarioId);

public sealed record CriarBannerResult(Guid BannerId);

public sealed class CriarBannerUseCase(IBannerRepository repo, IUnitOfWork uow)
{
    public async Task<CriarBannerResult> ExecuteAsync(CriarBannerCommand cmd, CancellationToken ct = default)
    {
        Banner banner;
        try
        {
            banner = Banner.Criar(cmd.Dados, cmd.CriadoPorUsuarioId);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }

        await repo.InserirAsync(banner, ct);
        await uow.CommitAsync();

        // Notificação opcional (NotificarAoPublicar) é enfileirada na Fatia 6, junto do
        // consumidor no worker — para não haver evento no outbox sem quem o consuma.
        return new CriarBannerResult(banner.Id);
    }
}
