using EasyStock.Application.Services.Auth;
using FluentValidation;

namespace EasyStock.Application.UseCases.AlterarSenhaUsuario
{
    public sealed record AlterarSenhaCommand(
        Guid UsuarioId,
        string SenhaAtual,
        string NovaSenha);

    public class AlterarSenhaUsuarioUseCase(
        IUsuarioRepository usuarioRepository,
        IValidator<AlterarSenhaCommand> validator,
        RevogadorSessoes revogadorSessoes,
        IResetTokenRepository resetTokens,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ILogger<AlterarSenhaUsuarioUseCase> logger)
    {
        public async Task ExecuteAsync(AlterarSenhaCommand command)
        {
            logger.LogInformation("Alterando senha do usuario {UsuarioId}", command.UsuarioId);

            // Este caminho (PUT /usuarios/{id}/senha) nao passa por auto-validation MVC
            // (o command e montado no controller a partir do DTO), entao a politica de
            // senha e aplicada aqui — antes so o fluxo /auth/change-password validava (#767).
            var validacao = validator.Validate(command);
            if (!validacao.IsValid)
                throw new UseCaseValidationException(validacao.Errors[0].ErrorMessage);

            var usuario = await usuarioRepository.GetByIdAsync(command.UsuarioId)
                ?? throw new UseCaseValidationException("Usuario nao encontrado.");

            if (!passwordHasher.Verify(command.SenhaAtual, usuario.SenhaHash))
                throw new UseCaseValidationException("Senha atual incorreta.");

            usuario.SenhaHash = passwordHasher.Hash(command.NovaSenha);
            usuario.AlteradoEm = DateTime.UtcNow;

            await usuarioRepository.UpdateAsync(usuario);

            // #1352: a senha mudou, então toda sessão anterior do usuário cai.
            await revogadorSessoes.RevogarAsync(usuario);
            // #2569: link de reset e chave de acesso abertos caem junto, como no reset (#2201).
            await resetTokens.InvalidarAbertosAsync(usuario.Id, usuario.AlteradoEm);
            await resetTokens.InvalidarChavesDeAcessoAbertasAsync(usuario.Id);
            await unitOfWork.CommitAsync();
        }
    }
}
