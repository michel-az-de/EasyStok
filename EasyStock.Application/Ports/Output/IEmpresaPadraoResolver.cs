namespace EasyStock.Application.Ports.Output;

/// <summary>
/// Empresa padrão da plataforma (ADR-0057, item 7; tenant único da ADR-0056): <c>Auth:Google:EmpresaPadrao</c>, CNPJ ou
/// nome exato. Dona dos eventos sem tenant próprio (aviso de plataforma) e empresa do superadmin sem empresa no token.
/// </summary>
public interface IEmpresaPadraoResolver
{
    /// <summary>
    /// O id da empresa padrão, ou <c>null</c> sem a chave ou sem casamento único (e loga aviso nomeando a chave). Quem
    /// precisa da empresa não publica quando vem <c>null</c>.
    /// </summary>
    Task<Guid?> ResolverAsync(CancellationToken ct = default);
}
