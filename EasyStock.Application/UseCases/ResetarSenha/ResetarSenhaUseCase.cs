using EasyStock.Application.Services.Auth;
using EasyStock.Application.Validators;

namespace EasyStock.Application.UseCases.ResetarSenha;

/// <summary>
/// Reset de senha pelo link do e-mail (N8). O uso único é de verdade: <c>ConsumirAsync</c> é um UPDATE condicional e só a
/// linha afetada igual a 1 autoriza trocar a senha (dois POST simultâneos com o mesmo token: um vence). O segredo de outra
/// finalidade, expirado ou já usado recebe a mesma recusa. O <c>Ip</c> do comando entra no teto de pedidos por IP.
/// </summary>
public sealed class ResetarSenhaUseCase(
    IResetTokenRepository resetTokenRepository,
    IUsuarioRepository usuarioRepository,
    ConcluidorDeReset concluidor,
    LimitePedidosAcesso limitePorIp,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    ILogger<ResetarSenhaUseCase> logger) : IUseCase<ResetarSenhaCommand, ResetarSenhaResult>
{
    internal const string MensagemDeRecusa = "Token invalido ou expirado.";

    public async Task<ResetarSenhaResult> ExecuteAsync(ResetarSenhaCommand command)
    {
        await limitePorIp.ExigirAsync(command.Ip);

        var validacao = new ResetarSenhaCommandValidator().Validate(command);
        if (!validacao.IsValid)
            throw new UseCaseValidationException(string.Join(" ", validacao.Errors.Select(e => e.ErrorMessage)));

        var agora = relogio.GetUtcNow().UtcDateTime;

        var token = await resetTokenRepository.GetByTokenAsync(command.Token);
        if (token is null || token.Finalidade != FinalidadeResetToken.Reset || token.Usado || token.ExpiraEm <= agora)
        {
            logger.LogWarning("Reset de senha recusado: token invalido, expirado ou de outra finalidade");
            throw new RegraDeDominioVioladaException(MensagemDeRecusa);
        }

        var usuario = await usuarioRepository.GetByIdAsync(token.UsuarioId);
        if (usuario is null || !usuario.Ativo)
        {
            logger.LogWarning("Reset de senha recusado: usuario {UsuarioId} inexistente ou inativo", token.UsuarioId);
            throw new RegraDeDominioVioladaException(MensagemDeRecusa);
        }

        // Consome ANTES de trocar a senha: quem perde a corrida do UPDATE sai aqui, sem tocar na conta.
        if (!await resetTokenRepository.ConsumirAsync(token.Id, agora))
        {
            logger.LogWarning("Reset de senha recusado: token do usuario {UsuarioId} ja consumido", usuario.Id);
            throw new RegraDeDominioVioladaException(MensagemDeRecusa);
        }

        await concluidor.ConcluirAsync(usuario, command.NovaSenha, "reset-password", command.Ip, command.UserAgent);
        await unitOfWork.CommitAsync();

        return new ResetarSenhaResult(true);
    }
}
