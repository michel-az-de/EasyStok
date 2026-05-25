namespace EasyStock.Application.UseCases.Common;

/// <summary>
/// Helpers defensivos reutilizáveis em use cases para centralizar validações
/// estruturais (IDs obrigatórios, tenant scope, etc.) que antes eram
/// replicadas em cada handler.
/// </summary>
public static class UseCaseGuards
{
    /// <summary>
    /// Garante que o <paramref name="empresaId"/> foi informado. Usado no topo
    /// de use cases que têm escopo multi-tenant.
    /// </summary>
    public static void EnsureEmpresaId(Guid empresaId)
    {
        if (empresaId == Guid.Empty)
            throw new UseCaseValidationException("EmpresaId é obrigatório.");
    }

    /// <summary>
    /// Variante para IDs obrigatórios genéricos. Mensagem customizável para
    /// ajudar o cliente a identificar qual campo faltou.
    /// </summary>
    public static void EnsureNotEmpty(Guid id, string nomeCampo)
    {
        if (id == Guid.Empty)
            throw new UseCaseValidationException($"{nomeCampo} é obrigatório.");
    }
}
