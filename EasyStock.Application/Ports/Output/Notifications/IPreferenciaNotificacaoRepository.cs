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

    /// <summary>
    /// #2211: liga ou desliga a rotina para a pessoa (cria a linha se faltar). Grava no commit da unidade de trabalho. A
    /// chave da tabela é pessoa e rotina; a empresa da linha passa a ser a desta chamada.
    /// </summary>
    Task DefinirAsync(Guid usuarioId, Guid empresaId, string rotinaCodigo, bool habilitada, CancellationToken ct = default);

    /// <summary>Apaga as preferências do usuário (anonimização). Imediato, como a remoção dos tokens.</summary>
    Task<int> RemoverPorUsuarioAsync(Guid usuarioId, CancellationToken ct = default);
}
