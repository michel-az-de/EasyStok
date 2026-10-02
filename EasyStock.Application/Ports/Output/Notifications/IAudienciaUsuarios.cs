namespace EasyStock.Application.Ports.Output.Notifications;

/// <summary>
/// Projeção de um usuário interno para a audiência (N4): só o que decide quem recebe e por onde. Nunca leva a
/// senha nem o endereço pendente. <see cref="ConvitePendente"/> (N9): ainda não aceitou o convite, o único caso em que o
/// WhatsApp dispensa o telefone verificado.
/// </summary>
public sealed record UsuarioParaAudiencia(
    Guid Id,
    string Nome,
    string Email,
    bool EmailConfirmado,
    bool Ativo,
    string? Telefone,
    DateTime? TelefoneVerificadoEm,
    bool ConvitePendente = false);

/// <summary>Usuários de uma empresa por nível de acesso (N4). Roda no tenant do evento: a empresa do escopo é a do evento.</summary>
public interface IAudienciaUsuarios
{
    /// <summary>O usuário pelo id (o explícito do payload), ou <c>null</c> quando não existe.</summary>
    Task<UsuarioParaAudiencia?> ObterAsync(Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// Usuários da empresa (vínculo <c>UsuarioEmpresa</c> ativo) com perfil de um dos níveis, na empresa do evento.
    /// Quem tem perfil de SuperAdmin não entra: a audiência dele é a <c>superadmins</c>.
    /// </summary>
    Task<IReadOnlyList<UsuarioParaAudiencia>> ListarDaEmpresaAsync(
        Guid empresaId, IReadOnlyCollection<NivelAcesso> niveis, CancellationToken ct = default);
}

/// <summary>
/// Superadmins da plataforma (N4). SuperAdmin é global (<c>Perfil.EmpresaId</c> nulo, sem <c>UsuarioEmpresa</c>), então a
/// consulta é cross-tenant por natureza e roda num escopo de DI próprio com o bypass de RLS aberto antes da primeira
/// conexão, nunca dentro do escopo do evento.
/// </summary>
public interface ISuperAdminsDaPlataforma
{
    Task<IReadOnlyList<UsuarioParaAudiencia>> ListarAsync(CancellationToken ct = default);
}
