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
            await unitOfWork.CommitAsync();
        }
    }
}
