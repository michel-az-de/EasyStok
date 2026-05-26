namespace EasyStock.Domain.Exceptions.Storefront;

/// <summary>
/// Lançada quando se tenta criar um segundo Storefront para uma Empresa que
/// já tem um. Storefront é 1:1 com Empresa (ver ADR-0002 multi-tenancy +
/// IStorefrontRepository.GetByEmpresaAsync que retorna 1). Mapeada para HTTP 409.
/// </summary>
public class EmpresaJaTemStorefrontException : RegraDeDominioVioladaException
{
    public Guid EmpresaId { get; }
    public Guid StorefrontExistenteId { get; }

    public EmpresaJaTemStorefrontException(Guid empresaId, Guid storefrontExistenteId)
        : base($"A empresa {empresaId} já possui um storefront ({storefrontExistenteId}). " +
               "Edite o existente em vez de criar um novo.")
    {
        EmpresaId = empresaId;
        StorefrontExistenteId = storefrontExistenteId;
    }
}
