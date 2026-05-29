namespace EasyStock.Application.UseCases.AlterarSenha;

public sealed class AlterarSenhaUseCase(
    IUsuarioRepository usuarioRepository,
    ICurrentUserAccessor currentUserAccessor,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    ILogger<AlterarSenhaUseCase> logger) : IUseCase<AlterarSenhaCommand, AlterarSenhaResult>
{
    public async Task<AlterarSenhaResult> ExecuteAsync(AlterarSenhaCommand command)
    {
        logger.LogInformation("Alterando senha do usuario atual");

        var usuarioId = currentUserAccessor.UsuarioId;
        if (usuarioId == Guid.Empty)
        {
            throw new UsuarioNaoAutorizadoException("Usuario nao autenticado.");
        }

        var usuario = await usuarioRepository.GetByIdAsync(usuarioId);
        if (usuario == null)
        {
            throw new RegraDeDominioVioladaException("Usuario nao encontrado.");
        }

        if (!passwordHasher.Verify(command.SenhaAtual, usuario.SenhaHash))
        {
            throw new CredenciaisInvalidasException("Senha atual incorreta.");
        }

        usuario.SenhaHash = passwordHasher.Hash(command.NovaSenha);
        usuario.AlteradoEm = DateTime.UtcNow;

        await usuarioRepository.UpdateAsync(usuario);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Senha alterada com sucesso para usuario {UsuarioId}", usuario.Id);
        return new AlterarSenhaResult(true);
    }
}