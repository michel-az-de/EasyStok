namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

/// <summary>Perfil que o usuário tem na empresa: nível e permissões explícitas (vazio = vale o nível).</summary>
public sealed record PerfilNaEmpresa(NivelAcesso Nivel, IReadOnlyCollection<Permissao> Permissoes,
    bool PermissoesExplicitas = false);

/// <summary>Usuário ativo vinculado à empresa, com os perfis que tem nela.</summary>
public sealed record UsuarioDaEmpresa(Guid UsuarioId, string Nome, string Email, IReadOnlyList<PerfilNaEmpresa> Perfis);

/// <summary>
/// Leitura dos usuários de uma empresa para decidir quem atende conversas (S41). Os perfis padrão
/// não têm <c>EmpresaId</c> e ficam fora do filtro de tenant; a implementação lê com bypass e põe
/// o <c>empresaId</c> no WHERE de cada vínculo.
/// </summary>
public interface IAtendenteRepository
{
    Task<IReadOnlyList<UsuarioDaEmpresa>> ListarUsuariosAtivosAsync(Guid empresaId, CancellationToken ct = default);

    Task<UsuarioDaEmpresa?> ObterUsuarioAtivoAsync(Guid empresaId, Guid usuarioId, CancellationToken ct = default);
}
