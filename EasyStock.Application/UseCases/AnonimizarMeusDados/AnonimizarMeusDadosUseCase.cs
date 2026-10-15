using EasyStock.Application.Ports.Output.Notifications;

namespace EasyStock.Application.UseCases.AnonimizarMeusDados;

public sealed record AnonimizarMeusDadosCommand(string ConfirmacaoTexto);

public sealed record AnonimizarMeusDadosResult(
    Guid UsuarioId,
    DateTime AnonimizadoEm,
    int RefreshTokensRemovidos,
    int ResetTokensRemovidos,
    int EmailConfirmationTokensRemovidos);

/// <summary>
/// LGPD Art. 18 (direito ao esquecimento). Anonimiza o usuário autenticado:
/// pseudonimiza campos PII (Nome, Email, AvatarUrl), zera credenciais (SenhaHash) e
/// desativa a conta. Hard-delete dos tokens de credenciais (Refresh/Reset/Confirm
/// email). AuditLog, MovimentacoesEstoque e demais entidades com valor histórico
/// mantêm UsuarioId — preserva trilha forense conforme orientação ANPD.
/// Operação irreversível.
/// </summary>
public sealed class AnonimizarMeusDadosUseCase(
    IUsuarioRepository usuarioRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IResetTokenRepository resetTokenRepository,
    IEmailConfirmationTokenRepository emailConfirmationTokenRepository,
    IConsentimentoRepository consentimentoRepository,
    IPreferenciaNotificacaoRepository preferenciaRepository,
    ICurrentUserAccessor currentUserAccessor,
    IUnitOfWork unitOfWork,
    ILogger<AnonimizarMeusDadosUseCase> logger)
{
    private const string ConfirmacaoEsperada = "ANONIMIZAR";

    public async Task<AnonimizarMeusDadosResult> ExecuteAsync(AnonimizarMeusDadosCommand command)
    {
        var usuarioId = currentUserAccessor.UsuarioId;
        if (usuarioId == Guid.Empty)
            throw new UsuarioNaoAutorizadoException("Usuario nao autenticado.");

        if (!string.Equals(command.ConfirmacaoTexto?.Trim(), ConfirmacaoEsperada, StringComparison.Ordinal))
            throw new UseCaseValidationException(
                $"Confirmacao invalida. Envie ConfirmacaoTexto='{ConfirmacaoEsperada}' para confirmar a anonimizacao.");

        var usuario = await usuarioRepository.GetByIdAsync(usuarioId)
            ?? throw new RegraDeDominioVioladaException("Usuario nao encontrado.");

        // Hard-delete das credenciais — sem valor historico.
        var refreshRemovidos = await refreshTokenRepository.DeleteAllByUsuarioIdAsync(usuarioId);
        var resetRemovidos = await resetTokenRepository.DeleteAllByUsuarioIdAsync(usuarioId);
        var confirmacaoRemovidos = await emailConfirmationTokenRepository.DeleteAllByUsuarioIdAsync(usuarioId);

        // N4: o contato some do usuario (Anonimizar) e o consentimento que o acompanhava tambem: opt-ins revogados, sem
        // o IP de origem, e as preferencias de rotina apagadas.
        await consentimentoRepository.RevogarPorAnonimizacaoAsync(usuarioId);
        await preferenciaRepository.RemoverPorUsuarioAsync(usuarioId);

        usuario.Anonimizar();
        await usuarioRepository.UpdateAsync(usuario);
        await unitOfWork.CommitAsync();

        logger.LogWarning(
            "AUDIT: Usuario {UsuarioId} anonimizado (LGPD Art.18). Tokens removidos — refresh:{R} reset:{S} confirmEmail:{C}.",
            usuarioId, refreshRemovidos, resetRemovidos, confirmacaoRemovidos);

        return new AnonimizarMeusDadosResult(
            usuarioId, usuario.AlteradoEm, refreshRemovidos, resetRemovidos, confirmacaoRemovidos);
    }
}
