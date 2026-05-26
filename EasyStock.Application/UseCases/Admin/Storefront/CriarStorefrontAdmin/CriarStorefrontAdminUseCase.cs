using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions.Storefront;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.UseCases.Admin.Storefront.CriarStorefrontAdmin;

public sealed record CriarStorefrontAdminCommand(
    Guid EmpresaId,
    string Slug,
    string TituloPublico,
    decimal PedidoMinimoEntrega) : ICommand;

public sealed record CriarStorefrontAdminResult(Guid StorefrontId, string Slug);

/// <summary>
/// Criação de Storefront pelo super-admin. Garante:
/// (1) Empresa existe;
/// (2) Empresa ainda não tem storefront (1:1 — EmpresaJaTemStorefrontException);
/// (3) Slug não está em uso globalmente (StorefrontSlugDuplicadoException).
///
/// Defaults seguros aplicados pela factory <see cref="StorefrontEntity.Criar"/>:
/// Ativo=false, NfeAutomaticaHabilitada=false, ModeloFiscal="manual" — ADR-0007/0010.
/// </summary>
public class CriarStorefrontAdminUseCase(
    IStorefrontRepository storefrontRepository,
    IEmpresaRepository empresaRepository,
    IUnitOfWork unitOfWork)
    : IUseCase<CriarStorefrontAdminCommand, CriarStorefrontAdminResult>
{
    public async Task<CriarStorefrontAdminResult> ExecuteAsync(CriarStorefrontAdminCommand command)
    {
        if (command.EmpresaId == Guid.Empty)
            throw new UseCaseValidationException("EmpresaId é obrigatório.");

        var empresa = await empresaRepository.GetByIdAsync(command.EmpresaId)
            ?? throw new UseCaseValidationException(
                "EMPRESA_INEXISTENTE",
                $"Empresa {command.EmpresaId} não existe.");

        var existente = await storefrontRepository.GetByEmpresaAsync(command.EmpresaId);
        if (existente is not null)
            throw new EmpresaJaTemStorefrontException(command.EmpresaId, existente.Id);

        var slugConflito = await storefrontRepository.GetBySlugAsync(command.Slug.Trim().ToLowerInvariant());
        if (slugConflito is not null)
            throw new StorefrontSlugDuplicadoException(command.Slug);

        // Factory aplica todas validações de domínio (slug regex, tamanho título, etc).
        var novo = StorefrontEntity.Criar(
            empresaId: command.EmpresaId,
            slug: command.Slug,
            tituloPublico: command.TituloPublico,
            pedidoMinimoEntrega: command.PedidoMinimoEntrega);

        await storefrontRepository.AddAsync(novo);
        await unitOfWork.CommitAsync();

        return new CriarStorefrontAdminResult(novo.Id, novo.Slug);
    }
}
