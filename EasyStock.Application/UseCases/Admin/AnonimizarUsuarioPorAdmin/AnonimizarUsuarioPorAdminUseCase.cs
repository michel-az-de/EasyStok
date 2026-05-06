using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Admin.AnonimizarUsuarioPorAdmin;

public sealed record AnonimizarUsuarioPorAdminCommand(
    Guid UsuarioId,
    string ConfirmacaoEmail,
    string Motivo);

public sealed record AnonimizarUsuarioPorAdminResult(
    Guid UsuarioId,
    DateTime AnonimizadoEm,
    int RefreshTokensRemovidos,
    int ResetTokensRemovidos,
    int EmailConfirmationTokensRemovidos);

/// <summary>
/// LGPD Art. 18 (direito ao esquecimento) — versão admin/operacional.
/// Adapta <c>AnonimizarMeusDadosUseCase</c> para ser disparado por SuperAdmin
/// em vez do próprio usuário (usado quando cliente solicita anonimização via
/// canal de suporte). Diferenças:
/// - Recebe <c>UsuarioId</c> explícito (não tira do JWT).
/// - Confirmação por email exato do usuário-alvo (impede operação por engano).
/// - Exige justificativa (motivo) propagada pelo controller para AdminAuditLog.
/// Mesma lógica de pseudonimização + hard-delete de tokens; preserva FKs em
/// AuditLog/MovimentacoesEstoque (orientação ANPD: trilha forense). Irreversível.
/// </summary>
public sealed class AnonimizarUsuarioPorAdminUseCase(
    IUsuarioRepository usuarioRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IResetTokenRepository resetTokenRepository,
    IEmailConfirmationTokenRepository emailConfirmationTokenRepository,
    IUnitOfWork unitOfWork,
    ILogger<AnonimizarUsuarioPorAdminUseCase> logger)
{
    public async Task<AnonimizarUsuarioPorAdminResult> ExecuteAsync(AnonimizarUsuarioPorAdminCommand command)
    {
        if (command.UsuarioId == Guid.Empty)
            throw new UseCaseValidationException("UsuarioId obrigatório.");
        if (string.IsNullOrWhiteSpace(command.ConfirmacaoEmail))
            throw new UseCaseValidationException("ConfirmacaoEmail obrigatória.");
        if (string.IsNullOrWhiteSpace(command.Motivo) || command.Motivo.Trim().Length < 20)
            throw new UseCaseValidationException("Motivo obrigatório (≥20 caracteres) — anonimização é irreversível.");

        var usuario = await usuarioRepository.GetByIdAsync(command.UsuarioId)
            ?? throw new RegraDeDominioVioladaException("Usuário não encontrado.");

        // Double-confirm: operador precisa digitar o email do usuário-alvo. Evita
        // anonimizar o usuário errado por engano (LGPD action irreversível).
        var emailAtual = (usuario.Email ?? string.Empty).Trim();
        var emailDigitado = command.ConfirmacaoEmail.Trim();
        if (!string.Equals(emailAtual, emailDigitado, StringComparison.OrdinalIgnoreCase))
            throw new UseCaseValidationException(
                "Confirmação inválida — email digitado não bate com o do usuário-alvo. Confira e tente de novo.");

        // Hard-delete das credenciais — sem valor histórico.
        var refreshRemovidos = await refreshTokenRepository.DeleteAllByUsuarioIdAsync(command.UsuarioId);
        var resetRemovidos = await resetTokenRepository.DeleteAllByUsuarioIdAsync(command.UsuarioId);
        var confirmacaoRemovidos = await emailConfirmationTokenRepository.DeleteAllByUsuarioIdAsync(command.UsuarioId);

        usuario.Anonimizar();
        await usuarioRepository.UpdateAsync(usuario);
        await unitOfWork.CommitAsync();

        logger.LogWarning(
            "AUDIT-LGPD: Admin anonimizou usuário {UsuarioId} (Art.18). Tokens removidos — refresh:{R} reset:{S} confirmEmail:{C}. Motivo: {Motivo}",
            command.UsuarioId, refreshRemovidos, resetRemovidos, confirmacaoRemovidos, command.Motivo);

        return new AnonimizarUsuarioPorAdminResult(
            command.UsuarioId, usuario.AlteradoEm, refreshRemovidos, resetRemovidos, confirmacaoRemovidos);
    }
}
