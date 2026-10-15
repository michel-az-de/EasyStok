using EasyStock.Domain.Entities.Notifications;

namespace EasyStock.Application.Ports.Output.Notifications;

/// <summary>Preferências de rotina por usuário (<c>notif_preferencias_usuario</c>).</summary>
public interface IPreferenciaNotificacaoRepository
{
    /// <summary>Todas as preferências do usuário, para o export de dados pessoais (LGPD).</summary>
    Task<IReadOnlyList<PreferenciaNotificacaoUsuario>> ListarPorUsuarioAsync(Guid usuarioId, CancellationToken ct = default);

    /// <summary>As preferências dessas pessoas numa rotina da empresa; a audiência (N4) pula quem desligou a rotina.</summary>
    Task<IReadOnlyList<PreferenciaNotificacaoUsuario>> ListarDaRotinaAsync(
        Guid empresaId, string rotinaCodigo, IReadOnlyCollection<Guid> usuarioIds, CancellationToken ct = default);

    /// <summary>Apaga as preferências do usuário (anonimização). Imediato, como a remoção dos tokens.</summary>
    Task<int> RemoverPorUsuarioAsync(Guid usuarioId, CancellationToken ct = default);
}
