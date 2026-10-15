namespace EasyStock.Application.Services.Auth;

/// <summary>
/// A empresa em que nasce o evento de um pedido anônimo (esqueci a senha, reset: sem JWT, sem tenant): a empresa do
/// usuário quando ele tem uma só empresa ativa e a empresa padrão da plataforma (ADR-0057, item 7) nos demais casos
/// (superadmin, duas empresas, sem vínculo). Fixa o tenant no escopo antes de gravar, como o webhook do atendimento:
/// sem isso a RLS barra o INSERT do evento (42501). Sem empresa resolvida devolve <c>null</c> e o chamador não publica.
/// </summary>
public sealed class EmpresaDoEventoAnonimo(
    IEmpresaPadraoResolver empresaPadrao, ITenantContextAccessor tenant, ILogger<EmpresaDoEventoAnonimo> logger)
{
    public async Task<Guid?> ResolverAsync(Usuario usuario, CancellationToken ct = default)
    {
        Guid? empresaId = null;
        if (!usuario.EhSuperAdmin())
        {
            var ativas = usuario.Empresas.Where(e => e.Ativo).Select(e => e.EmpresaId).Distinct().ToList();
            if (ativas.Count == 1) empresaId = ativas[0];
        }

        empresaId ??= await empresaPadrao.ResolverAsync(ct);
        if (empresaId is not { } id || id == Guid.Empty)
        {
            logger.LogWarning(
                "Evento de acesso do usuario {UsuarioId} sem empresa: ele nao tem uma so empresa ativa e {Chave} nao resolve.",
                usuario.Id, EmpresaPadraoResolver.Chave);
            return null;
        }

        tenant.SetCurrentTenant(id);
        return id;
    }
}
