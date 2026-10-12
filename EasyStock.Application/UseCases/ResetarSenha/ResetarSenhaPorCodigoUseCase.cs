using EasyStock.Application.Services.Auth;
using EasyStock.Application.Validators;

namespace EasyStock.Application.UseCases.ResetarSenha;

/// <summary>
/// Reset de senha pelo código de 6 dígitos do WhatsApp (N8). Um código ativo por usuário, 10 min, 5 tentativas: cada
/// confirmação gasta uma tentativa num UPDATE condicional ANTES de comparar (10 chutes em paralelo contam no máximo 5), a
/// comparação é em tempo constante e o consumo é o mesmo UPDATE de uso único do link. Superadmin, conta inexistente,
/// sem código ativo, esgotado ou errado: a mesma recusa genérica.
/// </summary>
public sealed class ResetarSenhaPorCodigoUseCase(
    IResetTokenRepository resetTokenRepository,
    IUsuarioRepository usuarioRepository,
    ConcluidorDeReset concluidor,
    LimitePedidosAcesso limitePorIp,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    ILogger<ResetarSenhaPorCodigoUseCase> logger) : IUseCase<ResetarSenhaPorCodigoCommand, ResetarSenhaResult>
{
    internal const string MensagemDeRecusa = "Codigo invalido ou expirado.";

    public async Task<ResetarSenhaResult> ExecuteAsync(ResetarSenhaPorCodigoCommand command)
    {
        await limitePorIp.ExigirAsync(command.Ip);

        // Formato ruim não gasta tentativa e recebe a recusa genérica, sem revelar nada da conta.
        if (!SegredosDeAcesso.FormatoDeCodigoValido(command.Codigo))
            throw Recusa("formato de codigo invalido");

        var validacao = new ResetarSenhaPorCodigoCommandValidator().Validate(command);
        if (!validacao.IsValid)
            throw new UseCaseValidationException(string.Join(" ", validacao.Errors.Select(e => e.ErrorMessage)));

        var agora = relogio.GetUtcNow().UtcDateTime;

        var usuario = EmailValidator.IsValid(command.Email) ? await usuarioRepository.GetByEmailAsync(command.Email) : null;
        if (usuario is null || !usuario.Ativo || usuario.EhSuperAdmin())
            throw Recusa("conta sem direito ao codigo");

        var token = await resetTokenRepository.ObterAbertoAsync(usuario.Id, FinalidadeResetToken.ResetCodigo, agora);
        if (token is null)
            throw Recusa("sem codigo ativo", usuario.Id);

        // A tentativa é gasta antes de comparar: esgotado, usado ou expirado devolve 0 e a conferência nem acontece.
        if (await resetTokenRepository.RegistrarTentativaAsync(token.Id, agora) == 0)
            throw Recusa("tentativas esgotadas ou codigo vencido", usuario.Id);

        if (!SegredosDeAcesso.CodigoConfere(token.Id, command.Codigo, token.TokenHash))
            throw Recusa("codigo errado", usuario.Id);

        if (!await resetTokenRepository.ConsumirAsync(token.Id, agora))
            throw Recusa("codigo ja consumido", usuario.Id);

        await concluidor.ConcluirAsync(usuario, command.NovaSenha, "reset-password-code", command.Ip, command.UserAgent);
        await unitOfWork.CommitAsync();

        return new ResetarSenhaResult(true);
    }

    private RegraDeDominioVioladaException Recusa(string motivo, Guid? usuarioId = null)
    {
        logger.LogWarning("Reset por codigo recusado ({Motivo}) para o usuario {UsuarioId}", motivo, usuarioId);
        return new RegraDeDominioVioladaException(MensagemDeRecusa);
    }
}
