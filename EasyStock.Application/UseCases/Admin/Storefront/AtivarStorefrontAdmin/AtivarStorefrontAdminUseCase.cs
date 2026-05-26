using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Admin.Storefront.AtivarStorefrontAdmin;

public sealed record AtivarStorefrontAdminCommand(Guid Id) : ICommand;

public sealed record AtivarStorefrontAdminResult(Guid Id, bool Ativo);

/// <summary>
/// Ativa Storefront. Nesta task NÃO valida pré-requisitos cross-aggregate
/// (cardápio visível, FreteZona configurado, MP credencial). Admin pode ativar
/// loja incompleta (UI mostra warning amarelo); TASK-EZ-ADMIN-002 adiciona
/// validação completa via cross-aggregate.
/// </summary>
public class AtivarStorefrontAdminUseCase(
    IStorefrontRepository storefrontRepository,
    IUnitOfWork unitOfWork)
    : IUseCase<AtivarStorefrontAdminCommand, AtivarStorefrontAdminResult>
{
    public async Task<AtivarStorefrontAdminResult> ExecuteAsync(AtivarStorefrontAdminCommand command)
    {
        var s = await storefrontRepository.GetByIdAsync(command.Id)
            ?? throw new StorefrontNaoEncontradoException();

        s.Ativar();
        await storefrontRepository.UpdateAsync(s);
        await unitOfWork.CommitAsync();

        return new AtivarStorefrontAdminResult(s.Id, s.Ativo);
    }
}
