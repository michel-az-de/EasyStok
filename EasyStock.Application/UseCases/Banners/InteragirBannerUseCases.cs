namespace EasyStock.Application.UseCases.Banners;

/// <summary>
/// "Ok, recebi" (Confirmado) e "dispensar/visto" (Visto). Ambos idempotentes e keyed por
/// UsuarioId (global). Banner inativo/fora da janela ainda EXISTE, então a interação é aceita
/// (204) — evita a corrida "janela fechou durante o clique"; só 404 se o id não existe.
/// </summary>
public sealed record RegistrarInteracaoBannerCommand(Guid BannerId, Guid UsuarioId, BannerInteracaoTipo Tipo);

public sealed class RegistrarInteracaoBannerUseCase(
    IBannerRepository repo,
    IBannerConfirmacaoRepository confirmacaoRepo,
    IUnitOfWork uow)
{
    public async Task ExecuteAsync(RegistrarInteracaoBannerCommand cmd, CancellationToken ct = default)
    {
        _ = await repo.ObterAsync(cmd.BannerId, ct)
            ?? throw new BannerNaoEncontradoException();

        var inserido = await confirmacaoRepo.RegistrarAsync(cmd.BannerId, cmd.UsuarioId, cmd.Tipo, ct);
        if (inserido)
            await uow.CommitAsync();
        // Se já existia (idempotente), nada a commitar — retorno de sucesso igual.
    }
}
