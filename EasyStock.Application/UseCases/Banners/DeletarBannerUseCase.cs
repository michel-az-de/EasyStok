namespace EasyStock.Application.UseCases.Banners;

public sealed record DeletarBannerCommand(Guid Id);

public sealed class DeletarBannerUseCase(
    IBannerRepository repo,
    IBannerConfirmacaoRepository confirmacaoRepo,
    IUnitOfWork uow)
{
    public async Task ExecuteAsync(DeletarBannerCommand cmd, CancellationToken ct = default)
    {
        var banner = await repo.ObterAsync(cmd.Id, ct)
            ?? throw new BannerNaoEncontradoException();

        // Pré-check amigável: preserva a prova de recebimento (FK Restrict). A corrida
        // (confirmação inserida entre o check e o commit) vira violação de FK 23503, que o
        // controller traduz para o MESMO 409 — defesa em profundidade.
        if (await confirmacaoRepo.PossuiParaBannerAsync(cmd.Id, ct))
            throw new BannerComConfirmacoesException();

        await repo.RemoverAsync(banner, ct);
        await uow.CommitAsync();
    }
}
