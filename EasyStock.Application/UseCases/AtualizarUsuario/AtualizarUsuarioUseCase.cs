namespace EasyStock.Application.UseCases.AtualizarUsuario
{
    public sealed record AtualizarUsuarioCommand(
        Guid UsuarioId,
        string Nome,
        string? Email);

    public class AtualizarUsuarioUseCase(
        IUsuarioRepository usuarioRepository,
        ICurrentUserAccessor currentUser,
        IUnitOfWork unitOfWork,
        ILogger<AtualizarUsuarioUseCase> logger)
    {
        public async Task ExecuteAsync(AtualizarUsuarioCommand command)
        {
            logger.LogInformation("Atualizando usuario {UsuarioId}", command.UsuarioId);

            var usuario = await usuarioRepository.GetByIdAsync(command.UsuarioId)
                ?? throw new UseCaseValidationException("Usuario nao encontrado.");

            // Isolamento multi-tenant: Usuario nao tem EmpresaId (escapa do filtro
            // global e da RLS), e GetByIdAsync roda com bypass de RLS por ser reusado
            // no fluxo pre-auth de refresh-token. Sem esta guarda, um Admin de tenant
            // poderia alterar nome/e-mail (vetor de account takeover) de usuario de
            // OUTRO tenant via PUT /api/usuarios/{id} (#764). SuperAdmin e cross-tenant
            // por design. Mesma mensagem do not-found para nao confirmar existencia.
            if (currentUser.Nivel != NivelAcesso.SuperAdmin &&
                !usuario.Empresas.Any(ue => ue.EmpresaId == currentUser.EmpresaId))
            {
                throw new UseCaseValidationException("Usuario nao encontrado.");
            }

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
