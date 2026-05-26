using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Admin.Storefront.DesativarStorefrontAdmin;

public sealed record DesativarStorefrontAdminCommand(Guid Id) : ICommand;

public sealed record DesativarStorefrontAdminResult(Guid Id, bool Ativo);

/// <summary>
/// Desativa Storefront — equivale a "soft delete" para o MVP admin (Ativo=false
/// remove da resolução pública /api/storefront/{slug}/...). Pedidos pendentes
/// NÃO são cancelados aqui — fica para job de reconciliação separado.
/// </summary>
public class DesativarStorefrontAdminUseCase(
    IStorefrontRepository storefrontRepository,
    IUnitOfWork unitOfWork)
    : IUseCase<DesativarStorefrontAdminCommand, DesativarStorefrontAdminResult>
{
    public async Task<DesativarStorefrontAdminResult> ExecuteAsync(DesativarStorefrontAdminCommand command)
    {
        var s = await storefrontRepository.GetByIdAsync(command.Id)
            ?? throw new StorefrontNaoEncontradoException();

        s.Desativar();
        await storefrontRepository.UpdateAsync(s);
        await unitOfWork.CommitAsync();

        return new DesativarStorefrontAdminResult(s.Id, s.Ativo);
    }
}
