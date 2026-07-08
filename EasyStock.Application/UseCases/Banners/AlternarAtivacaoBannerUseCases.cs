namespace EasyStock.Application.UseCases.Banners;

public sealed record AtivarBannerCommand(Guid Id);

public sealed class AtivarBannerUseCase(IBannerRepository repo, IUnitOfWork uow)
{
    public async Task ExecuteAsync(AtivarBannerCommand cmd, CancellationToken ct = default)
    {
        var banner = await repo.ObterAsync(cmd.Id, ct)
            ?? throw new BannerNaoEncontradoException();
        banner.Ativar();
        await repo.AtualizarAsync(banner, ct);
        await uow.CommitAsync();
        // A notificação da primeira ativação é cabeada na Fatia 6 (guard atômico + worker).
    }
}

public sealed record DesativarBannerCommand(Guid Id);

public sealed class DesativarBannerUseCase(IBannerRepository repo, IUnitOfWork uow)
{
    public async Task ExecuteAsync(DesativarBannerCommand cmd, CancellationToken ct = default)
    {
        var banner = await repo.ObterAsync(cmd.Id, ct)
            ?? throw new BannerNaoEncontradoException();
        banner.Desativar();
        await repo.AtualizarAsync(banner, ct);
        await uow.CommitAsync();
    }
}
