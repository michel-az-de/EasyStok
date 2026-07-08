using EasyStock.Domain.Entities.Banners;

namespace EasyStock.Application.UseCases.Banners;

public sealed record AtualizarBannerCommand(Guid Id, BannerConteudo Dados);

public sealed class AtualizarBannerUseCase(IBannerRepository repo, IUnitOfWork uow)
{
    public async Task ExecuteAsync(AtualizarBannerCommand cmd, CancellationToken ct = default)
    {
        var banner = await repo.ObterAsync(cmd.Id, ct)
            ?? throw new BannerNaoEncontradoException();

        // Revalida TODAS as invariantes (mesma porta de Criar) — o PUT não é atalho.
        try
        {
            banner.Atualizar(cmd.Dados);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }

        await repo.AtualizarAsync(banner, ct);
        await uow.CommitAsync();
    }
}
