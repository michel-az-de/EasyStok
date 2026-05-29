namespace EasyStock.Application.UseCases.AtualizarUsuario
{
    public sealed record AtualizarUsuarioCommand(
        Guid UsuarioId,
        string Nome,
        string? Email);

    public class AtualizarUsuarioUseCase(
        IUsuarioRepository usuarioRepository,
        IUnitOfWork unitOfWork,
        ILogger<AtualizarUsuarioUseCase> logger)
    {
        public async Task ExecuteAsync(AtualizarUsuarioCommand command)
        {
            logger.LogInformation("Atualizando usuario {UsuarioId}", command.UsuarioId);

            var usuario = await usuarioRepository.GetByIdAsync(command.UsuarioId)
                ?? throw new UseCaseValidationException("Usuario nao encontrado.");

            if (!string.IsNullOrWhiteSpace(command.Email) && command.Email != usuario.Email)
            {
                EmailValidator.EnsureValid(command.Email);

                var emailExistente = await usuarioRepository.GetByEmailAsync(command.Email);
                if (emailExistente is not null)
                    throw new UseCaseValidationException("Email ja cadastrado.");
                usuario.Email = command.Email.Trim();
            }

            usuario.Nome = command.Nome.Trim();
            usuario.AlteradoEm = DateTime.UtcNow;

            await usuarioRepository.UpdateAsync(usuario);
            await unitOfWork.CommitAsync();
        }
    }
}
